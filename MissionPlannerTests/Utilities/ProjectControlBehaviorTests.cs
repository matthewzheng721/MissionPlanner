using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.Comms;
using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;
using MissionPlanner.GCSViews.ConfigurationView;
using MissionPlanner.Utilities;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlannerTests.Utilities
{
    [TestClass, DoNotParallelize]
    public class ProjectControlBehaviorTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static void Pump(Func<bool> done)
        {
            var end = DateTime.UtcNow.AddSeconds(8);
            while (!done() && DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); }
            Assert.IsTrue(done(), "等待离线控制操作超时。");
        }
        private static void OnUi(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                // 使用真实 WinForms 消息循环；裸 STA + DoEvents 会在模态退出后丢失 UI 上下文。
                using (var host = new Form { Text = "V05 离线测试宿主", ShowInTaskbar = false, Opacity = 0 })
                {
                    host.Shown += (sender, args) => host.BeginInvoke((Action)(() =>
                    {
                        host.Hide();
                        try
                        {
                            Assert.IsInstanceOfType(SynchronizationContext.Current, typeof(WindowsFormsSynchronizationContext));
                            action();
                        }
                        catch (Exception ex) { failure = ex; }
                        finally { host.Close(); }
                    }));
                    Application.Run(host);
                }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            Assert.IsTrue(thread.Join(30000), "UI 测试未结束。"); if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        private delegate bool WindowCallback(IntPtr window, IntPtr value);
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        private static void Answer(bool yes, Action operation, Action beforeAnswer = null)
        {
            using (var timer = new System.Windows.Forms.Timer { Interval = 30 })
            {
                bool answered = false; var testThread = GetCurrentThreadId();
                timer.Tick += (s, e) => { var window = IntPtr.Zero; EnumThreadWindows(testThread, (candidate, value) => { var title = new StringBuilder(80); GetWindowText(candidate, title, title.Capacity); if (title.ToString() == "操作确认") { window = candidate; return false; } return true; }, IntPtr.Zero); if (window == IntPtr.Zero) return; answered = true; timer.Stop(); beforeAnswer?.Invoke(); var dialog = Control.FromHandle(window) as Form; if (dialog != null && dialog.Name == "ProjectConfirmation") { Assert.AreSame(dialog.AcceptButton, dialog.CancelButton, "默认操作必须仍为取消。"); var buttons = dialog.Controls.OfType<FlowLayoutPanel>().SelectMany(row => row.Controls.OfType<Button>()); buttons.First(button => button.DialogResult == (yes ? DialogResult.Yes : DialogResult.Cancel)).PerformClick(); } else SendMessage(window, 0x111, (IntPtr)(yes ? 6 : 7), IntPtr.Zero); };
                timer.Start(); operation(); Assert.IsInstanceOfType(SynchronizationContext.Current, typeof(WindowsFormsSynchronizationContext)); Assert.IsTrue(answered, "预期的执行前确认未出现。");
            }
        }
        private sealed class Link : IDisposable
        {
            private readonly FieldInfo mainPort = typeof(MainV2).GetField("_comPort", BindingFlags.Static | BindingFlags.NonPublic);
            private readonly object previous;
            public readonly MAVLinkInterface Port = new MAVLinkInterface();
            public readonly CommsInjection Stream = new CommsInjection();
            public readonly ConcurrentQueue<MAVLink.MAVLinkMessage> Sent = new ConcurrentQueue<MAVLink.MAVLinkMessage>();
            public bool Acknowledge = true;
            public Link()
            {
                previous = mainPort.GetValue(null); mainPort.SetValue(null, Port);
                Port.BaseStream = Stream; Port.sysidcurrent = 1; Port.compidcurrent = 1;
                Port.MAV.apname = MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA;
                Port.MAV.cs.mode = "FBWA"; Port.MAV.cs.firmware = Firmwares.ArduPlane;
                Stream.WriteCallback += (s, bytes) =>
                {
                    var message = new MAVLink.MAVLinkMessage(bytes.ToArray()); Sent.Enqueue(message);
                    if (Acknowledge && message.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG)
                    {
                        var command = message.ToStructure<MAVLink.mavlink_command_long_t>();
                        // COMMAND_ACK 含扩展字段，使用 MAVLink2；测试生成器的 v1 方法不裁剪扩展，会被真实解析器拒绝。
                        var ack = new MAVLink.MavlinkParse().GenerateMAVLinkPacket20(MAVLink.MAVLINK_MSG_ID.COMMAND_ACK,
                            new MAVLink.mavlink_command_ack_t { command = command.command, result = (byte)MAVLink.MAV_RESULT.ACCEPTED }, false, 1, 1);
                        var received = (EventHandler<MAVLink.MAVLinkMessage>)typeof(MAVLinkInterface).GetField("_OnPacketReceived", Private).GetValue(Port);
                        received?.Invoke(Port, new MAVLink.MAVLinkMessage(ack));
                        Stream.AppendBuffer(ack);
                    }
                };
                Param("SERVO5_TRIM", 2000); Param("RCMAP_PITCH", 2); Param("RCMAP_ROLL", 1);
                for (var channel = 1; channel <= 2; channel++) { Param("RC" + channel + "_MIN", 1000); Param("RC" + channel + "_TRIM", 1510); Param("RC" + channel + "_MAX", 1900); }
                Param("RC1_REVERSED", 0); Param("RC2_REVERSED", 0);
                Param("SDT_RUN", 0); Param("SDT_FRAC", 0.7);
            }
            public void Param(string name, double value) { Port.MAV.param.Add(new MAVLink.MAVLinkParam(name, value, MAVLink.MAV_PARAM_TYPE.REAL32)); Port.MAV.param_types[name] = MAVLink.MAV_PARAM_TYPE.REAL32; }
            public MAVLink.mavlink_param_set_t[] Writes => Sent.Where(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.PARAM_SET).Select(x => x.ToStructure<MAVLink.mavlink_param_set_t>()).ToArray();
            public MAVLink.mavlink_rc_channels_override_t[] Inputs => Sent.Where(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_OVERRIDE).Select(x => x.ToStructure<MAVLink.mavlink_rc_channels_override_t>()).ToArray();
            public void Dispose() { mainPort.SetValue(null, previous); Port.Dispose(); Stream.Dispose(); }
        }
        private static void Prepare(ConfigProjectMotor motor)
        {
            var handle = motor.Handle; motor.Activate(); ((System.Windows.Forms.Timer)Get(motor, "readTimer")).Stop();
            Pump(() => !(bool)Get(motor, "readInProgress"));
        }
        [TestMethod]
        public void EngineCancelSendsNothingAndRestoreNeedsNoConfirmation() => OnUi(() =>
        {
            using (var link = new Link()) using (var motor = new ConfigProjectMotor())
            {
                Prepare(motor); var before = link.Sent.Count;
                Answer(false, motor.StartEngine); Answer(false, motor.StopEngine);
                Assert.AreEqual(before, link.Sent.Count);
                motor.Restore(); Pump(() => link.Writes.Length == 1);
                Assert.AreEqual(2000f, link.Writes[0].param_value);
                Assert.IsFalse(link.Sent.Any(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG));
            }
        });
        [TestMethod]
        public void EngineStartPrecedesTrimAndStopIsIndependent() => OnUi(() =>
        {
            using (var link = new Link()) using (var motor = new ConfigProjectMotor())
            {
                Prepare(motor); Answer(true, motor.StartEngine); Pump(() => link.Writes.Any(x => x.param_value == 1300));
                var commands = link.Sent.Where(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG || x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.PARAM_SET).ToArray();
                Assert.AreEqual((ushort)MAVLink.MAV_CMD.DO_ENGINE_CONTROL, commands[0].ToStructure<MAVLink.mavlink_command_long_t>().command);
                Assert.AreEqual(1f, commands[0].ToStructure<MAVLink.mavlink_command_long_t>().param1);
                Assert.AreEqual((uint)MAVLink.MAVLINK_MSG_ID.PARAM_SET, commands[1].msgid);
                Answer(true, motor.StopEngine); Pump(() => link.Sent.Count(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG) == 2);
                Assert.AreEqual(1, link.Writes.Length, "停车不应自动写回 TRIM。");
                Assert.AreEqual(0f, link.Sent.Last(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG).ToStructure<MAVLink.mavlink_command_long_t>().param1);
            }
        });
        [TestMethod]
        public void QueuedMotorOperationsDoNotCrossReplacementStream() => OnUi(() =>
        {
            using (var link = new Link()) using (var motor = new ConfigProjectMotor()) using (var replacement = new CommsInjection())
            {
                Prepare(motor); var gate = new TaskCompletionSource<bool>();
                var queue = typeof(ConfigProjectMotor).GetField("queuedWrite", BindingFlags.Static | BindingFlags.NonPublic);
                queue.SetValue(null, gate.Task);
                Answer(true, motor.StartEngine); motor.Restore(); Answer(true, motor.StopEngine);
                int sentToReplacement = 0; replacement.WriteCallback += (s, b) => Interlocked.Increment(ref sentToReplacement);
                link.Port.BaseStream = replacement; gate.SetResult(true);
                Pump(() => ((Task)queue.GetValue(null)).IsCompleted);
                Assert.AreEqual(0, sentToReplacement); Assert.AreEqual(0, link.Writes.Length);
                queue.SetValue(null, Task.CompletedTask);
            }
        });
        [TestMethod]
        public void MissingEngineAckAfterReplacementNeverRetriesOnNewStream() => OnUi(() =>
        {
            using (var link = new Link()) using (var motor = new ConfigProjectMotor()) using (var replacement = new CommsInjection())
            {
                Prepare(motor); link.Acknowledge = false;
                Answer(true, motor.StartEngine);
                Pump(() => link.Sent.Any(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG));
                int packets = 0; replacement.WriteCallback += (s, b) => Interlocked.Increment(ref packets);
                link.Port.BaseStream = replacement;
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                Pump(() => elapsed.ElapsedMilliseconds > 2300);
                Assert.AreEqual(0, packets, "等待超过原重试周期，新连接不得收到旧命令。");
                Assert.AreEqual(0, link.Writes.Length);
            }
        });
        [TestMethod]
        public void ChangingConnectionWhileConfirmingPreventsStartAndStop() => OnUi(() =>
        {
            using (var link = new Link()) using (var motor = new ConfigProjectMotor()) using (var replacement = new CommsInjection())
            {
                Prepare(motor); int packets = 0; replacement.WriteCallback += (s, b) => Interlocked.Increment(ref packets);
                Answer(true, motor.StartEngine, () => link.Port.BaseStream = replacement);
                Assert.AreEqual(0, packets); Assert.AreEqual(0, link.Writes.Length);
                link.Port.BaseStream = link.Stream; link.Stream.Open();
                Answer(true, motor.StopEngine, () => link.Port.BaseStream = replacement);
                Assert.AreEqual(0, packets);
                Assert.IsFalse(link.Sent.Any(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG));
            }
        });
        [TestMethod]
        public void RestoreAndStopCancelMissingStartAckPromptly() => OnUi(() =>
        {
            using (var link = new Link()) using (var motor = new ConfigProjectMotor())
            {
                Prepare(motor); link.Acknowledge = false; Answer(true, motor.StartEngine);
                Pump(() => link.Sent.Any(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG));
                var elapsed = System.Diagnostics.Stopwatch.StartNew(); motor.Restore();
                Pump(() => link.Writes.Any(x => x.param_value == 2000));
                Console.WriteLine("缺 ACK 时恢复发送延迟：" + elapsed.ElapsedMilliseconds + " ms");
                Assert.IsTrue(elapsed.ElapsedMilliseconds < 1000); Assert.IsFalse(link.Writes.Any(x => x.param_value == 1300));
                Answer(true, motor.StartEngine);
                Pump(() => link.Sent.Count(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG) >= 2);
                elapsed.Restart(); Answer(true, motor.StopEngine);
                Pump(() => link.Sent.Any(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG && x.ToStructure<MAVLink.mavlink_command_long_t>().param1 == 0));
                Console.WriteLine("缺 ACK 时停车发送延迟（含确认框）：" + elapsed.ElapsedMilliseconds + " ms");
                Assert.IsTrue(elapsed.ElapsedMilliseconds < 1000); Assert.IsFalse(link.Writes.Any(x => x.param_value == 1300));
            }
        });
        [TestMethod]
        public void StopRetainsAckRetryAndRestoreCanInterruptWait() => OnUi(() =>
        {
            using (var link = new Link()) using (var motor = new ConfigProjectMotor())
            {
                Prepare(motor); link.Acknowledge = false;
                var elapsed = System.Diagnostics.Stopwatch.StartNew(); Answer(true, motor.StopEngine);
                Pump(() => link.Sent.Count(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG) >= 2);
                Assert.IsTrue(elapsed.ElapsedMilliseconds >= 1900);
                var commands = link.Sent.Where(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG).Select(x => x.ToStructure<MAVLink.mavlink_command_long_t>()).ToArray();
                Assert.AreEqual((byte)0, commands[0].confirmation); Assert.AreEqual((byte)1, commands[1].confirmation);
                elapsed.Restart(); motor.Restore(); Pump(() => link.Writes.Any(x => x.param_value == 2000));
                Console.WriteLine("停车等待 ACK 时恢复发送延迟：" + elapsed.ElapsedMilliseconds + " ms");
                Assert.IsTrue(elapsed.ElapsedMilliseconds < 1000);
            }
        });
        private static void PreparePolarity(Link link, ProjectPolarityPanel panel)
        {
            link.Port.MAV.cs.mode = "MANUAL";
            var bytes = new MAVLink.MavlinkParse().GenerateMAVLinkPacket10(MAVLink.MAVLINK_MSG_ID.HEARTBEAT, new MAVLink.mavlink_heartbeat_t(), 1, 1);
            var msg = new MAVLink.MAVLinkMessage(bytes) { rxtime = DateTime.UtcNow }; link.Port.MAV.addPacket(msg);
            Call(panel, "ChangeView", true);
        }
        [TestMethod]
        public void PolarityHoldsCentersReleasesOnFocusLossAndAuto() => OnUi(() =>
        {
            using (var link = new Link()) using (var panel = new ProjectPolarityPanel())
            {
                var handle = panel.Handle; PreparePolarity(link, panel); ((NumericUpDown)Get(panel, "fraction")).Value = 10;
                Call(panel, "Begin", "PITCH", 1); Pump(() => link.Inputs.Length >= 2);
                Assert.AreEqual((ushort)1549, link.Inputs.Last().chan2_raw);
                Assert.AreEqual((ushort)1510, link.Inputs.Last().chan1_raw);
                Call(panel, "Center"); Pump(() => link.Inputs.Last().chan2_raw == 1510);
                Call(panel, "HostDeactivated", null, EventArgs.Empty); Pump(() => link.Inputs.Last().chan2_raw == 0);
                Call(panel, "Begin", "PITCH", -1); Pump(() => link.Inputs.Last().chan2_raw == 1471);
                var auto = panel.Controls[0].Controls[1].Controls.OfType<Button>().Single(b => b.Text == "AUTO"); auto.PerformClick();
                Pump(() => link.Inputs.Last().chan2_raw == 0);
                Assert.IsFalse((bool)Get(panel, "held"));
                Call(panel, "Begin", "PITCH", 1); Pump(() => link.Inputs.Last().chan2_raw == 1549);
                panel.Dispose(); Pump(() => link.Inputs.Last().chan2_raw == 0);
            }
        });
        [TestMethod]
        public void CenteredPolarityReleasesOnExternalModeChange() => OnUi(() =>
        {
            using (var link = new Link()) using (var panel = new ProjectPolarityPanel())
            {
                var handle = panel.Handle; PreparePolarity(link, panel); ((NumericUpDown)Get(panel, "fraction")).Value = 10;
                Call(panel, "Begin", "PITCH", 1); Pump(() => link.Inputs.Length > 0);
                Call(panel, "Center"); Pump(() => link.Inputs.Last().chan2_raw == 1510);
                link.Port.MAV.cs.mode = "AUTO"; Call(panel, "RefreshState");
                Pump(() => link.Inputs.Last().chan2_raw == 0 && link.Inputs.Last().chan1_raw == 0);
            }
        });
        [TestMethod]
        public void QueuedPolarityIsCancelledOnCloseAndNeverSentToNewStream() => OnUi(() =>
        {
            using (var link = new Link()) using (var replacement = new CommsInjection())
            {
                var panel = new ProjectPolarityPanel(); var handle = panel.Handle; PreparePolarity(link, panel);
                ((NumericUpDown)Get(panel, "fraction")).Value = 10;
                var gate = new TaskCompletionSource<bool>(); Set(panel, "pending", gate.Task);
                Call(panel, "Begin", "PITCH", 1); Call(panel, "Center"); panel.Dispose();
                int newPackets = 0; replacement.WriteCallback += (s, b) => Interlocked.Increment(ref newPackets);
                link.Port.BaseStream = replacement; gate.SetResult(true);
                Pump(() => ((Task)Get(panel, "pending")).IsCompleted);
                Assert.AreEqual(0, newPackets); Assert.AreEqual(0, link.Inputs.Length);
            }
        });
        [TestMethod]
        public void ServoRunStopPacketsAndSubscriptionLifetime() => OnUi(() =>
        {
            using (var link = new Link())
            {
                var eventField = typeof(MAVLinkInterface).GetField("_OnPacketReceived", Private);
                Func<int> subscribers = () => ((Delegate)eventField.GetValue(link.Port))?.GetInvocationList().Count(x => x.Method.Name == "StateReceived") ?? 0;
                var panel = new ProjectServoTestPanel(); var handle = panel.Handle;
                Assert.AreEqual(1, subscribers());
                var run = (Task)Call(panel, "Command", 1); var stop = (Task)Call(panel, "Command", 2);
                Pump(() => run.IsCompleted && stop.IsCompleted);
                CollectionAssert.AreEqual(new[] { 1f, 2f }, link.Writes.Select(x => x.param_value).ToArray());
                var bytes = new MAVLink.MavlinkParse().GenerateMAVLinkPacket10(MAVLink.MAVLINK_MSG_ID.PARAM_VALUE,
                    new MAVLink.mavlink_param_value_t { param_id = Encoding.ASCII.GetBytes("SDT_STATE".PadRight(16, '\0')), param_value = 3 }, 1, 1);
                ((EventHandler<MAVLink.MAVLinkMessage>)eventField.GetValue(link.Port)).Invoke(link.Port, new MAVLink.MAVLinkMessage(bytes));
                Application.DoEvents(); StringAssert.Contains(((Label)Get(panel, "status")).Text, "最新回读：3");
                panel.Dispose(); Assert.AreEqual(0, subscribers());
            }
        });
        private static void FillServoParameters(Link link)
        {
            foreach (var name in ProjectServoPreview.Names)
            {
                if (link.Port.MAV.param.ContainsKey("SDT_" + name)) continue;
                double value = name == "MIN" ? 1000 : name == "MAX" ? 1450 : name == "PERIOD" ? .4 : name == "DURATION" ? 10 : name == "STEP" ? 100 : 0;
                link.Param("SDT_" + name, value);
            }
            for (var i = 1; i <= 4; i++) { link.Param("SERVO" + i + "_MIN", 1000); link.Param("SERVO" + i + "_TRIM", 1500); link.Param("SERVO" + i + "_MAX", 2000); }
        }
        private static void EchoParameter(Link link, MAVLink.mavlink_param_set_t command)
        {
            var response = new MAVLink.MavlinkParse().GenerateMAVLinkPacket10(MAVLink.MAVLINK_MSG_ID.PARAM_VALUE,
                new MAVLink.mavlink_param_value_t { param_id = command.param_id, param_value = command.param_value, param_type = (byte)MAVLink.MAV_PARAM_TYPE.REAL32 }, 1, 1);
            ((EventHandler<MAVLink.MAVLinkMessage>)typeof(MAVLinkInterface).GetField("_OnPacketReceived", Private).GetValue(link.Port))?.Invoke(link.Port, new MAVLink.MAVLinkMessage(response));
        }
        [TestMethod]
        public void ServoParameterWriteConfirmsWithoutTakingReceiver() => OnUi(() =>
        {
            using (var link = new Link())
            {
                FillServoParameters(link);
                link.Stream.WriteCallback += (sender, bytes) =>
                {
                    var message = new MAVLink.MAVLinkMessage(bytes.ToArray());
                    if (message.msgid == (uint)MAVLink.MAVLINK_MSG_ID.PARAM_SET) { Assert.IsFalse(link.Port.giveComport); EchoParameter(link, message.ToStructure<MAVLink.mavlink_param_set_t>()); }
                };
                using (var panel = new ProjectServoTestPanel())
                {
                    var handle = panel.Handle; var task = (Task)Call(panel, "Apply"); Pump(() => task.IsCompleted);
                    Assert.IsTrue((bool)Get(panel, "written")); Assert.AreEqual(15, link.Writes.Length);
                }
            }
        });
        [TestMethod]
        public void ServoVisibleEditorsSynchronizeWithoutSendingUntilApply() => OnUi(() =>
        {
            using (var link = new Link())
            {
                FillServoParameters(link);
                link.Stream.WriteCallback += (sender, bytes) => { var message = new MAVLink.MAVLinkMessage(bytes.ToArray()); if (message.msgid == (uint)MAVLink.MAVLINK_MSG_ID.PARAM_SET) EchoParameter(link, message.ToStructure<MAVLink.mavlink_param_set_t>()); };
                using (var panel = new ProjectServoTestPanel())
                {
                    var handle = panel.Handle;
                    var editors = (System.Collections.Generic.Dictionary<string, Control>)Get(panel, "editors");
                    var grid = (DataGridView)Get(panel, "grid");
                    ((TextBox)editors["SDT_FRAC"]).Text = "0.3";
                    var fraction = grid.Rows.Cast<DataGridViewRow>().Single(row => (string)row.Cells[0].Value == "SDT_FRAC");
                    Assert.AreEqual("0.3", fraction.Cells[1].Value);
                    var mode = grid.Rows.Cast<DataGridViewRow>().Single(row => (string)row.Cells[0].Value == "SDT_MODE");
                    mode.Cells[1].Value = "2";
                    Assert.AreEqual(2, ((ComboBox)editors["SDT_MODE"]).SelectedIndex);
                    Assert.AreEqual(0, link.Writes.Length, "编辑和预览不能发送参数。");
                    var task = (Task)Call(panel, "Apply"); Assert.IsTrue(editors.Values.All(editor => !editor.Enabled), "写入期间主编辑框必须与参数表一起禁用。"); Pump(() => task.IsCompleted);
                    Assert.IsTrue(editors.Values.All(editor => editor.Enabled));
                    Assert.IsTrue((bool)Get(panel, "written"));
                    Assert.AreEqual(0.3f, link.Writes.Single(write => Encoding.ASCII.GetString(write.param_id).TrimEnd('\0') == "SDT_FRAC").param_value);
                }
            }
        });
        [TestMethod]
        public void ServoPartialWriteTimeoutNeverReportsWholeSetWritten() => OnUi(() =>
        {
            using (var link = new Link())
            {
                FillServoParameters(link);
                link.Stream.WriteCallback += (sender, bytes) =>
                {
                    var message = new MAVLink.MAVLinkMessage(bytes.ToArray()); if (message.msgid != (uint)MAVLink.MAVLINK_MSG_ID.PARAM_SET) return;
                    var command = message.ToStructure<MAVLink.mavlink_param_set_t>();
                    if (Encoding.ASCII.GetString(command.param_id).TrimEnd('\0') != "SDT_PERIOD") EchoParameter(link, command);
                };
                using (var panel = new ProjectServoTestPanel())
                {
                    var handle = panel.Handle; var task = (Task)Call(panel, "Apply"); Pump(() => task.IsCompleted);
                    Assert.IsFalse((bool)Get(panel, "written")); StringAssert.Contains(((Label)Get(panel, "status")).Text, "可能已有部分生效");
                    Assert.AreEqual(5, link.Writes.Length); Assert.IsFalse(link.Port.giveComport);
                }
            }
        });
        [TestMethod]
        public void ServoParameterRetryNeverMovesToReplacementStream() => OnUi(() =>
        {
            using (var link = new Link()) using (var replacement = new CommsInjection())
            {
                FillServoParameters(link);
                using (var panel = new ProjectServoTestPanel())
                {
                    var handle = panel.Handle; var task = (Task)Call(panel, "Apply"); Pump(() => link.Writes.Length == 1);
                    int count = 0; replacement.WriteCallback += (s, b) => Interlocked.Increment(ref count); link.Port.BaseStream = replacement;
                    Pump(() => task.IsCompleted); Assert.IsFalse((bool)Get(panel, "written")); Assert.AreEqual(0, count);
                }
            }
        });
        [TestMethod]
        public void EcuKnownMessagesUpdateWithoutInventingMissingValues()
        {
            var ecu = new ProjectEcuStatus(); Assert.IsNull(ecu.Temperatures); Assert.IsNull(ecu.ThrottleValid);
            var now = DateTime.Now;
            ecu.Observe("ECU Cy1T C1:170 C2:167 C3:154 C4:154", now);
            ecu.Observe("ECU ACK: throttle valid=1", now);
            CollectionAssert.AreEqual(new[] { 170d, 167d, 154d, 154d }, ecu.Temperatures); Assert.AreEqual(1, ecu.ThrottleValid);
            ecu.Observe("ECU Cy1T C1:40 C2:41 C3:42 C4:43", now.AddSeconds(1));
            ecu.Observe("ECU ACK: throttle valid=0", now.AddSeconds(1));
            ecu.Observe("ECU ACK: throttle valid=1", now.AddSeconds(-1));
            Assert.AreEqual(40d, ecu.Temperatures[0]); Assert.AreEqual(0, ecu.ThrottleValid);
        }
        [TestMethod]
        public void WizardEndsWithPerItemSummaryAndFinish() => OnUi(() =>
        {
            using (var link = new Link()) using (var wizard = new ProjectPreflightWizard())
            {
                ProjectPreflight.RefreshConnection(); ProjectPreflight.Checklist.Set(0, ProjectCheckResult.Confirmed); ProjectPreflight.Checklist.Set(1, ProjectCheckResult.Skipped);
                for (var i = 0; i < 5; i++) Call(wizard, "Next");
                Assert.AreEqual(5, Get(wizard, "index"));
                var text = ((Label)Get(wizard, "readings")).Text;
                StringAssert.Contains(text, "基础状态检查：已核对"); StringAssert.Contains(text, "ECU 与缸温：已跳过"); StringAssert.Contains(text, "空速计：未核对");
                Assert.AreEqual("完成", ((Button)Get(wizard, "nextButton")).Text);
            }
        });
        private static AuxOptions PrepareAux(int option)
        {
            var aux = new AuxOptions(); var handle = aux.Handle;
            var combo = (ComboBox)Get(aux, "mavlinkComboBox1");
            combo.BindingContext = new BindingContext(); combo.DisplayMember = "Value"; combo.ValueMember = "Key";
            combo.DataSource = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, string>> { new System.Collections.Generic.KeyValuePair<int, string>(option, "测试选项") };
            combo.SelectedValue = option;
            ((TextBox)Get(aux, "TXT_low_value")).Text = "0"; ((TextBox)Get(aux, "TXT_highvalue")).Text = "2";
            Call(aux, "UpdateProjectAvailability"); return aux;
        }
        private static void ConfirmAllChecks()
        {
            ProjectPreflight.RefreshConnection();
            ProjectPreflight.Checklist.SetPolarity(false, true); ProjectPreflight.Checklist.SetPolarity(true, true);
            for (var index = 0; index < 8; index++) ProjectPreflight.Checklist.Set(index, ProjectCheckResult.Confirmed);
        }
        [DataTestMethod]
        [DataRow(41)] [DataRow(153)] [DataRow(154)] [DataRow(165)]
        public void AuxArmCannotBypassChecksAndCancelSendsNothing(int option) => OnUi(() =>
        {
            using (var link = new Link()) using (var aux = PrepareAux(option))
            {
                Assert.IsFalse(((Button)Get(aux, "BUT_High")).Enabled);
                Call(aux, "BUT_High_Click", null, EventArgs.Empty); Assert.AreEqual(0, link.Sent.Count);
                // 改名或把低按钮的实际档位改成2，不应绕过相同的解锁判定。
                ((TextBox)Get(aux, "TXT_low_value")).Text = "2";
                Call(aux, "UpdateProjectAvailability"); Assert.IsFalse(((Button)Get(aux, "BUT_Low")).Enabled);
                Call(aux, "BUT_Low_Click", null, EventArgs.Empty); Assert.AreEqual(0, link.Sent.Count);
                ConfirmAllChecks(); Call(aux, "UpdateProjectAvailability"); Assert.IsTrue(((Button)Get(aux, "BUT_High")).Enabled);
                Answer(false, () => Call(aux, "BUT_High_Click", null, EventArgs.Empty)); Assert.AreEqual(0, link.Sent.Count);
            }
        });
        [DataTestMethod]
        [DataRow(41, 0)] [DataRow(153, 0)] [DataRow(154, 0)] [DataRow(81, 2)]
        public void AuxDisarmAlwaysConfirmsAndCancelSendsNothing(int option, int position) => OnUi(() =>
        {
            using (var link = new Link()) using (var aux = PrepareAux(option))
            {
                var handler = position == 0 ? "BUT_Low_Click" : "BUT_High_Click";
                Answer(false, () => Call(aux, handler, null, EventArgs.Empty)); Assert.AreEqual(0, link.Sent.Count);
            }
        });
        [DataTestMethod]
        [DataRow(153, 2)] [DataRow(154, 2)] [DataRow(165, 2)] [DataRow(81, 2)] [DataRow(41, 0)]
        public void AuxConfirmedActionSendsOriginalOptionAndPosition(int option, int position) => OnUi(() =>
        {
            using (var link = new Link()) using (var aux = PrepareAux(option))
            {
                ConfirmAllChecks();
                Answer(true, () => Call(aux, position == 0 ? "BUT_Low_Click" : "BUT_High_Click", null, EventArgs.Empty));
                Pump(() => !(bool)Get(aux, "commandPending"));
                var command = link.Sent.Single(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG).ToStructure<MAVLink.mavlink_command_long_t>();
                Assert.AreEqual((ushort)MAVLink.MAV_CMD.DO_AUX_FUNCTION, command.command);
                Assert.AreEqual((float)option, command.param1); Assert.AreEqual((float)position, command.param2);
                Assert.IsFalse(link.Port.giveComport);
            }
        });
        [DataTestMethod]
        [DataRow(154, 1)] [DataRow(165, 0)] [DataRow(165, 1)] [DataRow(81, 0)] [DataRow(16, 2)]
        public void AuxNonArmingPositionsRemainAvailableWithoutChecks(int option, int position) => OnUi(() =>
        {
            using (var link = new Link()) using (var aux = PrepareAux(option))
            {
                var field = position == 0 ? "BUT_Low" : position == 1 ? "but_mid" : "BUT_High";
                Assert.IsTrue(((Button)Get(aux, field)).Enabled);
                Call(aux, position == 0 ? "BUT_Low_Click" : position == 1 ? "But_mid_Click" : "BUT_High_Click", null, EventArgs.Empty);
                Pump(() => !(bool)Get(aux, "commandPending"));
                var command = link.Sent.Single(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG).ToStructure<MAVLink.mavlink_command_long_t>();
                Assert.AreEqual((float)option, command.param1); Assert.AreEqual((float)position, command.param2);
            }
        });
        [DataTestMethod]
        [DataRow(153, 2)] [DataRow(81, 2)]
        public void AuxConfirmationCannotTransferToAnotherConnection(int option, int position) => OnUi(() =>
        {
            using (var link = new Link()) using (var aux = PrepareAux(option)) using (var replacement = new CommsInjection())
            {
                ConfirmAllChecks(); int packets = 0; replacement.WriteCallback += (sender, bytes) => Interlocked.Increment(ref packets);
                Answer(true, () => Call(aux, "BUT_High_Click", null, EventArgs.Empty), () => link.Port.BaseStream = replacement);
                Assert.AreEqual(0, packets); Assert.AreEqual(0, link.Sent.Count);
            }
        });        [TestMethod]
        public void AuxPendingAckDoesNotRetryArmingOnReplacementStream() => OnUi(() =>
        {
            using (var link = new Link()) using (var aux = PrepareAux(153)) using (var replacement = new CommsInjection())
            {
                ConfirmAllChecks(); link.Acknowledge = false;
                Answer(true, () => Call(aux, "BUT_High_Click", null, EventArgs.Empty));
                Pump(() => link.Sent.Any(x => x.msgid == (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_LONG));
                int packets = 0; replacement.WriteCallback += (sender, bytes) => Interlocked.Increment(ref packets);
                link.Port.BaseStream = replacement;
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                Pump(() => !(bool)Get(aux, "commandPending") && elapsed.ElapsedMilliseconds > 2300);
                Assert.AreEqual(0, packets); Assert.IsFalse(link.Port.giveComport);
            }
        });    }
}
