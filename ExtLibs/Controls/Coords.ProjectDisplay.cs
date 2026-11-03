using System;

namespace MissionPlanner.Controls
{
    public partial class Coords
    {
        // 只改变项目光标高程的无数据文本，不修改原Alt数值和查询结果。
        public bool ProjectHideUnavailableAltitude { get; set; }
        private string ProjectAltitudeDisplay => ProjectHideUnavailableAltitude &&
            (string.IsNullOrEmpty(AltSource) || string.Equals(AltSource, "Invalid", StringComparison.OrdinalIgnoreCase) || double.IsNaN(Alt) || double.IsInfinity(Alt))
            ? "— " + AltUnit : Alt.ToString("0.00") + AltUnit;
    }
}
