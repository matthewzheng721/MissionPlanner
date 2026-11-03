using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    public sealed class ProjectEcuMonitorPanel : UserControl
    {
        private readonly Label readings = new Label { Name = "ProjectEcuReadings", Dock = DockStyle.Top, Height = 210, Padding = new Padding(12) };
        private readonly RichTextBox messages = new RichTextBox { Name = "ProjectEcuMessages", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical };
        private readonly Timer timer = new Timer { Interval = 500 };
        private readonly Label[] values = new Label[5];
        private static readonly Font ValueFont = new Font("Microsoft YaHei UI", 20, FontStyle.Bold);
        private readonly Label source = new Label { Dock = DockStyle.Top, Height = 48, Padding = new Padding(4), AutoEllipsis = true };
        private readonly Label fault = new Label { Dock = DockStyle.Top, Height = 50, Padding = new Padding(8), Text = "ECU 报故格式尚未确认；请核对下方原始文字。" };
        public ProjectEcuMonitorPanel()
        {
            Padding = new Padding(16);
            var metrics = new TableLayoutPanel { Dock = DockStyle.Top, Height = 228, ColumnCount = 2, RowCount = 3 };
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (var i = 0; i < 3; i++) metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
            var titles = new[] { "油门量 · EFI 节气门位置", "转速 · EFI", "最高缸温 · 四缸", "润滑油压", "ECU 电压" };
            for (var i = 0; i < titles.Length; i++)
            {
                var cell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4), Margin = new Padding(2) };
                values[i] = new Label { Text = "—", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                cell.Controls.Add(values[i]); cell.Controls.Add(ProjectWorkspaceStyle.Heading(titles[i], 25)); metrics.Controls.Add(cell, i % 2, i / 2);
            }
            messages.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(messages); Controls.Add(ProjectWorkspaceStyle.Heading("ECU 原始回报", 30)); Controls.Add(fault); Controls.Add(metrics); Controls.Add(source);
            ProjectWorkspaceStyle.Apply(this);
            timer.Tick += (sender, args) => RefreshValues(); timer.Start(); RefreshValues();
        }
        public void RefreshValues()
        {
            if (IsDisposed) return;
            var port = MainV2.comPort;
            if (port == null || !port.BaseStream.IsOpen || port.logreadmode)
            {
                readings.Text = "未连接飞控。\r\n油门量：—\r\n转速：—\r\n最高缸温：—\r\n润滑油压：—\r\nECU 电压：—\r\nECU 报故：—";
                source.Text = "未连接飞控 · 等待 ECU 回报";
                foreach (var value in values) value.Text = "—";
                StyleValues();
                messages.Text = "等待 ECU 反馈；没有消息不等于无故障。"; return;
            }
            var snapshot = port.MAV.cs.messages.ToArray();
            var known = new ProjectEcuStatus();
            foreach (var entry in snapshot) known.Observe(entry.message, entry.time);
            string throttle = "—", rpm = "—", sampleTime = "尚未收到 EFI_STATUS";
            // 只读取最后报文，不从接收队列取走数据或增加遥测请求。
            var packet = port.MAV.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.EFI_STATUS);
            if (packet != null && packet.sysid == port.MAV.sysid && packet.compid == port.MAV.compid)
            {
                var efi = packet.ToStructure<MAVLink.mavlink_efi_status_t>();
                if (efi.ecu_index == 0)
                {
                    throttle = Format(efi.throttle_position, " %"); rpm = Format(efi.rpm, " rpm");
                    sampleTime = "EFI_STATUS 接收于 " + packet.rxtime.ToLocalTime().ToString("HH:mm:ss");
                }
            }
            var highest = known.Temperatures == null ? "—" : known.Temperatures.Max().ToString("0.#") + " ℃（" + known.TemperatureTime.ToString("HH:mm:ss") + "）";
            readings.Text = "油门量（EFI 节气门位置）：" + throttle + "\r\n转速（EFI）：" + rpm + "\r\n最高缸温（四缸）：" + highest +
                "\r\n润滑油压：—（映射未确认）\r\nECU 电压：—（映射未确认）\r\n" + known.ThrottleText +
                "\r\n" + sampleTime + "\r\nECU 报故：格式尚未确认，下方保留 ECU 原始文字。";
            values[0].Text = throttle; values[1].Text = rpm;
            values[2].Text = known.Temperatures == null ? "—" : known.Temperatures.Max().ToString("0.#") + " ℃";
            values[3].Text = values[4].Text = "—";
            source.Text = sampleTime + "\r\n" + known.ThrottleText;
            fault.Text = "油压、电压映射及报故格式待确认；没有消息不等于无故障。";
            StyleValues();
            var ecuMessages = snapshot.Where(entry => entry.message != null && entry.message.IndexOf("ECU", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            var text = ecuMessages.Length == 0 ? "未收到 ECU 原始文字；不代表无故障。" : string.Join("\r\n", ecuMessages.Skip(Math.Max(0, ecuMessages.Length - 40)).Select(entry => entry.time.ToString("HH:mm:ss") + "  " + entry.message));
            if (messages.Text != text) { messages.Text = text; messages.SelectionStart = messages.TextLength; messages.ScrollToCaret(); }
        }
        internal void StyleValues()
        {
            foreach (var value in values) { value.Font = ValueFont; value.ForeColor = ProjectWorkspaceStyle.Text; }
            messages.BackColor = ProjectWorkspaceStyle.Well; messages.ForeColor = ProjectWorkspaceStyle.Muted;
        }
        private static string Format(float value, string unit) => float.IsNaN(value) || float.IsInfinity(value) ? "—" : value.ToString("0.#") + unit;
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
