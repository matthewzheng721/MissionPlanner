using System;
using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    // 只承载已有数值标签，不缓存或计算第二套遥测状态。
    public sealed class ProjectTelemetryCell : Panel
    {
        private readonly string caption;
        private readonly Label value;
        public ProjectTelemetryCell(string caption, Label value)
        {
            this.caption = caption;
            this.value = value;
            Dock = DockStyle.Fill;
            Margin = new Padding(0, 2, 0, 2);
            Padding = new Padding(caption == "数传" ? 36 : caption == "GPS" ? 40 : 58, 0, 6, 0);
            BackColor = ProjectWorkspaceStyle.Well;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            value.Dock = DockStyle.Fill;
            value.TextAlign = ContentAlignment.MiddleRight;
            value.AutoEllipsis = true;
            value.BackColor = BackColor;
            Controls.Add(value);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextRenderer.DrawText(e.Graphics, caption, Font, new Rectangle(8, 0, Padding.Left - 8, Height),
                ProjectWorkspaceStyle.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            using(var line = new Pen(ProjectWorkspaceStyle.Edge))
                e.Graphics.DrawLine(line, 8, Height - 1, Width - 8, Height - 1);
        }
    }
}
