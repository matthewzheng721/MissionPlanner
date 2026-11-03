using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    // 控制台的双语分组标题只负责排版，不参与任何飞行状态或命令处理。
    public sealed class ProjectControlHeading : Control
    {
        private static readonly Font CaptionFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
        public string Caption { get; set; }
        public ProjectControlHeading(string title, string caption)
        {
            Text = title; Caption = caption; DoubleBuffered = true; Height = 26;
            Dock = DockStyle.Fill; Margin = Padding.Empty;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var primary = TextRenderer.MeasureText(Text, ProjectWorkspaceStyle.SectionFont, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, Text, ProjectWorkspaceStyle.SectionFont, new Point(4,4), ProjectWorkspaceStyle.Text, TextFormatFlags.NoPadding);
            var left = primary.Width + 12;
            if (Width > left + 12)
                TextRenderer.DrawText(e.Graphics, Caption, CaptionFont, new Rectangle(left,6,Width-left-4,Height-6), ProjectWorkspaceStyle.Muted,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
