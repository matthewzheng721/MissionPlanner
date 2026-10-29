using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;
using MissionPlanner.Maps;
using GMap.NET.Internals;
using GMap.NET.MapProviders;
using GMap.NET.Projections;
using GMap.NET.WindowsForms;

namespace MissionPlannerTests.Utilities
{
    [TestClass]
    [DoNotParallelize]
    public class PlannerPrefetchTests
    {
        private sealed class LocalTileProvider : GMapProvider
        {
            private readonly Guid id = Guid.NewGuid();
            public int Requests;
            public bool Fail;
            public override Guid Id => id;
            public override string Name => "本地测试瓦片";
            public override PureProjection Projection => MercatorProjection.Instance;
            public override GMapProvider[] Overlays => new GMapProvider[] { this };
            public readonly byte[] Image;
            public LocalTileProvider()
            {
                Image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII=");
            }
            public override PureImage GetTileImage(GPoint pos, int zoom)
            {
                Interlocked.Increment(ref Requests);
                Thread.Sleep(100);
                return Fail ? null : TileImageProxy.FromArray(Image);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MemoryHitIsPersistedAndReadableInCacheOnlyMode(bool bing)
        {
            RunSta(() =>
            {
                GMapImageProxy.Enable();
                var maps = GMaps.Instance;
                var oldCache = maps.PrimaryCache;
                var oldMode = maps.Mode;
                var oldMemory = maps.UseMemoryCache;
                var oldIdle = maps.CacheOnIdleRead;
                var oldBoost = maps.BoostCacheEngine;
                try
                {
                    maps.PrimaryCache = new MyImageCache { CacheLocation = Path.Combine(Path.GetTempPath(), "MP-PlannerMapTests", Guid.NewGuid().ToString()) + Path.DirectorySeparatorChar };
                    maps.Mode = AccessMode.CacheOnly;
                    maps.UseMemoryCache = true;
                    GMapProvider provider = bing ? (GMapProvider)BingSatelliteMapProvider.Instance : GoogleSatelliteMapProvider.Instance;
                    var imageBytes = new LocalTileProvider().Image;
                    var area = new RectLatLng(10, 10, .01, .01);
                    var tiles = provider.Projection.GetAreaTileList(area, 4, 0);
                    // 只在测试中填充内部内存缓存，全程 CacheOnly，禁止访问真实瓦片服务器。
                    var rawTileType = typeof(GMaps).Assembly.GetType("GMap.NET.Internals.RawTile");
                    var add = maps.MemoryCache.GetType().GetMethod("AddTileToMemoryCache", BindingFlags.Instance | BindingFlags.NonPublic);
                    foreach (var tile in tiles)
                    {
                        var raw = Activator.CreateInstance(rawTileType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null, new object[] { provider.DbId, tile, 4 }, null);
                        add.Invoke(maps.MemoryCache, new object[] { raw, imageBytes });
                    }
                    using (var window = new TilePrefetcher())
                    {
                        var task = window.StartNonModal(area, 4, provider, 0, 0);
                        Assert.IsFalse(window.Modal);
                        Pump(task);
                        Assert.AreEqual(tiles.Count, window.CompletedTiles);
                        Assert.IsNull(window.DownloadError);
                    }
                    Assert.AreEqual(oldIdle, maps.CacheOnIdleRead);
                    Assert.AreEqual(oldBoost, maps.BoostCacheEngine);
                    maps.Mode = AccessMode.CacheOnly;
                    maps.UseMemoryCache = false;
                    foreach (var tile in tiles)
                    {
                        Exception error;
                        using (var image = maps.GetImageFrom(provider, tile, 4, out error))
                            Assert.IsNotNull(image, "脱离内存缓存后仍应能读取预下载瓦片。");
                        GMapProvider other = bing ? (GMapProvider)GoogleSatelliteMapProvider.Instance : BingSatelliteMapProvider.Instance;
                        using (var image = maps.GetImageFrom(other, tile, 4, out error))
                            Assert.IsNull(image, "相同坐标的不同影像缓存不能串用。");
                    }
                }
                finally
                {
                    maps.PrimaryCache = oldCache;
                    maps.Mode = oldMode;
                    maps.UseMemoryCache = oldMemory;
                    maps.CacheOnIdleRead = oldIdle;
                    maps.BoostCacheEngine = oldBoost;
                }
            });
        }

        [TestMethod]
        public void FailedTilesAreNotCountedAsDownloadedAndWindowCanCancel()
        {
            RunSta(() =>
            {
                GMapImageProxy.Enable();
                var provider = new LocalTileProvider { Fail = true };
                using (var window = new TilePrefetcher())
                {
                    var task = window.StartNonModal(new RectLatLng(10, 10, .01, .01), 4, provider, 0, 0);
                    Pump(task);
                    Assert.AreEqual(0, window.CompletedTiles);
                    Assert.IsTrue(window.TotalTiles > 0);
                }
                using (var window = new TilePrefetcher())
                {
                    var task = window.StartNonModal(new RectLatLng(10, 10, 1, 1), 10, provider, 0, 0);
                    window.Close();
                    Pump(task);
                    Assert.IsTrue(window.UserAborted);
                    Assert.IsTrue(window.IsDisposed);
                }
            });
        }

        private static void Pump(Task task)
        {
            var limit = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < limit)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Assert.IsTrue(task.IsCompleted, "地图工作窗口应能完成或取消。");
            task.GetAwaiter().GetResult();
        }

        private static void RunSta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(30000), "界面测试超时。");
            if (error != null) throw error;
        }
    }
}
