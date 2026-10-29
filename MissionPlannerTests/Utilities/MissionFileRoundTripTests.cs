using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace MissionPlannerTests.Utilities
{
    [TestClass]
    public class MissionFileRoundTripTests
    {
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MissionFilePreservesHomeCommandsAndFrames(bool legacyWithoutType)
        {
            var points = new List<Locationwp>
            {
                new Locationwp { id = 16, frame = 0, lat = 35.1, lng = 110.2, alt = 584 },
                new Locationwp { id = 16, frame = 3, lat = 35.101, lng = 110.201, alt = 93, p1 = 2 },
                new Locationwp { id = 16, frame = 0, lat = 35.102, lng = 110.202, alt = 690 },
                new Locationwp { id = 178, frame = 3, p1 = 0, p2 = 22, p3 = -1 }
            };
            var format = MissionFile.ConvertFromLocationwps(points);
            foreach (var item in format.mission.items)
            {
                Assert.AreEqual("SimpleItem", item.type);
                if (legacyWithoutType) item.type = null;
            }
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mission");
            try
            {
                MissionFile.WriteFile(path, format);
                var loaded = MissionFile.ConvertToLocationwps(MissionFile.ReadFile(path));
                Assert.AreEqual(points.Count, loaded.Count);
                for (int i = 0; i < points.Count; i++)
                {
                    Assert.AreEqual(points[i].id, loaded[i].id);
                    Assert.AreEqual(points[i].frame, loaded[i].frame);
                    Assert.AreEqual(points[i].lat, loaded[i].lat);
                    Assert.AreEqual(points[i].lng, loaded[i].lng);
                    Assert.AreEqual(points[i].alt, loaded[i].alt);
                    Assert.AreEqual(points[i].p1, loaded[i].p1);
                    Assert.AreEqual(points[i].p2, loaded[i].p2);
                    Assert.AreEqual(points[i].p3, loaded[i].p3);
                    Assert.AreEqual(points[i].p4, loaded[i].p4);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
