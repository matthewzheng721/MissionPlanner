using System.Diagnostics;
using System.IO;
using MissionPlanner.Utilities;

namespace MissionPlanner
{
    // 网络诊断日志与当前运行数据目录一致；未启用独立模式时使用原配置路径。
    public sealed class RuntimeNetworkTraceListener : TextWriterTraceListener
    {
        public RuntimeNetworkTraceListener(string originalPath)
            : base(Settings.RuntimeDataDirectory == null
                ? originalPath
                : Path.Combine(Settings.GetDataDirectory(), "network.log"))
        {
        }
    }
}
