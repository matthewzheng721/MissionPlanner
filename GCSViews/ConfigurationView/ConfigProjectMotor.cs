using MissionPlanner.Controls;
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
        private readonly MyButton startButton = new MyButton { Name = "startButton", Text = "启动电机", Location = new Point(20, 20), Size = new Size(110, 30) };
        private readonly MyButton restoreButton = new MyButton { Name = "restoreButton", Text = "恢复", Location = new Point(145, 20), Size = new Size(110, 30) };
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
        private bool readInProgress;
        private int activation;
        private int writeVersion;

        public ConfigProjectMotor()
        {
            Name = "ConfigProjectMotor";
            Controls.AddRange(new Control[] { startButton, restoreButton, currentValue, status,
                new Label { AutoSize = true, Location = new Point(20, 130), Text = "写入和回读仅表示参数状态；不表示电机已启动或已停止。" } });
            startButton.Click += (sender, args) => WriteTrim(1300);
            restoreButton.Click += (sender, args) => WriteTrim(2000);
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
            readTimer.Stop();
            if (port != null) port.OnPacketReceived -= ReceiveParameter;
        }

        private bool CheckConnection()
        {
            if (port == null || !port.BaseStream.IsOpen)
            {
                status.Text = "连接已断开，参数值未确认。";
                awaitingReadback = false;
                return false;
            }
            if (!port.MAVlist[sysid, compid].param.ContainsKey(ParameterName))
            {
                status.Text = "当前飞控没有 SERVO5_TRIM 参数。";
                awaitingReadback = false;
                return false;
            }
            return true;
        }

        private async void WriteTrim(int value)
        {
            if (!active || !CheckConnection()) return;
            var version = ++writeVersion;
            var lifetime = activation;
            var target = port;
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
                    send = queuedWrite = queuedWrite.ContinueWith(previous =>
                        target.sendPacket(packet, targetSystem, targetComponent), TaskScheduler.Default);
                }
                await send;
                if (active && activation == lifetime && writeVersion == version) RequestRead();
            }
            catch (Exception ex)
            {
                if (active && activation == lifetime && writeVersion == version)
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