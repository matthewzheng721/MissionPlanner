using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL;

namespace MissionPlanner.Controls
{
    public partial class HUD
    {
        private bool projectInstrumentStyle;
        private Font projectInstrumentFont;
        private ToolTip projectInstrumentTip;
        private string projectInstrumentTipText;
        private readonly Dictionary<int, int> projectCharacterWidths = new Dictionary<int, int>();

        // 只由新工作台开启；数据属性和原控件事件保持原样。
        [Browsable(true), DefaultValue(false), Category("Appearance")]
        public bool ProjectInstrumentStyle
        {
            get { return projectInstrumentStyle; }
            set
            {
                if (projectInstrumentStyle == value) return;
                projectInstrumentStyle = value;
                if (!value && projectInstrumentTip != null) projectInstrumentTip.SetToolTip(this, null);
                Invalidate();
            }
        }

        private static readonly Color ProjectInk = Color.FromArgb(223, 234, 243);
        private static readonly Color ProjectMuted = Color.FromArgb(142, 174, 196);
        private static readonly Color ProjectGold = Color.FromArgb(234, 179, 77);
        private static readonly Color ProjectRed = Color.FromArgb(240, 106, 120);
        private static readonly Color ProjectGreen = Color.FromArgb(40, 217, 156);
        private static readonly Color ProjectDark = Color.FromArgb(6, 18, 29);

        private static bool ProjectFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static double ProjectHeading(double value) => (value % 360 + 360) % 360;

