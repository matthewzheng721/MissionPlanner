using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.Comms;
using MissionPlanner.Controls;
using MissionPlanner.GCSViews.ConfigurationView;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace MissionPlannerTests.Utilities
{
    [TestClass, DoNotParallelize]
    public class ProjectMonitoringTests
    {
        private static void OnUi(Action<Form> action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                using (var owner = new Form { ShowInTaskbar = false, Opacity = 0 })
                {
                    owner.Shown += (sender, args) => owner.BeginInvoke((Action)(() =>
                    {
                        try { action(owner); }
                        catch (Exception ex) { failure = ex; }
                        finally { owner.Close(); }
                    }));
                    Application.Run(owner);
                }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.IsTrue(thread.Join(20000), "监视窗口测试未结束。");
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
        private static string ReadVisibleEcuMetric(Control panel, string title)
        {
            var heading = Descendants(panel).OfType<Label>().Single(label => label.Text == title);
            var value = heading.Parent.Controls.OfType<Label>().Single(label => label != heading);
            Assert.IsTrue(heading.Visible && value.Visible, "读数必须来自实际显示的指标。");
            return value.Text;
        }
        private sealed class ReadOnlyLink : IDisposable
        {
            private readonly FieldInfo field = typeof(MainV2).GetField("_comPort", BindingFlags.Static | BindingFlags.NonPublic);
            private readonly object previous;
            public readonly MAVLinkInterface Port = new MAVLinkInterface();
            public readonly CommsInjection Stream = new CommsInjection();
            public int Writes;
            public ReadOnlyLink()
            {
                previous = field.GetValue(null); field.SetValue(null, Port);
                Port.BaseStream = Stream; Port.sysidcurrent = 1; Port.compidcurrent = 1;
                Stream.WriteCallback += (sender, bytes) => Writes++;
            }
            public void Dispose() { field.SetValue(null, previous); Port.Dispose(); Stream.Dispose(); }
        }
        [TestMethod]
        public void MonitoringWindowsCoexistAndRestoreTheSameControls() => OnUi(owner =>
        {
            using (var link = new ReadOnlyLink())
            using (var original = new Panel())
            using (var status = new Panel())
            using (var curve = new Panel())
            {
                original.Controls.Add(status); original.Controls.Add(curve);
                var statusEvents = new List<bool>(); var curveEvents = new List<bool>();
                using (var windows = new ProjectMonitoringWindows(owner, status, curve, statusEvents.Add, curveEvents.Add))
                {
                    windows.ShowMenu(); windows.ShowStatus(); windows.ShowCurve(); windows.ShowEcu(); Application.DoEvents();
                    Assert.AreEqual(4, owner.OwnedForms.Count(form => form.Visible));
                    var statusWindow = status.FindForm(); var curveWindow = curve.FindForm();
                    Assert.AreNotSame(statusWindow, curveWindow);
                    windows.ShowStatus(); Assert.AreSame(statusWindow, status.FindForm());
                    Assert.AreEqual(1, statusEvents.Count);
                    statusWindow.Close(); Assert.AreSame(original, status.Parent); Assert.IsFalse(status.IsDisposed);
                    Assert.IsTrue(curveWindow.Visible); Assert.IsFalse(curve.IsDisposed);
                    windows.ShowStatus(); Assert.AreNotSame(statusWindow, status.FindForm());
                    Assert.IsFalse(status.IsDisposed);
                    CollectionAssert.AreEqual(new[] { true, false, true }, statusEvents.ToArray());
                    owner.OwnedForms.Single(form => form.Text == "更多监视").Close();
                    Assert.IsTrue(status.FindForm().Visible); Assert.IsTrue(curveWindow.Visible);
                }
                Assert.AreSame(original, status.Parent); Assert.AreSame(original, curve.Parent);
                Assert.IsFalse(status.IsDisposed); Assert.IsFalse(curve.IsDisposed);
                CollectionAssert.AreEqual(new[] { true, false }, curveEvents.ToArray());
                Assert.AreEqual(0, link.Writes, "只读监视不应请求或写入飞控。");
            }
        });
        [TestMethod]
        public void EcuUsesExactFieldsAndDoesNotConsumePacketQueue() => OnUi(owner =>
        {
            using (var link = new ReadOnlyLink()) using (var panel = new ProjectEcuMonitorPanel())
            {
                var mav = link.Port.MAV;
                mav.cs.messages.Add((DateTime.Now, "ECU Cy1T C1:170 C2:167 C3:154 C4:154"));
                mav.cs.messages.Add((DateTime.Now, "ECU ACK: throttle valid=1"));
                mav.cs.messages.Add((DateTime.Now, "ECU 原始告警文字"));
                var packet = new MAVLink.MAVLinkMessage(new MAVLink.MavlinkParse().GenerateMAVLinkPacket20(MAVLink.MAVLINK_MSG_ID.EFI_STATUS,
                    new MAVLink.mavlink_efi_status_t { ecu_index = 0, engine_load = 90, throttle_position = 23, rpm = 5000, fuel_pressure = 80, ignition_voltage = 14 }, false, 1, 1));
                mav.addPacket(packet); panel.RefreshValues();
                // 检查操作员实际可见的数值卡片，不能读取已经离开控件树的旧汇总标签。
                owner.Controls.Add(panel);
                Assert.AreEqual("23 %", ReadVisibleEcuMetric(panel, "油门量 · EFI 节气门位置"));
                Assert.AreEqual("5000 rpm", ReadVisibleEcuMetric(panel, "转速 · EFI"));
                Assert.AreEqual("170 ℃", ReadVisibleEcuMetric(panel, "最高缸温 · 四缸"));
                Assert.AreEqual("—", ReadVisibleEcuMetric(panel, "润滑油压"));
                Assert.AreEqual("—", ReadVisibleEcuMetric(panel, "ECU 电压"));
                StringAssert.Contains(panel.Controls.Find("ProjectEcuMessages", true).Single().Text, "ECU 原始告警文字");
                Assert.AreSame(packet, mav.getPacket((uint)MAVLink.MAVLINK_MSG_ID.EFI_STATUS), "监视读取不能消耗原状态接收队列。");
                link.Stream.Close(); panel.RefreshValues();
                foreach (var title in new[] { "油门量 · EFI 节气门位置", "转速 · EFI", "最高缸温 · 四缸", "润滑油压", "ECU 电压" })
                    Assert.AreEqual("—", ReadVisibleEcuMetric(panel, title), "断联后必须清除旧读数。");
                Assert.IsTrue(Descendants(panel).OfType<Label>().Any(label => label.Visible && label.Text.StartsWith("未连接飞控")));
                Assert.AreEqual(0, link.Writes);
            }
        });
        [TestMethod]
        public void OnboardCalibrationOnlyHostsLevelAndLargeCompass() => OnUi(owner =>
        {
            using (var link = new ReadOnlyLink()) using (var windows = new ProjectOnboardCalibrationWindows(owner))
            {
                windows.ShowMenu(); windows.ShowLevel(); windows.ShowCompass(); Application.DoEvents();
                var levelWindow = owner.OwnedForms.Single(form => form.Text == "校准水平");
                var compassWindow = owner.OwnedForms.Single(form => form.Text == "大飞机罗盘校准");
                var level = levelWindow.Controls.OfType<ConfigAccelerometerCalibration>().Single();
                var compass = compassWindow.Controls.OfType<ConfigHWCompass2>().Single();
                Assert.AreEqual(1, level.Controls.OfType<Button>().Count(button => button.Visible));
                Assert.AreEqual(1, compass.Controls.OfType<Button>().Count(button => button.Visible));
                foreach (var page in new UserControl[] { level, compass })
                    foreach (var button in page.Controls.OfType<Button>().Where(button => button.Visible))
                        Assert.IsTrue(button.Bottom <= page.ClientSize.Height, "原校准按钮应在独立宿主可视范围内。");
                windows.ShowLevel(); Assert.AreSame(levelWindow, owner.OwnedForms.Single(form => form.Text == "校准水平"));
                levelWindow.Close(); Assert.IsTrue(level.IsDisposed); Assert.IsFalse(compass.IsDisposed);
                windows.ShowLevel(); Assert.AreNotSame(levelWindow, owner.OwnedForms.Single(form => form.Text == "校准水平"));
                Assert.AreEqual(0, link.Writes, "打开或重开校准入口不能自动执行校准。");
            }
        });
    }
}
