using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    public sealed class ProjectPolarityPanel : UserControl
    {
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(800, 0) };
        private readonly Label readings = new Label { AutoSize = true, MaximumSize = new Size(800, 0) };
        private readonly Label explanation = new Label { AutoSize = true, MaximumSize = new Size(800, 0) };
        private readonly NumericUpDown fraction = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 0, Width = 64 };
        private readonly Timer timer = new Timer { Interval = 100 };
        private readonly ProjectSurfaceView surfaces = new ProjectSurfaceView { Dock = DockStyle.Fill };
        private readonly FlowLayoutPanel inputs = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        private readonly CheckBox fbwaChecked = new CheckBox { Text = "FBWA 纠偏方向已核对", AutoSize = true };
        private readonly CheckBox manualChecked = new CheckBox { Text = "MANUAL 操纵方向已核对", AutoSize = true };
        private readonly Label otherCheck = new Label { AutoSize = true, Margin = new Padding(12, 3, 0, 0) };
        private readonly Button fbwaTab = new Button { Text = "FBWA 稳定纠偏", AutoSize = true };
        private readonly Button manualTab = new Button { Text = "MANUAL 操纵方向", AutoSize = true };
        private MAVLinkInterface port;
        private byte sys, comp;
        private int rollChannel, pitchChannel, rollTrim, pitchTrim, target, inputVersion, modeVersion;
        private string heldAxis;
        private int heldDirection;
        private bool held, manual, updating;
        private object inputStream;
        private Form host;
        private Task pending = Task.CompletedTask;

        public ProjectPolarityPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(4), AutoScroll = true };
            for (var i = 0; i < 8; i++) layout.RowStyles.Add(new RowStyle(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
            var tabs = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            tabs.Controls.AddRange(new Control[] { fbwaTab, manualTab }); fbwaTab.Click += (s, e) => ChangeView(false); manualTab.Click += (s, e) => ChangeView(true);
            var modes = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            AddMode(modes, "FBWA"); AddMode(modes, "MANUAL"); AddMode(modes, "AUTO");
            inputs.Controls.Add(new Label { Text = "输入行程 %", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }); inputs.Controls.Add(fraction);
            AddHold(inputs, "按住抬头", "PITCH", 1); AddHold(inputs, "按住低头", "PITCH", -1); AddHold(inputs, "按住左滚", "ROLL", -1); AddHold(inputs, "按住右滚", "ROLL", 1);
            var checks = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            checks.Controls.AddRange(new Control[] { fbwaChecked, manualChecked, otherCheck });
            fbwaChecked.CheckedChanged += (s, e) => ConfirmPart(false, fbwaChecked.Checked);
            manualChecked.CheckedChanged += (s, e) => ConfirmPart(true, manualChecked.Checked);
            layout.Controls.Add(tabs, 0, 0); layout.Controls.Add(modes, 0, 1); layout.Controls.Add(readings, 0, 2); layout.Controls.Add(explanation, 0, 3);
            layout.Controls.Add(surfaces, 0, 4); layout.Controls.Add(inputs, 0, 5); layout.Controls.Add(checks, 0, 6); layout.Controls.Add(status, 0, 7);
            Controls.Add(layout); ChangeView(false);
            timer.Tick += (s, e) => RefreshState(); timer.Start();
        }
        private void ConfirmPart(bool isManual, bool value)
        {
            if (updating) return; ProjectPreflight.RefreshConnection();
            if (value && (isManual != manual || ProjectServoTelemetry.Fresh(MainV2.comPort, MAVLink.MAVLINK_MSG_ID.HEARTBEAT) == null || !string.Equals(MainV2.comPort.MAV.cs.mode, isManual ? "MANUAL" : "FBWA", StringComparison.OrdinalIgnoreCase))) { RefreshState(); return; }
            ProjectPreflight.Checklist.SetPolarity(isManual, value);
        }
        private void ChangeView(bool value)
        {
            Release(); modeVersion++; manual = value;
            inputs.Visible = manual;
            explanation.Text = manual ? "发出操纵指令：按住动作，松开回遥控中位；退出或切换检查释放覆盖。输入不会触碰油门。" : "保持遥控中立，移动机身看纠偏；图按回报目标计算，未回报时按水平示意。";
            status.Text = "动画表示预期方向及相对幅度；回报 PWM 不是舵面位置测量。";
            RefreshState();
        }
        private void RefreshState()
        {
            if (IsDisposed) return;
            ProjectPreflight.RefreshConnection(); var p = MainV2.comPort; var connected = ProjectPreflight.Checklist.Connected;
            var heartbeat = ProjectServoTelemetry.Fresh(p, MAVLink.MAVLINK_MSG_ID.HEARTBEAT);
            var mode = heartbeat == null ? "等待模式反馈" : p.MAV.cs.mode;
            if ((rollChannel != 0 || pitchChannel != 0) && (!ReferenceEquals(p, port) || !ReferenceEquals(p.BaseStream, inputStream) || p.MAV.sysid != sys || p.MAV.compid != comp || !string.Equals(mode, "MANUAL", StringComparison.OrdinalIgnoreCase))) Release();
            else if (held && pending.IsCompleted) Send(target, false);
            var attitude = ProjectServoTelemetry.Fresh(p, MAVLink.MAVLINK_MSG_ID.ATTITUDE);
            double pitch = 0, roll = 0;
            if (attitude != null) { var a = attitude.ToStructure<MAVLink.mavlink_attitude_t>(); pitch = a.pitch * 180 / Math.PI; roll = a.roll * 180 / Math.PI; }
            readings.Text = "实际模式：" + mode + "    " + (attitude == null ? "姿态：等待新数据" : $"俯仰 {pitch:F1}°    滚转 {roll:F1}°");
            double[] expected = null;
            if (manual)
            {
                inputs.Enabled = connected && string.Equals(mode, "MANUAL", StringComparison.OrdinalIgnoreCase);
                surfaces.Caption = "MANUAL 操纵指令 → 预期舵面方向";
                if (inputs.Enabled) expected = ProjectSurfaceMotion.ForDemand(held && heldAxis == "PITCH" ? heldDirection * (double)fraction.Value / 100 : 0, held && heldAxis == "ROLL" ? heldDirection * (double)fraction.Value / 100 : 0);
            }
            else
            {
                surfaces.Caption = "水平目标下的预期纠偏 · 遥控须保持中立";
                var nav = ProjectServoTelemetry.Fresh(p, MAVLink.MAVLINK_MSG_ID.NAV_CONTROLLER_OUTPUT);
                double targetPitch = 0, targetRoll = 0;
                if (nav != null)
                {
                    var n = nav.ToStructure<MAVLink.mavlink_nav_controller_output_t>();
                    readings.Text += $"    飞控目标：俯仰 {n.nav_pitch:F1}° / 滚转 {n.nav_roll:F1}°";
                    targetPitch = n.nav_pitch; targetRoll = n.nav_roll;
                    surfaces.Caption = "按飞控回报目标的预期纠偏";
                }
                else { readings.Text += "    飞控目标：等待回报"; surfaces.Caption = "目标未回报，以下按水平 0° 示意"; }
                if (attitude != null && string.Equals(mode, "FBWA", StringComparison.OrdinalIgnoreCase)) expected = ProjectSurfaceMotion.ForAttitude(targetPitch, targetRoll, pitch, roll);
            }
            surfaces.Display(expected, null, ProjectServoTelemetry.Outputs(p));
            updating = true;
            fbwaChecked.Checked = ProjectPreflight.Checklist.FbwaPolarityConfirmed; manualChecked.Checked = ProjectPreflight.Checklist.ManualPolarityConfirmed;
            fbwaChecked.Visible = !manual; manualChecked.Visible = manual;
            fbwaChecked.Enabled = connected && string.Equals(mode, "FBWA", StringComparison.OrdinalIgnoreCase);
            manualChecked.Enabled = connected && string.Equals(mode, "MANUAL", StringComparison.OrdinalIgnoreCase);
            otherCheck.Text = manual ? "FBWA：" + (fbwaChecked.Checked ? "已核对" : "未核对") : "MANUAL：" + (manualChecked.Checked ? "已核对" : "未核对");
            ProjectWorkspaceStyle.Accent(fbwaTab, !manual); ProjectWorkspaceStyle.Accent(manualTab, manual);
            updating = false;
        }
        private void AddMode(Control parent, string mode)
        {
            var button = new Button { Text = mode, AutoSize = true }; parent.Controls.Add(button);
            button.Click += async (s, e) =>
            {
                var p = MainV2.comPort; var stream = p.BaseStream; var system = p.MAV.sysid; var component = p.MAV.compid; Release(); var request = ++modeVersion; await pending;
                if (IsDisposed || request != modeVersion || !ReferenceEquals(MainV2.comPort, p) || !ReferenceEquals(p.BaseStream, stream) || !stream.IsOpen || p.MAV.sysid != system || p.MAV.compid != component) return;
                try { await Task.Run(() => { if (request == modeVersion && ReferenceEquals(p.BaseStream, stream) && stream.IsOpen && p.MAV.sysid == system && p.MAV.compid == component) p.setMode(mode); }); if (!IsDisposed) status.Text = "已请求 " + mode + "；以上方实际模式反馈为准。"; }
                catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
            };
        }
        private void AddHold(Control parent, string text, string axis, int direction)
        {
            var button = new Button { Text = text, AutoSize = true }; parent.Controls.Add(button);
            button.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) Begin(axis, direction); };
            button.MouseUp += (s, e) => Center(); button.MouseCaptureChanged += (s, e) => { if (!button.Capture) Center(); };
            button.KeyDown += (s, e) => { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (!held) Begin(axis, direction); } };
            button.KeyUp += (s, e) => Center(); button.LostFocus += (s, e) => Release();
        }
        private int Parameter(string name)
        {
            if (!port.MAV.param.ContainsKey(name)) throw new InvalidOperationException("缺少 " + name + "，不发送输入。");
            return (int)port.MAV.param[name].Value;
        }
        private void Begin(string axis, int direction)
        {
            Release(); port = MainV2.comPort; inputStream = port.BaseStream; sys = port.MAV.sysid; comp = port.MAV.compid;
            try
            {
                if (!manual || !port.BaseStream.IsOpen || port.logreadmode || ProjectServoTelemetry.Fresh(port, MAVLink.MAVLINK_MSG_ID.HEARTBEAT) == null || !string.Equals(port.MAV.cs.mode, "MANUAL", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请选择 MANUAL 操纵检查，显式切换模式并等待反馈。");
                rollChannel = Parameter("RCMAP_ROLL"); pitchChannel = Parameter("RCMAP_PITCH");
                if (rollChannel < 1 || rollChannel > 18 || pitchChannel < 1 || pitchChannel > 18 || rollChannel == pitchChannel) throw new InvalidOperationException("遥控通道映射无效。");
                rollTrim = Parameter("RC" + rollChannel + "_TRIM"); pitchTrim = Parameter("RC" + pitchChannel + "_TRIM");
                foreach (var c in new[] { rollChannel, pitchChannel }) ProjectSurfaceMotion.RcTarget(Parameter("RC" + c + "_MIN"), Parameter("RC" + c + "_TRIM"), Parameter("RC" + c + "_MAX"), false, 1, Math.Max(.001, (double)fraction.Value / 100));
                if (fraction.Value <= 0) throw new InvalidOperationException("请先设置本次输入行程比例。");
                var channel = axis == "PITCH" ? pitchChannel : rollChannel; var reversed = Parameter("RC" + channel + "_REVERSED");
                if (reversed != 0 && reversed != 1) throw new InvalidOperationException("RC 反向参数无效。");
                target = ProjectSurfaceMotion.RcTarget(Parameter("RC" + channel + "_MIN"), Parameter("RC" + channel + "_TRIM"), Parameter("RC" + channel + "_MAX"), reversed == 1, direction, (double)fraction.Value / 100);
                heldAxis = axis; heldDirection = direction; held = true; Send(target, false);
                status.Text = "RC" + channel + " 请求 " + target + " μs；松开回中，另一轴保持遥控中位。";
            }
            catch (Exception ex) { rollChannel = pitchChannel = 0; held = false; status.Text = ex.Message; }
            RefreshState();
        }
        private void Center() { if (!held) return; held = false; inputVersion++; Send(heldAxis == "PITCH" ? pitchTrim : rollTrim, false); status.Text = "已请求回遥控中位；退出释放覆盖。舵面仍受配平与混控影响。"; RefreshState(); }
        private void Release() { held = false; inputVersion++; if (port != null && (rollChannel != 0 || pitchChannel != 0)) Send(0, true); rollChannel = pitchChannel = 0; }
        private void Send(int value, bool release)
        {
            if (rollChannel == 0 || pitchChannel == 0) return;
            var p = port; var system = sys; var component = comp; var stream = inputStream; var version = inputVersion;
            object packet = new MAVLink.mavlink_rc_channels_override_t { target_system = system, target_component = component };
            for (var i = 1; i <= 18; i++)
            {
                var field = packet.GetType().GetField("chan" + i + "_raw"); if (field == null) continue;
                ushort raw = ushort.MaxValue;
                if (i == rollChannel || i == pitchChannel) raw = release ? (ushort)(i <= 8 ? 0 : 65534) : (ushort)(i == rollChannel ? (heldAxis == "ROLL" ? value : rollTrim) : (heldAxis == "PITCH" ? value : pitchTrim));
                field.SetValue(packet, raw);
            }
            // 操作串行，过时的操纵不补发；释放仍发给原目标，不触碰后来的连接。
            pending = pending.ContinueWith(t => { try { if (ReferenceEquals(p.BaseStream, stream) && p.BaseStream.IsOpen && (release || (version == inputVersion && ReferenceEquals(MainV2.comPort, p) && p.MAV.sysid == system && p.MAV.compid == component && string.Equals(p.MAV.cs.mode, "MANUAL", StringComparison.OrdinalIgnoreCase)))) p.sendPacket(packet, system, component); } catch { /* 断链后由飞控覆盖超时接管。 */ } }, TaskScheduler.Default);
        }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); host = FindForm(); if (host != null) host.Deactivate += HostDeactivated; }
        private void HostDeactivated(object sender, EventArgs e) { Release(); }
        protected override void OnLeave(EventArgs e) { Release(); base.OnLeave(e); }
        protected override void Dispose(bool disposing) { if (disposing) { modeVersion++; timer.Dispose(); if (host != null) host.Deactivate -= HostDeactivated; Release(); } base.Dispose(disposing); }
    }
}