        private void DrawProjectInstrument()
        {
            if (Width < 100 || Height < 100) return;
            if (projectInstrumentFont == null) projectInstrumentFont = new Font("Microsoft YaHei UI", 10f);
            var compact = Width < 340 || Height < 260;
            var label = compact ? 10 : 12;
            var text = compact ? 11 : 13;
            var value = compact ? 16 : 20;
            var headingHeight = compact ? 29 : 38;
            var metricsHeight = compact ? 19 : 25;
            var stateHeight = compact ? 20 : 25;
            var attitudeValid = connected && ProjectFinite(_pitch) && ProjectFinite(_roll);
            var headingValid = connected && ProjectFinite(_heading);
            var actualPitch = attitudeValid ? _pitch : 0;
            var actualRoll = attitudeValid ? _roll : 0;
            var actualHeading = headingValid ? ProjectHeading(_heading) : 0;

            var warnings = new List<string>();
            var warningColor = ProjectInk;
            if (connected)
            {
                if (!attitudeValid || !headingValid) { warnings.Add("姿态 / 航向数据无效"); warningColor = ProjectRed; }
                if (failsafe) { warnings.Add(HUDT.FAILSAFE); warningColor = ProjectRed; }
                if (safetyactive) { warnings.Add(HUDT.SAFE); if (warningColor != ProjectRed) warningColor = ProjectGold; }
                if (!string.IsNullOrWhiteSpace(message))
                {
                    warnings.Add(message.Trim());
                    if (messageSeverity <= MAVLink.MAV_SEVERITY.ERROR) warningColor = ProjectRed;
                    else if (messageSeverity <= MAVLink.MAV_SEVERITY.WARNING && warningColor != ProjectRed) warningColor = ProjectGold;
                }
            }
            var warning = string.Join(" · ", warnings);
            var warningHeight = warning.Length == 0 ? 0 : (compact ? 29 : 35);
            var bottom = Height - metricsHeight - stateHeight - warningHeight;
            var viewport = new Rectangle(0, headingHeight, Width, Math.Max(40, bottom - headingHeight));
            var cx = Width / 2f;
            var cy = viewport.Top + viewport.Height * .55f;
            var pitchScale = viewport.Height / 62f;

            // 清空已移出 HUD 的点击区；预解锁区在下面用真实状态重新建立。
            ekfhitzone = vibehitzone = prearmhitzone = Rectangle.Empty;
            graphicsObject.ResetTransform();
            using (var background = new SolidBrush(ProjectDark)) graphicsObject.FillRectangle(background, ClientRectangle);

            ProjectInstrumentClip(viewport, true);
            try
            {
                graphicsObject.TranslateTransform(cx, cy);
                // 与 MP 相同：正俯仰使地平线下移，正滚转使地平线反向旋转。
                graphicsObject.RotateTransform(-actualRoll);
                var horizon = actualPitch * pitchScale;
                var extent = Math.Max(Width, Height) * 4f + Math.Abs(horizon);
                using (var sky = new SolidBrush(Color.FromArgb(36, 81, 109)))
                using (var ground = new SolidBrush(Color.FromArgb(120, 84, 47)))
                using (var horizonPen = new Pen(Color.FromArgb(210, 226, 233), 1.3f))
                {
                    graphicsObject.FillRectangle(sky, new RectangleF(-extent, -extent, extent * 2, extent + horizon));
                    graphicsObject.FillRectangle(ground, new RectangleF(-extent, horizon, extent * 2, extent - horizon));
                    graphicsObject.DrawLine(horizonPen, -extent, horizon, extent, horizon);
                }
                if (displayrollpitch)
                {
                    using (var ladder = new Pen(ProjectInk, 1f))
                    {
                        for (var degrees = -90; degrees <= 90; degrees += 5)
                        {
                            if (degrees == 0 || Math.Abs(degrees - actualPitch) > 45) continue;
                            var y = horizon - degrees * pitchScale;
                            var longMark = degrees % 10 == 0;
                            var length = Width * (longMark ? .13f : .075f);
                            if (degrees > 0)
                            {
                                graphicsObject.DrawLine(ladder, -length, y, -8, y);
                                graphicsObject.DrawLine(ladder, 8, y, length, y);
                            }
                            else
                            {
                                // 虚线通过短线段绘制，保证 GDI 和 OpenGL 两条路径一致。
                                for (var x = 8f; x < length; x += 7)
                                {
                                    graphicsObject.DrawLine(ladder, x, y, Math.Min(length, x + 4), y);
                                    graphicsObject.DrawLine(ladder, -x, y, -Math.Min(length, x + 4), y);
                                }
                            }
                            if (longMark) ProjectText(Math.Abs(degrees).ToString(CultureInfo.InvariantCulture), label, ProjectInk, -length - 25, y - label / 2f, 22, 2);
                        }
                    }
                }
            }
            finally
            {
                graphicsObject.ResetTransform();
                ProjectInstrumentClip(viewport, false);
            }

            if (displayrollpitch)
            {
                var radius = Math.Min(Width * .245f, viewport.Height * .44f);
                using (var ticks = new Pen(Color.FromArgb(202, 220, 231), 1.2f))
                using (var aircraft = new Pen(ProjectGold, compact ? 2f : 2.5f))
                using (var gold = new SolidBrush(ProjectGold))
                {
                    graphicsObject.DrawArc(ticks, new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2), 210, 120);
                    foreach (var a in new[] { -60, -45, -30, -15, 0, 15, 30, 45, 60 })
                    {
                        var angle = (a - 90) * Math.PI / 180;
                        var inner = radius - (a % 30 == 0 ? 7 : 4);
                        graphicsObject.DrawLine(ticks, cx + (float)Math.Cos(angle) * radius, cy + (float)Math.Sin(angle) * radius,
                            cx + (float)Math.Cos(angle) * inner, cy + (float)Math.Sin(angle) * inner);
                    }
                    var bank = Math.Max(-60, Math.Min(60, actualRoll));
                    var pointerAngle = (bank - 90) * Math.PI / 180;
                    var px = cx + (float)Math.Cos(pointerAngle) * (radius - 10);
                    var py = cy + (float)Math.Sin(pointerAngle) * (radius - 10);
                    graphicsObject.FillPolygon(gold, new[] { new Point((int)px, (int)py - 4), new Point((int)px - 4, (int)py + 3), new Point((int)px + 4, (int)py + 3) });
                    var wing = Width * .14f;
                    graphicsObject.DrawLine(aircraft, cx - wing, cy, cx - 17, cy);
                    graphicsObject.DrawLine(aircraft, cx - 17, cy, cx - 9, cy + 5);
                    graphicsObject.DrawLine(aircraft, cx - 9, cy + 5, cx + 9, cy + 5);
                    graphicsObject.DrawLine(aircraft, cx + 9, cy + 5, cx + 17, cy);
                    graphicsObject.DrawLine(aircraft, cx + 17, cy, cx + wing, cy);
                    graphicsObject.DrawLine(aircraft, cx, cy - 9, cx, cy - 1);
                }
            }

