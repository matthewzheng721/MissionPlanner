using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    // 原风向控件继续持有绑定；此视图只读取其现值，生命周期内不建立遥测缓存或计时器。
    public sealed class ProjectWindView : Control
    {
        private readonly WindDir source;
        private readonly Func<bool> live;
        private readonly Func<string> unit;
        public ProjectWindView(WindDir source, Func<bool> live, Func<string> unit)
        {
            this.source = source; this.live = live; this.unit = unit;
            DoubleBuffered = true; source.Invalidated += SourceInvalidated;
        }
        private void SourceInvalidated(object sender, InvalidateEventArgs args) { Invalidate(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) source.Invalidated -= SourceInvalidated;
            base.Dispose(disposing);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(ProjectWorkspaceStyle.Panel);
            TextRenderer.DrawText(e.Graphics, "估算风", Font, new Point(6,4), ProjectWorkspaceStyle.Muted);
            var available = live() && !double.IsNaN(source.Speed) && !double.IsInfinity(source.Speed);
            var speed = available ? source.Speed.ToString("0.0") + " " + unit() : "—";
            TextRenderer.DrawText(e.Graphics, speed, Font, new Rectangle(4,Height-25,Width-8,23), ProjectWorkspaceStyle.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var center = new PointF(Width/2f, Height/2f);
            var radius = Math.Max(12,Math.Min(Width,Height)/2f - 30);
            using(var edge = new Pen(ProjectWorkspaceStyle.Edge)) e.Graphics.DrawEllipse(edge,center.X-radius,center.Y-radius,radius*2,radius*2);
            if (!available) return;
            // Direction 已由原控件执行 +180°，此处直接沿用原箭头的方向与风速比例。
            var angle = (source.Direction - 90) * Math.PI / 180;
            var length = radius * source.Speed / Math.Max(10,source.Speed);
            using(var arrow = new Pen(ProjectWorkspaceStyle.Cyan,2) { EndCap = LineCap.ArrowAnchor })
                e.Graphics.DrawLine(arrow,center,new PointF(center.X+(float)(length*Math.Cos(angle)),center.Y+(float)(length*Math.Sin(angle))));
        }
    }
}
