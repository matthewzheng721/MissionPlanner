using GMap.NET;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;

namespace MissionPlanner.Utilities.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class LogMapTests
    {
        [DataTestMethod]
        [DataRow(149.135367797852, 35.3642621765137, 149.166237426758, -35.3642621765136)]
        [DataRow(79.0, 35.001, 149.0, 34.999)]
        [DataRow(149.164237426758, -35.3622621765137, 149.166237426758, -35.3642621765137)]
        public void GetMapBoundsBothDimensions(double left, double top, double right, double bottom)
        {
            // 覆盖实际超高日志、东西跨度和普通静止日志；禁止测试访问网络。
            var previousMode = GMaps.Instance.Mode;
            GMaps.Instance.Mode = AccessMode.CacheOnly;
            try
            {
                var method = typeof(MissionPlanner.Log.LogMap).GetMethod("GetMap",
                    BindingFlags.Static | BindingFlags.NonPublic);
                var map = method.Invoke(null, new object[] { RectLatLng.FromLTRB(left, top, right, bottom) });
                using ((IDisposable)map)
                {
                    int width = (int)map.GetType().GetProperty("Width").GetValue(map);
                    int height = (int)map.GetType().GetProperty("Height").GetValue(map);
                    // 原有内容尺寸上限 2000，加两侧各 10 像素边距。
                    Assert.IsTrue(width > 0 && width <= 2020, "缩略图宽度超出范围: " + width);
                    Assert.IsTrue(height > 0 && height <= 2020, "缩略图高度超出范围: " + height);
                }
            }
            finally
            {
                GMaps.Instance.Mode = previousMode;
            }
        }
    }
}