            if (displayspeed) DrawProjectTape(viewport, cy, false, _airspeed, speedunit, label, value);
            if (displayalt) DrawProjectTape(viewport, cy, true, _alt, altunit, label, value);
            DrawProjectHeading(actualHeading, headingValid, headingHeight, label, compact ? 15 : 18);

            using (var background = new SolidBrush(ProjectDark)) graphicsObject.FillRectangle(background, new Rectangle(0, bottom, Width, Height - bottom));
            using (var edge = new Pen(Color.FromArgb(33, 61, 80))) graphicsObject.DrawLine(edge, 0, bottom, Width, bottom);
            var modeWidth = Math.Min(Width * .39f, 154);
            var modeText = connected ? (_mode ?? "—") : "—";
            var armed = !connected ? "未连接飞控" : status ? "已解锁" : "已锁定";
            if (connected && !status && displayprearm) armed += prearmstatus ? " · 准备解锁" : " · 未准备解锁";
            var stateColor = !connected ? ProjectMuted : status ? ProjectGold : prearmstatus ? ProjectGreen : ProjectRed;
            ProjectText(armed, text, stateColor, 6, bottom + 3, Width - modeWidth - 10);
            ProjectText(modeText, text, connected ? ProjectGreen : ProjectMuted, Width - modeWidth - 6, bottom + 3, modeWidth, 2);
            if (connected && !status && displayprearm) prearmhitzone = new Rectangle(0, bottom, (int)(Width - modeWidth), stateHeight);
            if (warningHeight > 0) ProjectWarningText(warning, text, warningColor, bottom + stateHeight, warningHeight);
            UpdateProjectInstrumentTip(warning);

            var metricsTop = Height - metricsHeight + 2;
            var column = Width / 3f;
            ProjectText("俯仰 " + (attitudeValid ? actualPitch.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "°" : "—"), label, ProjectInk, 5, metricsTop, column - 7);
            ProjectText("滚转 " + (attitudeValid ? actualRoll.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "°" : "—"), label, ProjectInk, column, metricsTop, column, 1);
            ProjectText("航向 " + (headingValid ? actualHeading.ToString("000", CultureInfo.InvariantCulture) + "°" : "—"), label, ProjectInk, column * 2, metricsTop, column - 5, 2);
        }

        private void DrawProjectHeading(double actual, bool valid, int height, int label, int value)
        {
            using (var dark = new SolidBrush(ProjectDark)) graphicsObject.FillRectangle(dark, new Rectangle(0, 0, Width, height));
            if (!displayheading) return;
            var cx = Width / 2f;
            using (var tick = new Pen(ProjectMuted))
            using (var gold = new SolidBrush(ProjectGold))
            {
                var spacing = (Width - 16) / 80f;
                var first = (int)Math.Floor(actual / 10) * 10 - 40;
                for (var i = first; i <= first + 90; i += 10)
                {
                    var x = cx + (float)(i - actual) * spacing;
                    if (x < 10 || x > Width - 10) continue;
                    var degrees = (int)ProjectHeading(i);
                    var caption = degrees == 0 ? "N" : degrees == 90 ? "E" : degrees == 180 ? "S" : degrees == 270 ? "W" : degrees.ToString(CultureInfo.InvariantCulture);
                    ProjectText(caption, label, ProjectMuted, x - 16, 2, 32, 1);
                    graphicsObject.DrawLine(tick, x, height - 9, x, height - 3);
                }
                var box = new RectangleF(cx - 26, 1, 52, height - 9);
                using (var dark = new SolidBrush(ProjectDark)) graphicsObject.FillRectangle(dark, box);
                graphicsObject.DrawRectangle(tick, box);
                ProjectText(valid ? actual.ToString("000", CultureInfo.InvariantCulture) + "°" : "—", value, ProjectInk, box.X + 2, 2, box.Width - 4, 1);
                graphicsObject.FillPolygon(gold, new[] { new Point((int)cx, height - 1), new Point((int)cx - 4, height - 7), new Point((int)cx + 4, height - 7) });
                graphicsObject.DrawLine(tick, 0, height - 1, Width, height - 1);
            }
        }

