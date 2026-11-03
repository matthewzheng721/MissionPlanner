using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace MissionPlanner.Controls
{
    public partial class HUD
    {
        // 内部回退只在代码中选择；不新增公开属性、菜单或第二套遥测状态。
        private static readonly bool UseProjectAvionics = true;

        // 外框、文字和状态沿用工程令牌；天地保留蓝天/棕地航空语义。
        private static readonly Color AvionicsDark = Color.FromArgb(0x0B, 0x11, 0x1B);
        private static readonly Color AvionicsPanel = Color.FromArgb(0x11, 0x1D, 0x2A);
        private static readonly Color AvionicsSection = Color.FromArgb(0x18, 0x27, 0x37);
        private static readonly Color AvionicsGround = Color.FromArgb(0x6F, 0x51, 0x31);
        private static readonly Color AvionicsSky = Color.FromArgb(0x23, 0x4D, 0x66);
        private static readonly Color AvionicsBorder = Color.FromArgb(0x2B, 0x40, 0x53);
        private static readonly Color AvionicsInk = Color.FromArgb(0xE6, 0xEE, 0xF6);
        private static readonly Color AvionicsMuted = Color.FromArgb(0x91, 0xA8, 0xBB);
        private static readonly Color AvionicsCyan = Color.FromArgb(0x35, 0xB9, 0xF2);
        private static readonly Color AvionicsGreen = Color.FromArgb(0x34, 0xD3, 0xA0);
        private static readonly Color AvionicsAmber = Color.FromArgb(0xF2, 0xB8, 0x4B);
        private static readonly Color AvionicsRed = Color.FromArgb(0xF0, 0x64, 0x70);

        private void DrawProjectAvionics()
        {
            if (Width < 100 || Height < 100) return;
            if (projectInstrumentFont == null) projectInstrumentFont = new Font("Microsoft YaHei UI", 10f);
            var compact = Width < 340 || Height < 260;
            var label = compact ? 10 : 12;
            var text = compact ? 11 : 13;
            var value = compact ? 20 : 24;
            var headingHeight = compact ? 29 : 38;
            var metricsHeight = compact ? 19 : 25;
            var stateHeight = compact ? 20 : 25;
            var attitudeValid = connected && ProjectFinite(_pitch) && ProjectFinite(_roll);
            var headingValid = connected && ProjectFinite(_heading);
            var actualPitch = attitudeValid ? _pitch : 0;
            var actualRoll = attitudeValid ? _roll : 0;
            var actualHeading = headingValid ? ProjectHeading(_heading) : 0;

            var warnings = new List<string>();
            var warningColor = AvionicsInk;
            if (connected)
            {
                if (!attitudeValid || !headingValid) { warnings.Add("姿态 / 航向数据无效"); warningColor = AvionicsRed; }
                if (failsafe) { warnings.Add(HUDT.FAILSAFE); warningColor = AvionicsRed; }
                if (safetyactive) { warnings.Add(HUDT.SAFE); if (warningColor != AvionicsRed) warningColor = AvionicsAmber; }
                if (!string.IsNullOrWhiteSpace(message))
                {
                    warnings.Add(message.Trim());
                    if (messageSeverity <= MAVLink.MAV_SEVERITY.ERROR) warningColor = AvionicsRed;
                    else if (messageSeverity <= MAVLink.MAV_SEVERITY.WARNING && warningColor != AvionicsRed) warningColor = AvionicsAmber;
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
            using (var background = new SolidBrush(AvionicsDark)) graphicsObject.FillRectangle(background, ClientRectangle);

            ProjectInstrumentClip(viewport, true);
            try
            {
                graphicsObject.TranslateTransform(cx, cy);
                // 与 MP 相同：正俯仰使地平线下移，正滚转使地平线反向旋转。
                graphicsObject.RotateTransform(-actualRoll);
                var horizon = actualPitch * pitchScale;
                var extent = Math.Max(Width, Height) * 4f + Math.Abs(horizon);
                using (var sky = new SolidBrush(AvionicsSky))
                using (var ground = new SolidBrush(AvionicsGround))
                using (var horizonPen = new Pen(AvionicsCyan, 1.3f))
                {
                    graphicsObject.FillRectangle(sky, new RectangleF(-extent, -extent, extent * 2, extent + horizon));
                    graphicsObject.FillRectangle(ground, new RectangleF(-extent, horizon, extent * 2, extent - horizon));
                    graphicsObject.DrawLine(horizonPen, -extent, horizon, extent, horizon);
                }
                if (displayrollpitch)
                {
                    using (var ladder = new Pen(AvionicsInk, 1f))
                    {
                        for (var degrees = -90; degrees <= 90; degrees += 5)
                        {
                            if (degrees == 0 || Math.Abs(degrees - actualPitch) > 45) continue;
                            var y = horizon - degrees * pitchScale;
                            var longMark = degrees % 10 == 0;
                            // 20°、10°、5°三级线长只改变识别层级，不改变角度对应的高度。
                            var length = Width * (degrees % 20 == 0 ? .145f : longMark ? .115f : .065f);
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
                            if (longMark)
                            {
                                var caption = Math.Abs(degrees).ToString(CultureInfo.InvariantCulture);
                                ProjectText(caption, label, AvionicsInk, -length - 25, y - label / 2f, 22, 2);
                                ProjectText(caption, label, AvionicsInk, length + 3, y - label / 2f, 22);
                            }
                            // 正俯仰短钩向下，负俯仰短钩向上；虚实线继续提供方向区分。
                            var hook = degrees > 0 ? 3f : -3f;
                            graphicsObject.DrawLine(ladder, -length, y, -length, y + hook);
                            graphicsObject.DrawLine(ladder, length, y, length, y + hook);
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
                using (var ticks = new Pen(AvionicsInk, 1.2f))
                using (var aircraft = new Pen(AvionicsAmber, compact ? 2f : 2.5f))
                using (var gold = new SolidBrush(AvionicsAmber))
                {
                    // HUD.DrawArc 在 GL 与 GDI 的角度解释不同，短线弧共用相同端点。
                    DrawAvionicsBankArc(ticks, cx, cy, radius);
                    foreach (var a in new[] { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 })
                    {
                        var angle = (a - 90) * Math.PI / 180;
                        var inner = radius - (a % 30 == 0 ? 9 : a % 45 == 0 ? 6 : 4);
                        graphicsObject.DrawLine(ticks, cx + (float)Math.Cos(angle) * radius, cy + (float)Math.Sin(angle) * radius,
                            cx + (float)Math.Cos(angle) * inner, cy + (float)Math.Sin(angle) * inner);
                    }
                    var bank = Math.Max(-60, Math.Min(60, actualRoll));
                    var pointerAngle = (bank - 90) * Math.PI / 180;
                    var px = cx + (float)Math.Cos(pointerAngle) * (radius - 10);
                    var py = cy + (float)Math.Sin(pointerAngle) * (radius - 10);
                    graphicsObject.FillPolygon(gold, new[] { new Point((int)px, (int)py - 4), new Point((int)px - 4, (int)py + 3), new Point((int)px + 4, (int)py + 3) });
                    // 中央固定三角与移动指针分别表示零滚转基准和实际滚转。
                    graphicsObject.DrawLine(ticks, cx - 4, cy - radius - 5, cx, cy - radius - 1);
                    graphicsObject.DrawLine(ticks, cx, cy - radius - 1, cx + 4, cy - radius - 5);
                    var wing = Width * .14f;
                    graphicsObject.DrawLine(aircraft, cx - wing, cy, cx - 17, cy);
                    graphicsObject.DrawLine(aircraft, cx - 17, cy, cx - 9, cy + 5);
                    graphicsObject.DrawLine(aircraft, cx - 9, cy + 5, cx - 4, cy + 5);
                    graphicsObject.DrawLine(aircraft, cx + 4, cy + 5, cx + 9, cy + 5);
                    graphicsObject.DrawLine(aircraft, cx + 9, cy + 5, cx + 17, cy);
                    graphicsObject.DrawLine(aircraft, cx + 17, cy, cx + wing, cy);
                    graphicsObject.DrawLine(aircraft, cx - wing, cy, cx - wing, cy + 5);
                    graphicsObject.DrawLine(aircraft, cx + wing, cy, cx + wing, cy + 5);
                    graphicsObject.DrawPolygon(aircraft, new[] {
                        new Point((int)cx, (int)cy - 3), new Point((int)cx + 3, (int)cy),
                        new Point((int)cx, (int)cy + 3), new Point((int)cx - 3, (int)cy) });
                }
            }

            DrawAvionicsFrame(viewport);
            if (displayspeed) DrawAvionicsTape(viewport, cy, false, _airspeed, speedunit, label, value);
            if (displayalt) DrawAvionicsTape(viewport, cy, true, _alt, altunit, label, value);
            DrawAvionicsHeading(actualHeading, headingValid, headingHeight, label, compact ? 17 : 20);

            using (var background = new SolidBrush(AvionicsDark)) graphicsObject.FillRectangle(background, new Rectangle(0, bottom, Width, Height - bottom));
            using (var edge = new Pen(AvionicsBorder)) graphicsObject.DrawLine(edge, 0, bottom, Width, bottom);
            var modeWidth = Math.Min(Width * .39f, 154);
            var modeText = connected ? (_mode ?? "—") : "—";
            var armed = !connected ? "未连接飞控" : status ? "已解锁" : "已锁定";
            if (connected && !status && displayprearm) armed += prearmstatus ? " · 准备解锁" : " · 未准备解锁";
            var stateColor = !connected ? AvionicsMuted : status ? AvionicsAmber : prearmstatus ? AvionicsGreen : AvionicsRed;
            ProjectText(armed, text, stateColor, 6, bottom + 3, Width - modeWidth - 10);
            using (var modeFill = new SolidBrush(AvionicsPanel))
                graphicsObject.FillRectangle(modeFill, new RectangleF(Width - modeWidth - 8, bottom + 2, modeWidth + 4, stateHeight - 3));
            using (var modeEdge = new Pen(connected ? AvionicsGreen : AvionicsMuted, 2))
                graphicsObject.DrawLine(modeEdge, Width - modeWidth - 8, bottom + 3, Width - modeWidth - 8, bottom + stateHeight - 3);
            ProjectText("模式", label, AvionicsMuted, Width - modeWidth - 3, bottom + 4, label * 2 + 4);
            ProjectText(modeText, text, connected ? AvionicsGreen : AvionicsMuted, Width - modeWidth + label * 2 + 4, bottom + 3, modeWidth - label * 2 - 13, 2);
            if (connected && !status && displayprearm) prearmhitzone = new Rectangle(0, bottom, (int)(Width - modeWidth), stateHeight);
            if (warningHeight > 0) ProjectWarningText(warning, text, warningColor, bottom + stateHeight, warningHeight);
            UpdateProjectInstrumentTip(warning);

            var metricsTop = Height - metricsHeight + 2;
            var column = Width / 3f;
            using (var metricFill = new SolidBrush(AvionicsSection))
                graphicsObject.FillRectangle(metricFill, new RectangleF(0, metricsTop - 2, Width, metricsHeight));
            using (var metricEdge = new Pen(AvionicsBorder))
            {
                graphicsObject.DrawLine(metricEdge, 0, metricsTop - 2, Width, metricsTop - 2);
                graphicsObject.DrawLine(metricEdge, column, metricsTop + 3, column, Height - 4);
                graphicsObject.DrawLine(metricEdge, column * 2, metricsTop + 3, column * 2, Height - 4);
            }
            DrawAvionicsMetric("俯仰", attitudeValid ? actualPitch.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "°" : "—", 0, metricsTop, column, label, text);
            DrawAvionicsMetric("滚转", attitudeValid ? actualRoll.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "°" : "—", column, metricsTop, column, label, text);
            DrawAvionicsMetric("航向", headingValid ? actualHeading.ToString("000", CultureInfo.InvariantCulture) + "°" : "—", column * 2, metricsTop, column, label, text);
        }

        private void DrawAvionicsHeading(double actual, bool valid, int height, int label, int value)
        {
            using (var dark = new SolidBrush(AvionicsDark)) graphicsObject.FillRectangle(dark, new Rectangle(0, 0, Width, height));
            if (!displayheading) return;
            var cx = Width / 2f;
            using (var tick = new Pen(AvionicsMuted))
            using (var gold = new SolidBrush(AvionicsAmber))
            {
                var spacing = (Width - 16) / 80f;
                var first = (int)Math.Floor(actual / 10) * 10 - 40;
                for (var i = first; i <= first + 90; i += 5)
                {
                    var x = cx + (float)(i - actual) * spacing;
                    if (x < 10 || x > Width - 10) continue;
                    var major = i % 10 == 0;
                    if (major)
                    {
                        var degrees = (int)ProjectHeading(i);
                        var caption = degrees == 0 ? "N" : degrees == 90 ? "E" : degrees == 180 ? "S" : degrees == 270 ? "W" : degrees.ToString(CultureInfo.InvariantCulture);
                        ProjectText(caption, label, AvionicsMuted, x - 16, 2, 32, 1);
                    }
                    graphicsObject.DrawLine(tick, x, height - (major ? 9 : 6), x, height - 3);
                }
                var boxWidth = Width < 340 || Height < 260 ? 60 : 70;
                var box = new RectangleF(cx - boxWidth / 2f, 1, boxWidth, height - 9);
                using (var dark = new SolidBrush(AvionicsPanel)) graphicsObject.FillRectangle(dark, box);
                using (var focus = new Pen(AvionicsCyan, 1.2f)) graphicsObject.DrawRectangle(focus, box);
                ProjectText(valid ? actual.ToString("000", CultureInfo.InvariantCulture) + "°" : "—", value, AvionicsInk, box.X + 2, 2, box.Width - 4, 1);
                graphicsObject.FillPolygon(gold, new[] { new Point((int)cx, height - 1), new Point((int)cx - 4, height - 7), new Point((int)cx + 4, height - 7) });
                graphicsObject.DrawLine(tick, 0, height - 1, Width, height - 1);
            }
        }

        private void DrawAvionicsTape(Rectangle viewport, float center, bool altitude, float reading, string unit, int label, int value)
        {
            var width = Width < 340 ? 52 : 64;
            var x = altitude ? Width - width : 0;
            var valid = connected && ProjectFinite(reading);
            var number = valid ? reading : 0;
            var top = viewport.Top + label + 8;
            var bottom = viewport.Bottom - 4;
            var half = value / 2 + 5;
            using (var dark = new SolidBrush(AvionicsPanel)) graphicsObject.FillRectangle(dark, new RectangleF(x, top, width, Math.Max(1, bottom - top)));
            var scaleEdge = altitude ? x : x + width;
            using (var spine = new Pen(AvionicsBorder))
                graphicsObject.DrawLine(spine, scaleEdge, top, scaleEdge, bottom);
            // 单位标题独立于数值带宽；常见 m/s、km/h 不因数值窗而省略。
            var caption = (altitude ? "高度 " : "空速 ") + unit;
            var captionWidth = Math.Min(Width * .32f, Math.Max(width - 5, ProjectTextWidth(caption, label) + 2));
            var captionX = altitude ? Width - captionWidth - 3 : 3;
            ProjectText(caption, label, AvionicsInk, captionX, viewport.Top + 3, captionWidth, altitude ? 2 : 0);
            if (valid)
            {
                var step = altitude ? (Math.Abs(number) > 1000 ? 50 : 10) : (number > 150 ? 20 : 5);
                var pixels = Math.Max(16, (bottom - top) / 5f);
                var first = (int)Math.Floor(number / step) * step - step * 4;
                using (var line = new Pen(AvionicsMuted))
                {
                    for (var tick = first; tick <= first + step * 8; tick += step)
                    {
                        var y = center - (tick - number) / step * pixels;
                        if (y < top + label / 2f || y > bottom - label || (!altitude && tick < 0)) continue;
                        var labelTop = y - label / 2f;
                        // 真实截图中邻近数字会被主读数窗裁成半字；相交标签整体隐藏，刻度位置不变。
                        if (labelTop + label + 3 < center - half - 1 || labelTop > center + half + 1)
                            ProjectText(tick.ToString(CultureInfo.InvariantCulture), label, AvionicsMuted, x + (altitude ? 9 : 1), labelTop, width - 10, 1);
                        var edge = altitude ? x : x + width;
                        graphicsObject.DrawLine(line, edge, y, edge + (altitude ? 8 : -8), y);
                    }
                    // 每两个原有主刻度之间增加一个半步刻度，原步进和每单位像素比例不变。
                    for (var tick = first + step / 2f; tick <= first + step * 8; tick += step)
                    {
                        var y = center - (tick - number) / step * pixels;
                        if (y < top + label / 2f || y > bottom - label || (!altitude && tick < 0)) continue;
                        graphicsObject.DrawLine(line, scaleEdge, y, scaleEdge + (altitude ? 4 : -4), y);
                    }
                }
            }
            var points = altitude
                ? new[] { new Point(x - 6, (int)center), new Point(x + 1, (int)center - half), new Point(Width - 2, (int)center - half), new Point(Width - 2, (int)center + half), new Point(x + 1, (int)center + half) }
                : new[] { new Point(width + 6, (int)center), new Point(width - 1, (int)center - half), new Point(2, (int)center - half), new Point(2, (int)center + half), new Point(width - 1, (int)center + half) };
            using (var dark = new SolidBrush(AvionicsDark))
            using (var edge = new Pen(!altitude && _lowairspeed ? AvionicsRed : AvionicsCyan, 1.2f))
            {
                graphicsObject.FillPolygon(dark, points);
                graphicsObject.DrawPolygon(edge, points);
            }
            var current = !valid ? "—" : number.ToString(altitude ? "0" : "0.0", CultureInfo.InvariantCulture);
            DrawAvionicsTapeValue(current, !altitude && valid, value, !altitude && _lowairspeed ? AvionicsRed : AvionicsInk,
                x + 4, center, width - 8);
        }

        private void DrawAvionicsTapeValue(string current, bool splitDecimal, int preferredSize, Color color, float x, float center, float width)
        {
            // 保留同一个格式化读数字符串；整数主读数放大，小数尾位单独降低字号。
            var point = splitDecimal ? current.IndexOf('.') : -1;
            if (point < 0)
            {
                var size = preferredSize;
                while (size > 10 && ProjectTextWidth(current, size) > width) size--;
                ProjectText(current, size, color, x, center - size / 2f - 1, width, 1);
                return;
            }
            var whole = current.Substring(0, point);
            var fraction = current.Substring(point);
            var wholeSize = preferredSize;
            var fractionSize = Math.Max(10, wholeSize - 6);
            while (wholeSize > 10 && ProjectTextWidth(whole, wholeSize) + ProjectTextWidth(fraction, fractionSize) > width)
            {
                wholeSize--;
                fractionSize = Math.Max(10, wholeSize - 6);
            }
            var wholeWidth = ProjectTextWidth(whole, wholeSize);
            var fractionWidth = ProjectTextWidth(fraction, fractionSize);
            var start = x + (width - wholeWidth - fractionWidth) / 2;
            var top = center - wholeSize / 2f - 1;
            ProjectText(whole, wholeSize, color, start, top, wholeWidth);
            ProjectText(fraction, fractionSize, color, start + wholeWidth, top + wholeSize - fractionSize, fractionWidth);
        }

        private void DrawAvionicsMetric(string caption, string reading, float x, float y, float width, int label, int value)
        {
            var labelWidth = label * 2 + 3;
            ProjectText(caption, label, AvionicsMuted, x + 5, y + 1, labelWidth);
            ProjectText(reading, value, AvionicsInk, x + labelWidth + 7, y, width - labelWidth - 12, 2);
        }

        private void DrawAvionicsBankArc(Pen pen, float cx, float cy, float radius)
        {
            // 固定 40 段；不使用每帧路径细分、渐变纹理或额外位图。
            var previous = AvionicsArcPoint(cx, cy, radius, -60);
            for (var angle = -57; angle <= 60; angle += 3)
            {
                var next = AvionicsArcPoint(cx, cy, radius, angle);
                graphicsObject.DrawLine(pen, previous.X, previous.Y, next.X, next.Y);
                previous = next;
            }
        }

        private static PointF AvionicsArcPoint(float cx, float cy, float radius, int angle)
        {
            var radians = (angle - 90) * Math.PI / 180;
            return new PointF(cx + (float)Math.Cos(radians) * radius, cy + (float)Math.Sin(radians) * radius);
        }

        private void DrawAvionicsFrame(Rectangle viewport)
        {
            // 角标只描述仪表区域，不覆盖姿态中心和两侧数值。
            var inset = Width < 340 ? 56 : 68;
            var left = inset;
            var right = Width - inset;
            var top = viewport.Top + 4;
            var bottom = viewport.Bottom - 4;
            using (var edge = new Pen(AvionicsBorder))
            {
                graphicsObject.DrawLine(edge, left, top, left + 13, top);
                graphicsObject.DrawLine(edge, left, top, left, top + 9);
                graphicsObject.DrawLine(edge, right - 13, top, right, top);
                graphicsObject.DrawLine(edge, right, top, right, top + 9);
                graphicsObject.DrawLine(edge, left, bottom, left + 13, bottom);
                graphicsObject.DrawLine(edge, left, bottom - 9, left, bottom);
                graphicsObject.DrawLine(edge, right - 13, bottom, right, bottom);
                graphicsObject.DrawLine(edge, right, bottom - 9, right, bottom);
            }
        }
    }
}
