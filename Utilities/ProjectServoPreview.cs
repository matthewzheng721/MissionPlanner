using System;
using System.Collections.Generic;

namespace MissionPlanner.Utilities
{
    // 仅用于本项目四舵面方向示意；没有舵角传感器，不把归一化值称为实测角度。
    public static class ProjectSurfaceMotion
    {
        public static double[] ForDemand(double pitchUp, double rollRight)
        {
            return new[] { Clamp(pitchUp - rollRight), Clamp(pitchUp), Clamp(pitchUp), Clamp(pitchUp + rollRight) };
        }
        public static double FromPwm(int surface, double pwm) => Clamp((pwm - 1500) / 500 * (surface < 2 ? -1 : 1));
        public static double Clamp(double value) => Math.Max(-1, Math.Min(1, value));
        public static double[] ForAttitude(double targetPitch, double targetRoll, double pitch, double roll) => ForDemand((targetPitch - pitch) / 25, (targetRoll - roll) / 35);
        public static int RcTarget(int min, int trim, int max, bool reversed, int direction, double fraction)
        {
            if (min <= 0 || min >= trim || trim >= max || max >= ushort.MaxValue - 1 || fraction <= 0 || fraction > 1)
                throw new InvalidOperationException("RC 输入范围或行程比例无效。");
            return trim + direction * (reversed ? -1 : 1) * (int)(Math.Min(trim - min, max - trim) * fraction);
        }
    }

    // 对照 sd_pwm_test.lua v3 的理想时序预览，不模拟链路延迟或脚本调度失败。
    public sealed class ProjectServoPreview
    {
        public static readonly string[] Names = { "MIN", "MAX", "PERIOD", "DURATION", "MODE", "STEP", "FRAC", "S1_MIN", "S1_MAX", "S2_MIN", "S2_MAX", "S3_MIN", "S3_MAX", "S4_MIN", "S4_MAX" };
        public readonly int[] Low = new int[4], High = new int[4], Trim = new int[4];
        public int Mode { get; private set; }
        public double Duration { get; private set; }
        public double Period { get; private set; }
        private int minimum, maximum, step;
        private static bool Pwm(double v) => v >= 800 && v <= 2200 && v == Math.Floor(v);
        public static ProjectServoPreview Create(IDictionary<string, double> values)
        {
            Func<string, double> get = name => { double v; if (!values.TryGetValue(name, out v) || double.IsNaN(v) || double.IsInfinity(v)) throw new InvalidOperationException("缺少有效参数 " + name); return v; };
            var a = get("SDT_MIN"); var b = get("SDT_MAX"); var p = get("SDT_PERIOD"); var d = get("SDT_DURATION"); var m = get("SDT_MODE"); var st = get("SDT_STEP"); var f = get("SDT_FRAC");
            if (!Pwm(a) || !Pwm(b) || a >= b) throw new InvalidOperationException("需满足整数 800 ≤ MIN < MAX ≤ 2200。");
            if (p < .1 || p > 10 || d < .1 || d > 3600 || (m != 0 && m != 1 && m != 2) || f < .1 || f > 1 || (m == 1 && (st < 1 || st > 1400 || st != Math.Floor(st))))
                throw new InvalidOperationException("检查周期、时长、模式、阶跃增量及 FRAC（0.10～1.00）。");
            var result = new ProjectServoPreview { minimum = (int)a, maximum = (int)b, Mode = (int)m, step = (int)st, Period = Math.Floor(p * 100 + .5) / 100, Duration = Math.Floor(d * 100 + .5) / 100 };
            for (var i = 0; i < 4; i++)
            {
                var n = (i + 1).ToString(); var low = get("SERVO" + n + "_MIN"); var high = get("SERVO" + n + "_MAX"); var trim = get("SERVO" + n + "_TRIM");
                if (!Pwm(low) || !Pwm(high) || !Pwm(trim) || low >= trim || trim >= high) throw new InvalidOperationException("SERVO" + n + " 范围须包含 TRIM。");
                var x = get("SDT_S" + n + "_MIN"); var y = get("SDT_S" + n + "_MAX");
                if ((x != 0 && !Pwm(x)) || (y != 0 && !Pwm(y))) throw new InvalidOperationException("S" + n + " 独立限幅应为 0 或整数 PWM。");
                x = x == 0 ? low : x; y = y == 0 ? high : y;
                if (x < low || x >= trim || y <= trim || y > high) throw new InvalidOperationException("S" + n + " 独立限幅须包含 TRIM 且位于校准范围内。");
                var span = Math.Floor(Math.Min(trim - low, high - trim) * f + .0001);
                result.Low[i] = (int)Math.Max(x, trim - span); result.High[i] = (int)Math.Min(y, trim + span); result.Trim[i] = (int)trim;
                if (result.Low[i] >= trim || result.High[i] <= trim) throw new InvalidOperationException("S" + n + " 可用行程不足。");
            }
            return result;
        }
        public int Requested(double seconds)
        {
            var interval = Mode == 0 ? Period / 2 : Mode == 1 ? Period : .02;
            var index = (long)Math.Floor(Math.Max(0, seconds) / interval + .0000001);
            if (Mode == 0) return index % 2 == 0 ? maximum : minimum;
            if (Mode == 1) return (int)Math.Min(minimum + index % ((long)Math.Ceiling((maximum - minimum) / (double)step) + 1) * step, maximum);
            return (int)Math.Floor((minimum + maximum) / 2d - (maximum - minimum) / 2d * Math.Cos(2 * Math.PI * (index * interval % Period) / Period) + .5);
        }
        public int[] At(double seconds)
        {
            if (seconds >= Duration + 1) return null;
            if (seconds >= Duration) return (int[])Trim.Clone();
            var requested = Requested(seconds); var result = new int[4];
            for (var i = 0; i < 4; i++) result[i] = Math.Max(Low[i], Math.Min(High[i], requested));
            return result;
        }
    }
}