        private void DrawProjectTape(Rectangle viewport, float center, bool altitude, float reading, string unit, int label, int value)
        {
            var width = Width < 340 ? 46 : 58;
            var x = altitude ? Width - width : 0;
            var valid = connected && ProjectFinite(reading);
            var number = valid ? reading : 0;
            var top = viewport.Top + label + 8;
            var bottom = viewport.Bottom - 4;
            using (var dark = new SolidBrush(Color.FromArgb(231, 12, 32, 47))) graphicsObject.FillRectangle(dark, new RectangleF(x, top, width, Math.Max(1, bottom - top)));
            // 单位标题独立于数值带宽；常见 m/s、km/h 不因 46/58 像素数值窗而省略。
            var caption = (altitude ? "高度 " : "空速 ") + unit;
            var captionWidth = Math.Min(Width * .32f, Math.Max(width - 5, ProjectTextWidth(caption, label) + 2));
            var captionX = altitude ? Width - captionWidth - 3 : 3;
            ProjectText(caption, label, ProjectInk, captionX, viewport.Top + 3, captionWidth, altitude ? 2 : 0);
            if (valid)
            {
                var step = altitude ? (Math.Abs(number) > 1000 ? 50 : 10) : (number > 150 ? 20 : 5);
                var pixels = Math.Max(16, (bottom - top) / 5f);
                var first = (int)Math.Floor(number / step) * step - step * 4;
                using (var line = new Pen(ProjectMuted))
                {
                    for (var tick = first; tick <= first + step * 8; tick += step)
                    {
                        var y = center - (tick - number) / step * pixels;
                        if (y < top + label / 2f || y > bottom - label || (!altitude && tick < 0)) continue;
                        ProjectText(tick.ToString(CultureInfo.InvariantCulture), label, ProjectInk, x + (altitude ? 9 : 1), y - label / 2f, width - 10, 1);
                        var edge = altitude ? x : x + width;
                        graphicsObject.DrawLine(line, edge, y, edge + (altitude ? 6 : -6), y);
                    }
                }
            }
            var half = value / 2 + 5;
            var points = altitude
                ? new[] { new Point(x - 6, (int)center), new Point(x + 1, (int)center - half), new Point(Width - 2, (int)center - half), new Point(Width - 2, (int)center + half), new Point(x + 1, (int)center + half) }
                : new[] { new Point(width + 6, (int)center), new Point(width - 1, (int)center - half), new Point(2, (int)center - half), new Point(2, (int)center + half), new Point(width - 1, (int)center + half) };
            using (var dark = new SolidBrush(ProjectDark))
            using (var edge = new Pen(!altitude && _lowairspeed ? ProjectRed : ProjectInk, 1.2f))
            {
                graphicsObject.FillPolygon(dark, points);
                graphicsObject.DrawPolygon(edge, points);
            }
            var current = !valid ? "—" : number.ToString(altitude ? "0" : "0.0", CultureInfo.InvariantCulture);
            var size = value;
            while (size > 10 && ProjectTextWidth(current, size) > width - 6) size--;
            ProjectText(current, size, !altitude && _lowairspeed ? ProjectRed : ProjectInk, x + 3, center - size / 2f - 1, width - 6, 1);
        }

