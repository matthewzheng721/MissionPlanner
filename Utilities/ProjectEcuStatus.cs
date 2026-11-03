using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MissionPlanner.Utilities
{
    // 仅识别已确认的 ECU 文本格式；保留时间，不把历史值称为实时正常。
    public sealed class ProjectEcuStatus
    {
        private static readonly Regex temperatures = new Regex(@"ECU Cy1T\s+C1:(-?\d+(?:\.\d+)?)\s+C2:(-?\d+(?:\.\d+)?)\s+C3:(-?\d+(?:\.\d+)?)\s+C4:(-?\d+(?:\.\d+)?)");
        private static readonly Regex throttle = new Regex(@"ECU ACK:\s*throttle valid=([01])\b");
        public double[] Temperatures { get; private set; }
        public int? ThrottleValid { get; private set; }
        public DateTime TemperatureTime { get; private set; }
        public DateTime ThrottleTime { get; private set; }
        public void Observe(string text, DateTime time)
        {
            if (text == null) return;
            var match = temperatures.Match(text);
            if (match.Success && time >= TemperatureTime)
            {
                Temperatures = Enumerable.Range(1, 4).Select(i => double.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
                TemperatureTime = time;
            }
            match = throttle.Match(text);
            if (match.Success && time >= ThrottleTime) { ThrottleValid = int.Parse(match.Groups[1].Value); ThrottleTime = time; }
        }
        public string TemperatureText => Temperatures == null ? "四缸温度：—（未收到已知 ECU 反馈）" : "四缸温度：" + string.Join(" / ", Temperatures.Select(t => t.ToString("0.#", CultureInfo.InvariantCulture))) + " ℃　接收于 " + TemperatureTime.ToString("HH:mm:ss");
        public string ThrottleText => ThrottleValid.HasValue ? "throttle valid=" + ThrottleValid.Value + "　接收于 " + ThrottleTime.ToString("HH:mm:ss") : "throttle valid：—（未收到反馈）";
    }
}
