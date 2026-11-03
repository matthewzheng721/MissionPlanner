using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Joystick;
using System;
using System.Reflection;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MissionPlanner.Comms;
using MissionPlanner.Utilities;

namespace MissionPlannerTests.Utilities
{
    [TestClass, DoNotParallelize]
    public class ProjectJoystickArmTests
    {
        private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);

        private sealed class ProbeJoystick : JoystickBase
        {
            public ProbeJoystick(Func<MissionPlanner.MAVLinkInterface> port) : base(port) { }
            public override bool AcquireJoystick(string name) => false;
            public override int getNumberPOV() => 0;
            public override void UnAcquireJoyStick() { }
            public override int getNumButtons() => 0;
            public override bool IsJoystickValid() => false;
            public override IMyJoystickState GetCurrentState() => null;
        }

        [DataTestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void RejectedArmOrDisarmNeverAccessesConnection(bool arm)
        {
            var previous = JoystickBase.ArmConfirmation;
            var context = SynchronizationContext.Current;
            var accesses = 0;
            var confirmations = 0;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                using (var joystick = new ProbeJoystick(() => { accesses++; return null; }))
                {
                    accesses = 0;
                    JoystickBase.ArmConfirmation = requested => { confirmations++; Assert.AreEqual(arm, requested); return false; };
                    var method = typeof(JoystickBase).GetMethod("ProcessButtonEvent", BindingFlags.Instance | BindingFlags.NonPublic);
                    var buttonType = method.GetParameters()[0].ParameterType;
                    var button = Activator.CreateInstance(buttonType);
                    buttonType.GetField("buttonno").SetValue(button, 0);
                    var function = buttonType.GetField("function");
                    function.SetValue(button, Enum.Parse(function.FieldType, arm ? "Arm" : "Disarm"));
                    method.Invoke(joystick, new[] { button, (object)true });
                    Assert.AreEqual(1, confirmations, "每次操作必须进入应用确认回调。");
                    Assert.AreEqual(0, accesses, "拒绝后不得读取接口或发送解锁/锁定。");
                }
            }
            finally
            {
                JoystickBase.ArmConfirmation = previous;
                SynchronizationContext.SetSynchronizationContext(context);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void RealPreflightDialogBlocksUncheckedOrCancelledArm(bool allChecked)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                var field = typeof(MissionPlanner.MainV2).GetField("_comPort", BindingFlags.Static | BindingFlags.NonPublic);
                var previousPort = field.GetValue(null);
                var previousCallback = JoystickBase.ArmConfirmation;
                try
                {
                    using (var port = new MissionPlanner.MAVLinkInterface())
                    using (var stream = new CommsInjection())
                    using (var owner = new Form())
                    using (var timer = new System.Windows.Forms.Timer { Interval = 30 })
                    {
                        field.SetValue(null, port); port.BaseStream = stream; port.sysidcurrent = 1; port.compidcurrent = 1;
                        ProjectPreflight.RefreshConnection();
                        ProjectPreflight.Checklist.SetPolarity(false, allChecked); ProjectPreflight.Checklist.SetPolarity(true, allChecked);
                        for (var i = 0; i < 8; i++) ProjectPreflight.Checklist.Set(i, allChecked ? ProjectCheckResult.Confirmed : ProjectCheckResult.Unchecked);
                        var ownerHandle = owner.Handle;
                        var seen = new System.Collections.Generic.HashSet<string>();
                        var accesses = 0; var answered = false;
                        using (var joystick = new ProbeJoystick(() => { accesses++; return null; }))
                        {
                            accesses = 0;
                            JoystickBase.ArmConfirmation = arm => ProjectPreflight.ConfirmArm(owner, arm);
                            // 只枚举当前测试线程的窗口，绝不点击其他应用或用户实例。
                            timer.Tick += (s, e) => EnumThreadWindows(GetCurrentThreadId(), (window, unused) =>
                            {
                                var title = new System.Text.StringBuilder(100); GetWindowText(window, title, title.Capacity);
                                if (seen.Add(title.ToString())) Console.WriteLine("本测试线程窗口：" + title);
                                if (title.ToString() != (allChecked ? "操作确认" : "尚未完成检查")) return true;
                                answered = true; timer.Stop(); var dialog = Control.FromHandle(window) as Form; if (allChecked && dialog != null && dialog.Name == "ProjectConfirmation") { Assert.AreSame(dialog.AcceptButton, dialog.CancelButton); dialog.CancelButton.PerformClick(); } else SendMessage(window, allChecked ? 0x111 : 0x10, allChecked ? (IntPtr)7 : IntPtr.Zero, IntPtr.Zero); return false;
                            }, IntPtr.Zero);
                            timer.Start();
                            var method = typeof(JoystickBase).GetMethod("ProcessButtonEvent", BindingFlags.Instance | BindingFlags.NonPublic);
                            var type = method.GetParameters()[0].ParameterType; var button = Activator.CreateInstance(type);
                            type.GetField("buttonno").SetValue(button, 0);
                            type.GetField("function").SetValue(button, Enum.Parse(type.GetField("function").FieldType, "Arm"));
                            method.Invoke(joystick, new[] { button, (object)true });
                            Assert.IsTrue(answered); Assert.AreEqual(0, accesses, "未核对或取消确认时不得到达解锁发送接口。");
                        }
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { field.SetValue(null, previousPort); JoystickBase.ArmConfirmation = previousCallback; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "解锁确认测试超时。");
            if (failure != null) throw failure;
        }
    }
}