        private void ProjectInstrumentClip(Rectangle rectangle, bool enabled)
        {
            if (opengl)
            {
                if (enabled) { GL.Enable(EnableCap.ScissorTest); GL.Scissor(rectangle.X, Height - rectangle.Bottom, rectangle.Width, rectangle.Height); }
                else GL.Disable(EnableCap.ScissorTest);
            }
            else if (enabled) graphicsObjectGDIP.SetClip(rectangle);
            else graphicsObjectGDIP.ResetClip();
        }

        // 使用原 HUD 字形纹理缓存，工作台字形不画黑色描边并使用独立缓存键。
        private int HudCharacterKey(int character, float size, Color color)
        {
            return character ^ (int)(size * 1000) ^ color.ToArgb() ^ (ProjectInstrumentStyle ? int.MinValue : 0);
        }

        private int ProjectCharacterWidth(char character, int size)
        {
            var key = character | (size << 16);
            int width;
            if (projectCharacterWidths.TryGetValue(key, out width)) return width;
            var maximum = Width / 150f;
            using (var path = new GraphicsPath())
            {
                path.AddString(character.ToString(), projectInstrumentFont.FontFamily, 0, size, Point.Empty, StringFormat.GenericTypographic);
                // 空格没有路径点；GDI 对空路径读取 PathPoints 会抛出异常。
                if (path.PointCount > 0)
                    foreach (var point in path.PathPoints) maximum = Math.Max(maximum, point.X);
            }
            width = (int)(maximum + 2);
            projectCharacterWidths[key] = width;
            return width;
        }

        private int ProjectTextWidth(string caption, int size)
        {
            var width = 0;
            foreach (var character in caption) width += ProjectCharacterWidth(character, size);
            return width;
        }

        private string ProjectFitText(string caption, int size, float width)
        {
            if (ProjectTextWidth(caption, size) <= width) return caption;
            var result = new StringBuilder();
            var used = 0;
            var ellipsis = ProjectCharacterWidth('…', size);
            foreach (var character in caption)
            {
                var next = ProjectCharacterWidth(character, size);
                if (used + next + ellipsis > width) break;
                result.Append(character); used += next;
            }
            return result + "…";
        }

        private void ProjectText(string caption, int size, Color color, float x, float y, float width, int alignment = 0)
        {
            if (string.IsNullOrEmpty(caption) || width <= 0) return;
            caption = ProjectFitText(caption, size, width);
            var measured = ProjectTextWidth(caption, size);
            if (alignment == 1) x += (width - measured) / 2;
            else if (alignment == 2) x += width - measured;
            using (var brush = new SolidBrush(color)) drawstring(caption, projectInstrumentFont, size - 5, brush, x, y);
        }

        private void ProjectWarningText(string caption, int size, Color color, int y, int height)
        {
            var first = new StringBuilder();
            var offset = 0;
            var available = Width - 12;
            var used = 0;
            var clean = caption.Replace('\r', ' ').Replace('\n', ' ');
            foreach (var character in clean)
            {
                var next = ProjectCharacterWidth(character, size);
                if (used + next > available) break;
                first.Append(character); used += next; offset++;
            }
            ProjectText(first.ToString(), size, color, 6, y + 1, available);
            if (offset < clean.Length) ProjectText(clean.Substring(offset), size, color, 6, y + height / 2f, available);
        }

        private void UpdateProjectInstrumentTip(string caption)
        {
            // 长报文保留完整内容，窄面板两行之外可悬停查看，不改变原消息列表。
            if (projectInstrumentTipText == caption) return;
            projectInstrumentTipText = caption;
            if (projectInstrumentTip == null) projectInstrumentTip = new ToolTip { ShowAlways = true, AutoPopDelay = 15000 };
            projectInstrumentTip.SetToolTip(this, caption);
        }
    }
}
