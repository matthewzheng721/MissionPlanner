using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    public sealed class ProjectSurfaceView : Control
    {
        private double[] motion;
        private int[] previewPwm;
        private ushort[] reportedPwm;
        public string Caption { get; set; } = "预期舵面方向";
        public ProjectSurfaceView() { DoubleBuffered = true; Height = 188; MinimumSize = new Size(520, 180); }
        public void Display(double[] expected, int[] preview, ushort[] reported) { motion = expected; previewPwm = preview; reportedPwm = reported; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(ProjectWorkspaceStyle.Well)) g.FillRectangle(background, ClientRectangle);
            using (var text = new SolidBrush(Color.FromArgb(232, 240, 243)))
            using (var muted = new SolidBrush(Color.FromArgb(172, 191, 202)))
            using (var line = new Pen(Color.FromArgb(99, 125, 140), 2))
            using (var accent = new Pen(ProjectWorkspaceStyle.Cyan, 5))
            {
                g.DrawString(Caption, Font, text, 12, 7);
                var mid = Math.Max(105, Math.Min(170, Height / 2f));
                var width = ClientSize.Width / 4f;
                for (var i = 0; i < 4; i++)
                {
                    var x = width * i + 12; var center = x + width / 2 - 12;
                    g.DrawString("S" + (i + 1) + (i == 0 || i == 3 ? " · 混控" : " · 俯仰"), Font, text, x, 33);
                    g.DrawString(i < 2 ? "反向安装 / PWM↑ 下偏" : "正向安装 / PWM↑ 上偏", Font, muted, x, 56);
                    g.DrawLine(line, center - 44, mid, center + 44, mid);
                    var known = motion != null && !double.IsNaN(motion[i]); var v = known ? motion[i] : 0;
                    // 单幅侧视符号：左端为铰链，后缘向上为上偏；仅相对幅度，不标真实角度。
                    var angle = ProjectSurfaceMotion.Clamp(v) * .70;
                    g.DrawLine(accent, center - 24, mid, center - 24 + (float)(58 * Math.Cos(angle)), mid - (float)(58 * Math.Sin(angle)));
                    g.DrawString(!known ? "等待数据" : Math.Abs(v) < .015 ? "中立" : v > 0 ? "↑ 上偏" : "↓ 下偏", Font, text, x, mid + 22);
                    var previewText = previewPwm == null ? "" : "预览 " + previewPwm[i] + " μs  ";
                    g.DrawString(previewText, Font, muted, x, mid + 44);
                    g.DrawString("回报 " + (reportedPwm == null || reportedPwm[i] == 0 ? "—" : reportedPwm[i] + " μs"), Font, muted, x, mid + 63);
                }
            }
        }
    }

    internal static class ProjectCheckStyles
    {
        public static void Apply(Control control) => ProjectWorkspaceStyle.Apply(control);
    }

    internal static class ProjectServoTelemetry
    {
        // 按报文自身的接收时间检查新鲜度；心跳更新不能掩盖姿态/输出停更。
        public static MAVLink.MAVLinkMessage Fresh(MAVLinkInterface port, MAVLink.MAVLINK_MSG_ID id)
        {
            if (port == null || !port.BaseStream.IsOpen || port.logreadmode) return null;
            var msg = port.MAV.getPacketLast((uint)id);
            if (msg == null || msg.sysid != port.MAV.sysid || msg.compid != port.MAV.compid || msg.rxtime == DateTime.MinValue) return null;
            var age = DateTime.Now - msg.rxtime.ToLocalTime();
            return age.TotalSeconds >= 0 && age.TotalSeconds <= 3 ? msg : null;
        }
        public static ushort[] Outputs(MAVLinkInterface port)
        {
            var packet = Fresh(port, MAVLink.MAVLINK_MSG_ID.SERVO_OUTPUT_RAW); if (packet == null) return null;
            var v = packet.ToStructure<MAVLink.mavlink_servo_output_raw_t>();
            return v.port == 0 ? new[] { v.servo1_raw, v.servo2_raw, v.servo3_raw, v.servo4_raw } : null;
        }
    }
}
