using System;
using System.Drawing;
using System.Globalization;
using SkiaSharp;

namespace MissionPlanner.Controls
{
    public partial class QuickView
    {
        // 字体在首次构造控件前初始化，整个进程复用，避免绘制过程中反复创建和首次加载重入。
        private static readonly SKTypeface ProjectTitleTypeface = CreateProjectTypeface("Microsoft YaHei UI");
        private static readonly SKTypeface ProjectNumberTypeface = CreateProjectTypeface("Segoe UI");
        private static SKTypeface CreateProjectTypeface(string family)
        {
            var face = SKTypeface.FromFamilyName(family);
            using (var paint = new SKPaint { Typeface = face, TextSize = 12 }) paint.MeasureText("高度 0123456789");
            return face;
        }

        // 显示适配属性由项目主题设置；旧控件及其他调用方默认仍走原绘制分支。
        public bool ProjectTelemetryStyle { get; set; }
        public Color ProjectMutedColor { get; set; } = Color.LightSteelBlue;
        public Color ProjectEdgeColor { get; set; } = Color.SlateGray;
        public Color ProjectAccentColor { get; set; } = Color.DeepSkyBlue;

        // 仅拆分原描述末尾单位，不改数值、不换算、不推测缺少的单位。
        public static string[] ProjectDisplayParts(string description)
        {
            var label = description ?? string.Empty;
            var unit = string.Empty;
            var index = label.LastIndexOf('(');
            if (index > 0 && label.EndsWith(")", StringComparison.Ordinal))
            {
                unit = label.Substring(index + 1, label.Length - index - 2).Trim();
                label = label.Substring(0, index).Trim();
            }
            switch(label)
            {
                case "Altitude": label = "高度"; break;
                case "Ground Speed": label = "地速"; break;
                case "Air Speed": label = "空速"; break;
                case "Dist to Home": label = "距 Home"; break;
            }
            return new[] { label, unit };
        }

        public string ProjectFormattedNumber => DataUnavailable || double.IsNaN(number) || double.IsInfinity(number)
            ? "—" : number.ToString(numberformat, CultureInfo.CurrentCulture);

        private static SKColor ProjectSkColor(Color color) => new SKColor(color.R, color.G, color.B, color.A);

        private void DrawProjectTelemetry(SKCanvas canvas, int pixelWidth, int pixelHeight)
        {
            if (Width < 8 || Height < 8) return;
            canvas.Save();
            try
            {
                canvas.Scale(pixelWidth / (float)Width, pixelHeight / (float)Height);
                canvas.Clear(ProjectSkColor(BackColor));
                var compact = Width < 160 || Height < 90;
                var inset = compact ? 9f : 12f;
                var parts = ProjectDisplayParts(desc);
                var type = ProjectTitleTypeface;
                var digits = ProjectNumberTypeface;
                using(var paint = new SKPaint { IsAntialias = true, Typeface = type })
                {
                    paint.Color = ProjectSkColor(ProjectEdgeColor);
                    paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1;
                    canvas.DrawRoundRect(new SKRect(.5f,.5f,Width-.5f,Height-.5f), 5,5,paint);
                    paint.Style = SKPaintStyle.Fill;
                    paint.TextSize = compact ? 11 : 12;
                    paint.Color = ProjectSkColor(ProjectMutedColor);
                    var title = parts[0];
                    while (title.Length > 1 && paint.MeasureText(title) > Width - inset * 2)
                        title = title.Substring(0, title.Length - 1);
                    if (title != parts[0] && title.Length > 1) title = title.Substring(0,title.Length-1)+"…";
                    canvas.DrawText(title, inset, inset + paint.TextSize, paint);
                    var unitWidth = paint.MeasureText(parts[1]);
                    var available = Math.Max(18, Width - inset * 2 - (unitWidth > 0 ? unitWidth + 8 : 0));
                    var numberText = ProjectFormattedNumber;
                    paint.Typeface = digits;
                    paint.TextSize = Math.Min(compact ? 28 : 34, Math.Max(18, Height * .35f));
                    var measured = paint.MeasureText(numberText);
                    if(measured > available) paint.TextSize = Math.Max(10, paint.TextSize * available / measured);
                    paint.Color = ProjectSkColor(DataUnavailable ? ProjectMutedColor : numberColor);
                    var baseline = Math.Min(Height - 12, Math.Max(inset + 40, Height * .76f));
                    canvas.DrawText(numberText, inset, baseline, paint);
                    paint.Typeface = type; paint.TextSize = compact ? 10 : 11;
                    paint.Color = ProjectSkColor(ProjectMutedColor);
                    canvas.DrawText(parts[1], Width - inset - paint.MeasureText(parts[1]), baseline, paint);
                    // 只指示有无当前数据；不生成趋势或额外健康判断。
                    paint.Color = ProjectSkColor(DataUnavailable ? ProjectEdgeColor : ProjectAccentColor);
                    canvas.DrawRect(inset, Height-4, Math.Min(28,Width-inset*2), 2, paint);
                }
            }
            finally { canvas.Restore(); }
        }
    }
}
