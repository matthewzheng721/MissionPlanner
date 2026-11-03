using MissionPlanner.Controls;
using MissionPlanner.Utilities;
using System;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    // 专用参数操作；显示飞控回读值，不推断电机实际运行状态。
    public class ConfigProjectMotor : MyUserControl, IActivate, IDeactivate
    {
        private const string ParameterName = "SERVO5_TRIM";
        private readonly MyButton startButton = new MyButton { Name = "startButton", Text = "一键开机", Location = new Point(20, 20), Size = new Size(110, 30) };
        private readonly MyButton restoreButton = new MyButton { Name = "restoreButton", Text = "恢复", Location = new Point(145, 20), Size = new Size(110, 30) };
        private readonly MyButton stopButton = new MyButton { Text = "一键停车", Location = new Point(270, 20), Size = new Size(110, 30) };
        private readonly Label currentValue = new Label { Name = "currentValue", AutoSize = true, Location = new Point(20, 65), Text = "SERVO5_TRIM：尚未回读" };
        private readonly Label status = new Label { Name = "status", AutoSize = true, Location = new Point(20, 95) };
        private readonly Timer readTimer = new Timer { Interval = 500 };
        private MAVLinkInterface port;
        private byte sysid;
        private byte compid;
        private bool active;
        private bool awaitingReadback;
        private int? requestedValue;
        private DateTime readDeadline;
        private static readonly object writeOrder = new object();
        private static Task queuedWrite = Task.CompletedTask;
        private static int engineVersion;
        private bool readInProgress;
        private int activation;
        private int writeVersion;

        public ConfigProjectMotor()
        {
            Name = "ConfigProjectMotor";
            Controls.AddRange(new Control[] { startButton, restoreButton, stopButton, currentValue, status,
                new Label { AutoSize = true, Location = new Point(20, 130), Text = "写入和回读仅表示参数状态；不表示电机已启动或已停止。" } });
            startButton.Click += (sender, args) => StartEngine();
            stopButton.Click += (sender, args) => StopEngine();
            restoreButton.Click += (sender, args) => Restore();
            readTimer.Tick += (sender, args) => RefreshPendingRead();
        }

        public void Activate()
        {
            Deactivate();
            port = MainV2.comPort;
            sysid = port.MAV.sysid;
            compid = port.MAV.compid;
            active = true;
            port.OnPacketReceived += ReceiveParameter;
            requestedValue = null;
            awaitingReadback = true;
            readDeadline = DateTime.UtcNow.AddSeconds(5);
            status.Text = "正在读取当前参数…";
            readTimer.Start();
            RefreshPendingRead();
        }

        public void Deactivate()
        {
            active = false;
            activation++;
            System.Threading.Interlocked.Increment(ref engineVersion);
            readTimer.Stop();
            if (port != null) port.OnPacketReceived -= ReceiveParameter;
        }

        private bool CheckConnection(bool requireTrim = true)
        {
            if (port == null || !port.BaseStream.IsOpen || port != MainV2.comPort || port.MAV.sysid != sysid || port.MAV.compid != compid)
            {
                status.Text = "连接已断开，参数值未确认。";
                awaitingReadback = false;
                return false;
            }
            if (requireTrim && !port.MAVlist[sysid, compid].param.ContainsKey(ParameterName))
            {
                status.Text = "当前飞控没有 SERVO5_TRIM 参数。";
                awaitingReadback = false;
                return false;
            }
            return true;
        }

        public void StartEngine()
        {
            if (!active || !CheckConnection()) return;
            var target = port; var stream = target.BaseStream; var lifetime = activation;
            var system = sysid; var component = compid;
            if (target.MAVlist[system, component].param[ParameterName].Value != 2000) { status.Text = "SERVO5_TRIM 当前不是 2000，请先核对或恢复。"; return; }
            if (!ProjectPreflight.ConfirmAction(this, "一键开机", "执行 Engine Start 后写入 SERVO5_TRIM=1300；3 秒恢复由飞控负责。")) return;
            if (!SameOperation(target, stream, lifetime, system, component)) { status.Text = "确认期间连接或页面已变化，未执行。"; return; }
            WriteTrim(1300, true);
        }
        public void Restore() { WriteTrim(2000); }
        private bool SameOperation(MAVLinkInterface target, object stream, int lifetime, byte system, byte component)
        {
            return active && activation == lifetime && ReferenceEquals(port, target) && ReferenceEquals(MainV2.comPort, target) &&
                ReferenceEquals(target.BaseStream, stream) && target.BaseStream.IsOpen && target.MAV.sysid == system && target.MAV.compid == component;
        }
        public async void StopEngine()
        {
            if (!active || !CheckConnection(false)) return;
            var target = port; var system = sysid; var component = compid; var stream = target.BaseStream; var lifetime = activation;
            if (!ProjectPreflight.ConfirmAction(this, "一键停车", "发送 Engine Stop；以飞控反馈和实际发动机状态为准。")) return;
            if (!SameOperation(target, stream, lifetime, system, component)) { status.Text = "确认期间连接或页面已变化，未执行。"; return; }
            var engineGeneration = System.Threading.Interlocked.Increment(ref engineVersion);
            status.Text = "正在请求 Engine Stop，等待飞控应答…";
            try
            {
                Task send;
                lock (writeOrder)
                {
                    send = queuedWrite = queuedWrite.ContinueWith(async t =>
                    {
                        if (!SameOperation(target, stream, lifetime, system, component)) throw new InvalidOperationException("连接或页面已变化，已取消旧操作。");
                        if (!await SendEngineCommand(target, system, component, stream, false, engineGeneration)) throw new InvalidOperationException("Engine Stop 未确认。");
                    }, TaskScheduler.Default).Unwrap();
                }
                await send;
                if (!IsDisposed && engineGeneration == System.Threading.Volatile.Read(ref engineVersion) && SameOperation(target, stream, lifetime, system, component)) status.Text = "Engine Stop 命令已确认；请观察发动机。";
            }
            catch (Exception ex) { if (!IsDisposed && activation == lifetime && engineGeneration == System.Threading.Volatile.Read(ref engineVersion)) status.Text = ex.Message; }
        }
        private static async Task<bool> SendEngineCommand(MAVLinkInterface target, byte system, byte component, object stream, bool start, int generation)
        {
            var acknowledgement = new TaskCompletionSource<bool>();
            EventHandler<MAVLink.MAVLinkMessage> received = (sender, message) =>
            {
                if (!ReferenceEquals(target.BaseStream, stream) || message.sysid != system || message.compid != component || message.msgid != (uint)MAVLink.MAVLINK_MSG_ID.COMMAND_ACK) return;
                if (generation != System.Threading.Volatile.Read(ref engineVersion)) return;
                var ack = message.ToStructure<MAVLink.mavlink_command_ack_t>();
                if (ack.command == (ushort)MAVLink.MAV_CMD.DO_ENGINE_CONTROL)
                    acknowledgement.TrySetResult(ack.result == (byte)MAVLink.MAV_RESULT.ACCEPTED);
            };
            target.OnPacketReceived += received;
            try
            {
                var packet = new MAVLink.mavlink_command_long_t { target_system = system, target_component = component,
                    command = (ushort)MAVLink.MAV_CMD.DO_ENGINE_CONTROL, param1 = start ? 1 : 0 };
                // 保留原 2 秒间隔和 3 次重试；只重试同一连接上的当前操作。
                for (byte attempt = 0; attempt < 4; attempt++)
                {
                    if (!ReferenceEquals(target.BaseStream, stream) || !target.BaseStream.IsOpen || target.MAV.sysid != system || target.MAV.compid != component || generation != System.Threading.Volatile.Read(ref engineVersion)) return false;
                    packet.confirmation = attempt;
                    target.sendPacket(packet, system, component);
                    var deadline = DateTime.UtcNow.AddSeconds(2);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (!ReferenceEquals(target.BaseStream, stream) || !target.BaseStream.IsOpen || target.MAV.sysid != system || target.MAV.compid != component || generation != System.Threading.Volatile.Read(ref engineVersion)) return false;
                        if (acknowledgement.Task.IsCompleted) return await acknowledgement.Task;
                        await Task.WhenAny(acknowledgement.Task, Task.Delay(50));
                    }
                }
                return false;
            }
            finally { target.OnPacketReceived -= received; }
        }
        private async void WriteTrim(int value, bool engineStart = false)
        {
            if (!active || !CheckConnection()) return;
            var version = ++writeVersion;
            var engineGeneration = System.Threading.Interlocked.Increment(ref engineVersion);
            var lifetime = activation;
            var target = port;
            var stream = target.BaseStream;
            var targetSystem = sysid;
            var targetComponent = compid;
            requestedValue = value;
            awaitingReadback = true;
            readDeadline = DateTime.UtcNow.AddSeconds(5);
            status.Text = "正在写入 " + value + "，等待飞控回读…";
            try
            {
                var mav = port.MAVlist[sysid, compid];
                var type = mav.param_types[ParameterName];
                // 与 MP 原参数写入采用相同编码和公共发送通道；每次点击仅写一次。
                // 不重发旧写入，避免快速点击恢复后，旧的 1300 重试覆盖 2000。
                var encodedType = (mav.cs.capabilities & (uint)MAVLink.MAV_PROTOCOL_CAPABILITY.PARAM_FLOAT) > 0 ||
                    mav.apname == MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA ? MAVLink.MAV_PARAM_TYPE.REAL32 : (MAVLink.MAV_PARAM_TYPE)type;
                var packet = new MAVLink.mavlink_param_set_t
                {
                    target_system = sysid, target_component = compid,
                    param_id = ParameterId(), param_type = (byte)type,
                    param_value = new MAVLink.MAVLinkParam(ParameterName, value, encodedType).float_value
                };
                Task send;
                // 仅排入用户点击的写入，跨页面保持顺序；等待公共写锁不会阻塞界面。
                lock (writeOrder)
                {
                    send = queuedWrite = queuedWrite.ContinueWith(async previous =>
                        {
                            if (!SameOperation(target, stream, lifetime, targetSystem, targetComponent)) throw new InvalidOperationException("连接或页面已变化，已取消旧操作。");
                            if (engineStart && !await SendEngineCommand(target, targetSystem, targetComponent, stream, true, engineGeneration)) throw new InvalidOperationException("Engine Start 未确认，未写入 1300。");
                            if (!SameOperation(target, stream, lifetime, targetSystem, targetComponent)) throw new InvalidOperationException("连接或页面已变化，已取消旧操作。");
                            if (engineStart && engineGeneration != System.Threading.Volatile.Read(ref engineVersion)) throw new InvalidOperationException("旧开机操作已取消，未写入 1300。");
                            target.sendPacket(packet, targetSystem, targetComponent);
                        }, TaskScheduler.Default).Unwrap();
                }
                await send;
                if (active && activation == lifetime && writeVersion == version) RequestRead();
            }
            catch (Exception ex)
            {
                if (active && activation == lifetime && writeVersion == version && (!engineStart || engineGeneration == System.Threading.Volatile.Read(ref engineVersion)))
                {
                    awaitingReadback = false;
                    status.Text = "写入未确认：" + ex.Message;
                }
            }
        }

        private static byte[] ParameterId()
        {
            var id = new byte[16];
            Encoding.ASCII.GetBytes(ParameterName).CopyTo(id, 0);
            return id;
        }

        private async void RequestRead()
        {
            if (readInProgress) return;
            readInProgress = true;
            var target = port;
            var stream = target.BaseStream;
            var targetSystem = sysid;
            var targetComponent = compid;
            var lifetime = activation;
            try
            {
                await Task.Run(() => target.sendPacket(new MAVLink.mavlink_param_request_read_t
                {
                    target_system = targetSystem, target_component = targetComponent,
                    param_id = ParameterId(), param_index = -1
                }, targetSystem, targetComponent));
            }
            catch (Exception ex)
            {
                if (active && activation == lifetime)
                {
                    awaitingReadback = false;
                    status.Text = "读取失败：" + ex.Message;
                }
            }
            finally { readInProgress = false; }
        }
        private void RefreshPendingRead()
        {
            if (!active || !CheckConnection() || !awaitingReadback) return;
            if (DateTime.UtcNow >= readDeadline)
            {
                awaitingReadback = false;
                status.Text = "回读超时，未确认" + (requestedValue.HasValue ? "写入 " + requestedValue.Value : "当前参数") + "。";
                return;
            }
            try { RequestRead(); }
            catch (Exception ex)
            {
                awaitingReadback = false;
                status.Text = "读取失败：" + ex.Message;
            }
        }

        private void ReceiveParameter(object sender, MAVLink.MAVLinkMessage message)
        {
            if (!active || message.sysid != sysid || message.compid != compid ||
                message.msgid != (uint)MAVLink.MAVLINK_MSG_ID.PARAM_VALUE) return;
            var param = message.ToStructure<MAVLink.mavlink_param_value_t>();
            if (Encoding.ASCII.GetString(param.param_id).TrimEnd('\0') != ParameterName) return;
            var mav = port.MAVlist[sysid, compid];
            var type = mav.apname == MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA ||
                (mav.cs.capabilities & (uint)MAVLink.MAV_PROTOCOL_CAPABILITY.PARAM_FLOAT) > 0
                ? MAVLink.MAV_PARAM_TYPE.REAL32 : (MAVLink.MAV_PARAM_TYPE)param.param_type;
            var value = new MAVLink.MAVLinkParam(ParameterName, BitConverter.GetBytes(param.param_value), type,
                (MAVLink.MAV_PARAM_TYPE)param.param_type).Value;
            var lifetime = activation;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!active || IsDisposed || activation != lifetime) return;
                    currentValue.Text = "SERVO5_TRIM 回读值：" + value;
                    // 收到较早的回包也只显示实际值，绝不把它称为最新操作成功。
                    if (!requestedValue.HasValue || value == requestedValue.Value)
                    {
                        awaitingReadback = false;
                        status.Text = "飞控参数已回读：" + value;
                    }
                    else if (awaitingReadback)
                        status.Text = "当前回读 " + value + "，等待确认 " + requestedValue.Value + "…";
                    else
                        status.Text = "回读值与最近请求 " + requestedValue.Value + " 不一致。";
                }));
            }
            catch (InvalidOperationException) { /* 切页释放期间不再更新已关闭控件。 */ }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Deactivate(); readTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}