using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Controls;
using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlannerTests.Utilities
{
    [TestClass, DoNotParallelize]
    public class ProjectStatusLifecycleTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class ProbeStatus : Status
        {
            public int VisibilityChangesAfterDisposal;
            protected override void SetVisibleCore(bool value)
            {
                if (IsDisposed) VisibilityChangesAfterDisposal++;
                base.SetVisibleCore(value);
            }
        }

        private static void OnSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception error) { failure = error; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(15000), "Status 生命周期测试超时。");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static void PumpUntil(Func<bool> complete)
        {
            var end = DateTime.UtcNow.AddSeconds(3);
            while (!complete() && DateTime.UtcNow < end)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            Assert.IsTrue(complete(), "原 Status 界面委托未完成。");
        }

        private static System.Threading.Timer TimerOf(Status status) =>
            (System.Threading.Timer)typeof(Status).GetField("_hidetimer", Private).GetValue(status);

        private static void Invoke(Status status, string method, params object[] args)
        {
            try { typeof(Status).GetMethod(method, Private).Invoke(status, args); }
            catch (TargetInvocationException error)
            {
                ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw();
                throw;
            }
        }

        [TestMethod]
        public void DisposedStatusStopsTimerAndIgnoresItsLateCallback() => OnSta(() =>
        {
            // 仅构造未显示的子控件并处理其消息，不启动主窗、不占用桌面。
            var status = new ProbeStatus();
            var timer = TimerOf(status);
            status.Dispose();
            Assert.IsTrue(status.IsDisposed);
            Assert.IsFalse(status.IsHandleCreated);
            Assert.IsNull(TimerOf(status));
            Assert.ThrowsException<ObjectDisposedException>(() => timer.Change(1, Timeout.Infinite));

            // 调用计时器实际绑定的回调，模拟 Dispose 前已经进入线程池的最后一次执行。
            var late = Task.Run(() => Invoke(status, "HideAfterDelay", new object[] { null }));
            Assert.IsTrue(late.Wait(3000), "销毁后回调应立即结束。");
            late.GetAwaiter().GetResult();
            Application.DoEvents();
            Assert.AreEqual(0, status.VisibilityChangesAfterDisposal);
            Assert.IsFalse(status.IsHandleCreated, "过期回调不得重建窗口句柄。");
        });

        [TestMethod]
        public void QueuedStatusVisibilityDoesNotReviveDisposedControl() => OnSta(() =>
        {
            var status = new ProbeStatus();
            status.Percent = 35;
            // Percent 已真实调用 BeginInvoke；在处理界面队列之前关闭。
            status.Dispose();
            Application.DoEvents();
            // 覆盖委托已经取出、未被 WinForms 清队列取消时的执行体。
            Invoke(status, "ApplyVisibilityIfAlive", true);
            Invoke(status, "ApplyVisibilityIfAlive", false);
            status.Percent = 75;
            Assert.AreEqual(35d, status.Percent, "关闭后的迟到更新不再修改控件状态。");
            Assert.AreEqual(0, status.VisibilityChangesAfterDisposal);
            Assert.IsTrue(status.IsDisposed);
            Assert.IsFalse(status.IsHandleCreated);
        });

        [TestMethod]
        public void ActiveStatusKeepsPercentValidationAndShowHideBehavior() => OnSta(() =>
        {
            using (var status = new Status())
            {
                // 构造器原 1ms 回调自然隐藏，再观察原 Percent 的显示委托。
                PumpUntil(() => !status.Visible);
                status.Percent = 42;
                PumpUntil(() => status.Visible);
                Assert.AreEqual(42d, status.Percent);
                status.Percent = -1;
                status.Percent = 101;
                Assert.AreEqual(42d, status.Percent, "原0至100范围检查保持。");

                var hide = Task.Run(() => Invoke(status, "HideAfterDelay", new object[] { null }));
                Assert.IsTrue(hide.Wait(3000));
                hide.GetAwaiter().GetResult();
                PumpUntil(() => !status.Visible);
                Assert.AreEqual(42d, status.Percent, "隐藏不能清空原进度值。");
            }
        });

        [TestMethod]
        public void PercentUpdatesRacingDisposeDoNotUseReleasedTimer() => OnSta(() =>
        {
            var status = new Status();
            using (var firstUpdate = new ManualResetEventSlim())
            {
                var update = Task.Run(() =>
                {
                    status.Percent = 1;
                    firstUpdate.Set();
                    for (var i = 0; i < 2000; i++)
                    {
                        status.Percent = i % 101;
                        Thread.Yield();
                    }
                });
                try
                {
                    Assert.IsTrue(firstUpdate.Wait(3000), "并发更新未开始。");
                    status.Dispose();
                    Assert.IsTrue(update.Wait(3000), "销毁与更新并发时不应等待或重试。");
                    update.GetAwaiter().GetResult();
                    Application.DoEvents();
                    Assert.IsNull(TimerOf(status));
                    Assert.IsFalse(status.IsHandleCreated);
                }
                finally { status.Dispose(); }
            }
        });

        [TestMethod]
        public void LiveStatusDoesNotHideUnexpectedTimerDisposal() => OnSta(() =>
        {
            using (var status = new Status())
            {
                PumpUntil(() => !status.Visible);
                // 单独破坏仍存活控件的计时器，证明异常过滤只接受关闭竞态。
                TimerOf(status).Dispose();
                Assert.IsFalse(status.IsDisposed);
                Assert.ThrowsException<ObjectDisposedException>(() => status.Percent = 20);
            }
        });
    }
}
