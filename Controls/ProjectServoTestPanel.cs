using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    public sealed class ProjectServoTestPanel : UserControl
    {
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, MinimumSize = new Size(540, 0) };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(810, 0), Text = "先读取飞控 SDT v3 参数；不使用示例值。" };
        private readonly Label previewStatus = new Label { AutoSize = true, MaximumSize = new Size(810, 0) };
        private readonly ProjectSurfaceView surfaces = new ProjectSurfaceView();
        private readonly Timer timer = new Timer { Interval = 50 };
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Button play = new Button { Text = "播放预览", AutoSize = true };
        private bool busy, loading, written;
        private MAVLinkInterface observedPort;
        private object observedStream;
        private byte observedSystem, observedComponent;
        private ProjectServoPreview preview;
        private Task commandQueue = Task.CompletedTask;
        private readonly Dictionary<string, Control> editors = new Dictionary<string, Control>();
        private bool syncingEditors;

        public ProjectServoTestPanel()
        {
            AutoScroll = true;
            var layout = new TableLayoutPanel { Name = "ProjectServoLayout", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(4) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            surfaces.Dock = DockStyle.Fill;
            var animation = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            animation.Controls.Add(play); play.Click += (s, e) => TogglePreview();
            Add(animation, "复位预览", () => { clock.Reset(); play.Text = "播放预览"; UpdatePreview(); });
            animation.Controls.Add(new Label { Text = "仅演示；不发送飞控命令", AutoSize = true, Margin = new Padding(12, 8, 0, 0) });
            grid.Columns.Add("parameter", "参数"); grid.Columns.Add("value", "值"); grid.Columns.Add("meaning", "含义");
            grid.Columns[0].ReadOnly = grid.Columns[2].ReadOnly = true; grid.Columns[0].FillWeight = 35; grid.Columns[1].FillWeight = 20; grid.Columns[2].FillWeight = 65;
            grid.CellValueChanged += (s, e) => { if (loading || e.ColumnIndex != 1) return; written = false; clock.Reset(); play.Text = "播放预览"; SyncEditors(); RebuildPreview(); };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            Add(buttons, "读取参数", Read); Add(buttons, "写入参数", async () => await Apply());
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            commands.Controls.Add(new Label { Text = "飞控脚本", AutoSize = true, Margin = new Padding(3, 8, 8, 0) });
            Add(commands, "定位起点", async () => await Command(3)); Add(commands, "运行测试", async () => await Command(1)); Add(commands, "回中并释放", async () => await Command(2)); Add(commands, "立即释放", async () => await Command(0));
            var primary = new TableLayoutPanel { Name = "ProjectServoPrimary", Dock = DockStyle.Top, Height = 132, ColumnCount = 3, RowCount = 2 };
            for (var i = 0; i < 3; i++) primary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            primary.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); primary.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var names = new[] { "MODE", "MIN", "MAX", "PERIOD", "DURATION", "FRAC" };
            var captions = new[] { "测试模式", "MIN / μs", "MAX / μs", "周期 / 每级时间 · s", "运行时长 / s", "行程比例 FRAC" };
            for (var i = 0; i < names.Length; i++)
            {
                var name = "SDT_" + names[i]; Control input;
                if (i == 0)
                {
                    var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }; mode.Items.AddRange(new object[] { "0 · 往返", "1 · 阶跃", "2 · 正弦" });
                    mode.SelectedIndexChanged += (s, e) => WriteEditor(name, mode.SelectedIndex.ToString(CultureInfo.InvariantCulture)); input = mode;
                }
                else { var value = new TextBox { AutoSize = false, Height = 30 }; value.TextChanged += (s, e) => WriteEditor(name, value.Text); input = value; }
                editors.Add(name, input); input.Dock = DockStyle.Top;
                var cell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5), Margin = Padding.Empty }; cell.Controls.Add(input); cell.Controls.Add(ProjectWorkspaceStyle.Heading(captions[i], 25));
                primary.Controls.Add(cell, i % 3, i / 3);
            }
            var limits = new Panel { Name = "ProjectServoLimits", Dock = DockStyle.Top, Height = 220, Visible = false };
            grid.MinimumSize = new Size(0, 0); grid.Dock = DockStyle.Fill; limits.Controls.Add(grid);
            var limitsButton = new Button { Name = "ProjectServoLimitsToggle", Text = "各路限幅与阶跃步长  ▸", Dock = DockStyle.Top, Height = 32, TextAlign = ContentAlignment.MiddleLeft };
            limitsButton.Click += (s, e) => { limits.Visible = !limits.Visible; limitsButton.Text = limits.Visible ? "各路限幅与阶跃步长  ▾" : "各路限幅与阶跃步长  ▸"; };
            layout.Controls.Add(buttons); layout.Controls.Add(previewStatus); layout.Controls.Add(primary); layout.Controls.Add(surfaces); layout.Controls.Add(animation); layout.Controls.Add(limitsButton); layout.Controls.Add(limits); layout.Controls.Add(commands); layout.Controls.Add(status);
            Controls.Add(layout); Read(); timer.Tick += (s, e) => TickPreview(); timer.Start();
        }
        private void WriteEditor(string name, string value)
        {
            if (busy || loading || syncingEditors) return;
            var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (string)r.Cells[0].Value == name);
            if (row != null) row.Cells[1].Value = value;
        }
        private void SyncEditors()
        {
            syncingEditors = true;
            try
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    Control input;
                    if (!editors.TryGetValue((string)row.Cells[0].Value, out input)) continue;
                    var value = Convert.ToString(row.Cells[1].Value);
                    if (input is ComboBox combo) { int mode; combo.SelectedIndex = int.TryParse(value, out mode) && mode >= 0 && mode <= 2 ? mode : -1; }
                    else if (input.Text != value) input.Text = value;
                    row.Visible = false;
                }
            }
            finally { syncingEditors = false; }
        }
        private static void Add(Control parent, string text, Action action) { var b = new Button { Text = text, AutoSize = true }; b.Click += (s, e) => action(); parent.Controls.Add(b); }
        private bool SameSource() => ReferenceEquals(MainV2.comPort, observedPort) && observedPort != null && ReferenceEquals(observedPort.BaseStream, observedStream) && observedPort.BaseStream.IsOpen && !observedPort.logreadmode && observedPort.MAV.sysid == observedSystem && observedPort.MAV.compid == observedComponent;
        private void TickPreview()
        {
            if (!SameSource() && preview != null) { written = false; preview = null; clock.Reset(); play.Text = "播放预览"; previewStatus.Text = "连接或目标已变化，请重新读取本机参数。"; }
            UpdatePreview();
        }
        private async void Read()
        {
            if (busy) return;
            var p = MainV2.comPort;
            if (observedPort != p) { if (observedPort != null) observedPort.OnPacketReceived -= StateReceived; observedPort = p; observedPort.OnPacketReceived += StateReceived; }
            observedStream = p.BaseStream; observedSystem = p.MAV.sysid; observedComponent = p.MAV.compid;
            loading = true; written = false; clock.Reset(); grid.Rows.Clear();
            foreach (var n in ProjectServoPreview.Names)
            {
                var key = "SDT_" + n;
                var description = n == "MODE" ? "0 往返 / 1 阶跃 / 2 正弦" : n == "PERIOD" ? "秒；阶跃为每级时间，其余为完整周期" : n == "DURATION" ? "秒；到时 TRIM 保持 1 秒后释放" : n == "FRAC" ? "校准行程比例 0.10～1.00" : n == "STEP" ? "阶跃增量 μs" : n.StartsWith("S") ? "该路独立限幅；0 沿用 SERVO 范围" : "全局请求 PWM（μs）";
                grid.Rows.Add(key, p.MAV.param.ContainsKey(key) ? p.MAV.param[key].Value.ToString(CultureInfo.InvariantCulture) : "—", description);
            }
            loading = false; SyncEditors(); RebuildPreview(); status.Text = "已读取当前参数缓存；请求脚本状态回报。";
            if (!SameSource()) { status.Text = "未连接飞控；参数不可确认。"; return; }
            var stream = observedStream; var sys = observedSystem; var comp = observedComponent;
            try { await Task.Run(() => { if (ReferenceEquals(p.BaseStream, stream) && p.BaseStream.IsOpen && p.MAV.sysid == sys && p.MAV.compid == comp) p.sendPacket(new MAVLink.mavlink_param_request_read_t { target_system = sys, target_component = comp, param_index = -1, param_id = System.Text.Encoding.ASCII.GetBytes("SDT_STATE".PadRight(16, '\0')) }, sys, comp); }); }
            catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
        }
        private Dictionary<string, double> Values()
        {
            var values = new Dictionary<string, double>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                double v; var name = (string)row.Cells[0].Value;
                if (!double.TryParse(Convert.ToString(row.Cells[1].Value), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v)) throw new InvalidOperationException("请读取并补全 " + name + "。");
                values.Add(name, v);
            }
            for (var i = 1; i <= 4; i++) foreach (var suffix in new[] { "MIN", "TRIM", "MAX" })
            {
                var name = "SERVO" + i + "_" + suffix;
                if (!MainV2.comPort.MAV.param.ContainsKey(name)) throw new InvalidOperationException("缺少 " + name + "，不能推算逐路限幅。");
                values.Add(name, MainV2.comPort.MAV.param[name].Value);
            }
            return values;
        }
        private void RebuildPreview()
        {
            try { if (!SameSource()) throw new InvalidOperationException("等待当前飞控参数。"); preview = ProjectServoPreview.Create(Values()); UpdatePreview(); }
            catch (Exception ex) { preview = null; clock.Reset(); previewStatus.Text = ex.Message; surfaces.Display(null, null, ProjectServoTelemetry.Outputs(MainV2.comPort)); }
        }
        private void TogglePreview()
        {
            grid.EndEdit();
            if (clock.IsRunning) { clock.Stop(); play.Text = "继续预览"; return; }
            RebuildPreview(); if (preview == null) return;
            if (clock.Elapsed.TotalSeconds >= preview.Duration + 1) clock.Reset();
            clock.Start(); play.Text = "暂停预览";
        }
        private void UpdatePreview()
        {
            surfaces.Caption = written ? "已写入并回报的参数 · 预期动作" : "待写入参数预览 · 不代表飞控正在执行";
            if (preview == null) { surfaces.Display(null, null, ProjectServoTelemetry.Outputs(MainV2.comPort)); return; }
            var elapsed = clock.Elapsed.TotalSeconds; var pwm = preview.At(elapsed);
            var motion = pwm == null ? null : Enumerable.Range(0, 4).Select(i => ProjectSurfaceMotion.FromPwm(i, pwm[i])).ToArray();
            surfaces.Display(motion, pwm, ProjectServoTelemetry.Outputs(MainV2.comPort));
            previewStatus.Text = $"预览 {Math.Min(elapsed, preview.Duration + 1):F1} s / {preview.Duration:F2} s  · " + (pwm == null ? "已释放；之后由飞控原功能接管" : elapsed >= preview.Duration ? "各路 TRIM 保持 1 秒" : "理想波形，已应用逐路限幅；未标定实际舵角");
            if (pwm == null) { clock.Stop(); play.Text = "重新预览"; }
        }
        private void StateReceived(object sender, MAVLink.MAVLinkMessage message)
        {
            if (IsDisposed || !IsHandleCreated || !SameSource() || message.sysid != observedSystem || message.compid != observedComponent || message.msgid != (uint)MAVLink.MAVLINK_MSG_ID.PARAM_VALUE) return;
            var v = message.ToStructure<MAVLink.mavlink_param_value_t>();
            if (System.Text.Encoding.ASCII.GetString(v.param_id).TrimEnd('\0') != "SDT_STATE") return;
            try { BeginInvoke((Action)(() => { if (!IsDisposed && SameSource()) status.Text = "SDT_STATE 最新回读：" + v.param_value + "（0空闲 / 1定位 / 2运行 / 3回中 / 4异常）"; })); }
            catch (InvalidOperationException) { /* 关闭页面后不更新。 */ }
        }
        private async Task Apply()
        {
            if (busy || !SameSource()) { status.Text = "请先连接并读取当前目标的参数。"; return; }
            grid.EndEdit(); Dictionary<string, double> values;
            try { values = Values(); ProjectServoPreview.Create(values); } catch (Exception ex) { status.Text = ex.Message; return; }
            var p = observedPort; var sys = observedSystem; var comp = observedComponent; var stream = observedStream;
            busy = true; grid.Enabled = false; written = false;
            foreach (var editor in editors.Values) editor.Enabled = false;
            try
            {
                foreach (var pair in values.Where(v => v.Key.StartsWith("SDT_")))
                {
                    if (IsDisposed || !ReferenceEquals(MainV2.comPort, p) || !ReferenceEquals(p.BaseStream, stream) || !p.BaseStream.IsOpen || p.MAV.sysid != sys || p.MAV.compid != comp) throw new InvalidOperationException("连接或目标变化，已停止写入。");
                    await WriteConfirmed(p, sys, comp, stream, pair.Key, pair.Value);
                }
                if (!IsDisposed && SameSource()) { written = true; clock.Reset(); play.Text = "播放预览"; RebuildPreview(); status.Text = "全部参数写入已回报。动画仍是预览；运行测试须单独点击。"; }
            }
            catch (Exception ex) { if (!IsDisposed) { written = false; status.Text = "未完成整组写入；可能已有部分生效。" + ex.Message; } }
            finally { busy = false; if (!IsDisposed) { grid.Enabled = true; foreach (var editor in editors.Values) editor.Enabled = true; SyncEditors(); UpdatePreview(); } }
        }
        private async Task WriteConfirmed(MAVLinkInterface p, byte sys, byte comp, object stream, string name, double requested)
        {
            var response = new TaskCompletionSource<float>();
            EventHandler<MAVLink.MAVLinkMessage> received = (sender, message) =>
            {
                if (message.sysid != sys || message.compid != comp || message.msgid != (uint)MAVLink.MAVLINK_MSG_ID.PARAM_VALUE) return;
                var value = message.ToStructure<MAVLink.mavlink_param_value_t>();
                if (System.Text.Encoding.ASCII.GetString(value.param_id).TrimEnd('\0') == name) response.TrySetResult(value.param_value);
            };
            Func<bool> current = () => !IsDisposed && ReferenceEquals(MainV2.comPort, p) && ReferenceEquals(p.BaseStream, stream) && p.BaseStream.IsOpen && p.MAV.sysid == sys && p.MAV.compid == comp;
            p.OnPacketReceived += received;
            try
            {
                // 只订阅应答，不接管读取线程；每次重试前检查同一连接，避免旧写入跨到重连目标。
                for (var attempt = 0; attempt < 3 && !response.Task.IsCompleted; attempt++)
                {
                    if (!current()) throw new InvalidOperationException("连接或页面已变化。");
                    await Task.Run(() => { if (current()) p.sendPacket(new MAVLink.mavlink_param_set_t { target_system = sys, target_component = comp, param_id = System.Text.Encoding.ASCII.GetBytes(name.PadRight(16, '\0')), param_type = (byte)MAVLink.MAV_PARAM_TYPE.REAL32, param_value = (float)requested }, sys, comp); });
                    for (var wait = 0; wait < 30 && !response.Task.IsCompleted; wait++) { if (!current()) throw new InvalidOperationException("连接或页面已变化。"); await Task.Delay(50); }
                }
                if (!current() || !response.Task.IsCompleted || Math.Abs(response.Task.Result - requested) > Math.Max(.00001, Math.Abs(requested) * .000001)) throw new InvalidOperationException("参数未确认：" + name);
            }
            finally { p.OnPacketReceived -= received; }
        }
        private async Task Command(int command)
        {
            if (busy && command != 2 && command != 0) { status.Text = "参数操作进行中，请等待回报。"; return; }
            var p = MainV2.comPort;
            if (!SameSource() || !p.MAV.param.ContainsKey("SDT_RUN") || !p.MAV.param.ContainsKey("SDT_FRAC")) { status.Text = "连接或 SDT v3 参数缺失，未执行。"; return; }
            var sys = p.MAV.sysid; var comp = p.MAV.compid; var stream = p.BaseStream;
            try
            {
                var packet = new MAVLink.mavlink_param_set_t { target_system = sys, target_component = comp, param_id = System.Text.Encoding.ASCII.GetBytes("SDT_RUN".PadRight(16, '\0')), param_type = (byte)MAVLink.MAV_PARAM_TYPE.REAL32, param_value = command };
                commandQueue = commandQueue.ContinueWith(previous => { if (!ReferenceEquals(MainV2.comPort, p) || !ReferenceEquals(p.BaseStream, stream) || !stream.IsOpen || p.MAV.sysid != sys || p.MAV.compid != comp || (IsDisposed && command != 2 && command != 0)) throw new InvalidOperationException("连接或页面已变化，已取消旧命令。"); p.sendPacket(packet, sys, comp); }, TaskScheduler.Default);
                await commandQueue;
                if (!IsDisposed) status.Text = "SDT_RUN 已发送，尚未确认；以状态回报与现场舵面为准。";
            }
            catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); clock.Stop(); if (observedPort != null) observedPort.OnPacketReceived -= StateReceived; } base.Dispose(disposing); }
    }
}
