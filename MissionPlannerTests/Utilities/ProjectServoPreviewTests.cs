using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;

namespace MissionPlannerTests.Utilities
{
    [TestClass]
    public class ProjectServoPreviewTests
    {
        private static Dictionary<string, double> Settings(int mode = 0)
        {
            var p = new Dictionary<string, double> { ["SDT_MIN"] = 1000, ["SDT_MAX"] = 2000, ["SDT_PERIOD"] = .4, ["SDT_DURATION"] = 10, ["SDT_MODE"] = mode, ["SDT_STEP"] = 300, ["SDT_FRAC"] = .7 };
            for (var i = 1; i <= 4; i++) { p["SDT_S" + i + "_MIN"] = 0; p["SDT_S" + i + "_MAX"] = 0; p["SERVO" + i + "_MIN"] = 1000; p["SERVO" + i + "_TRIM"] = 1500; p["SERVO" + i + "_MAX"] = 2000; }
            return p;
        }
        [TestMethod]
        public void PhysicalReversalAndDemandAreDistinct()
        {
            Assert.AreEqual(-.2, ProjectSurfaceMotion.FromPwm(0, 1600), .00001); Assert.AreEqual(-.2, ProjectSurfaceMotion.FromPwm(1, 1600), .00001);
            Assert.AreEqual(.2, ProjectSurfaceMotion.FromPwm(2, 1600), .00001); Assert.AreEqual(.2, ProjectSurfaceMotion.FromPwm(3, 1600), .00001);
            CollectionAssert.AreEqual(new[] { .5, .5, .5, .5 }, ProjectSurfaceMotion.ForDemand(.5, 0));
            CollectionAssert.AreEqual(new[] { -.5, 0d, 0d, .5 }, ProjectSurfaceMotion.ForDemand(0, .5));
            CollectionAssert.AreEqual(new[] { -.5, -.5, -.5, -.5 }, ProjectSurfaceMotion.ForDemand(-.5, 0));
        }
        [TestMethod]
        public void FeedbackCorrectionUsesReportedTargetIncludingPitchTrim()
        {
            CollectionAssert.AreEqual(new[] { -.04, -.04, -.04, -.04 }, ProjectSurfaceMotion.ForAttitude(-2, 0, -1, 0));
            CollectionAssert.AreEqual(new[] { -.4, -.4, -.4, -.4 }, ProjectSurfaceMotion.ForAttitude(0, 0, 10, 0));
            CollectionAssert.AreEqual(new[] { .4, .4, .4, .4 }, ProjectSurfaceMotion.ForAttitude(0, 0, -10, 0));
            CollectionAssert.AreEqual(new[] { 1d, 0d, 0d, -1d }, ProjectSurfaceMotion.ForAttitude(0, 0, 0, 35));
            CollectionAssert.AreEqual(new[] { -1d, 0d, 0d, 1d }, ProjectSurfaceMotion.ForAttitude(0, 0, 0, -35));
        }
        [TestMethod]
        public void ThreeWaveformsFollowLuaTimingAndClipPerChannel()
        {
            var p = Settings(); p["SDT_S1_MAX"] = 1700; var wave = ProjectServoPreview.Create(p);
            Assert.AreEqual(2000, wave.Requested(0)); Assert.AreEqual(1000, wave.Requested(.2)); Assert.AreEqual(2000, wave.Requested(.4));
            CollectionAssert.AreEqual(new[] { 1700, 1850, 1850, 1850 }, wave.At(0));
            wave = ProjectServoPreview.Create(Settings(1));
            Assert.AreEqual(1000, wave.Requested(0)); Assert.AreEqual(1300, wave.Requested(.4)); Assert.AreEqual(2000, wave.Requested(1.6)); Assert.AreEqual(1000, wave.Requested(2));
            wave = ProjectServoPreview.Create(Settings(2));
            Assert.AreEqual(1000, wave.Requested(0)); Assert.AreEqual(1500, wave.Requested(.1)); Assert.AreEqual(2000, wave.Requested(.2)); Assert.AreEqual(1000, wave.Requested(.4));
        }
        [TestMethod]
        public void CompletionHoldsIndividualTrimThenReleases()
        {
            var p = Settings(); p["SERVO2_TRIM"] = 1470; var wave = ProjectServoPreview.Create(p);
            CollectionAssert.AreEqual(new[] { 1500, 1470, 1500, 1500 }, wave.At(10));
            Assert.IsNotNull(wave.At(10.99)); Assert.IsNull(wave.At(11));
        }
        [TestMethod]
        public void InvalidCalibrationAndZeroRoundedTravelDoNotPreview()
        {
            var p = Settings(); p["SDT_S1_MIN"] = 1600;
            Assert.ThrowsException<InvalidOperationException>(() => ProjectServoPreview.Create(p));
            p = Settings(); p["SERVO1_MIN"] = 1499; p["SERVO1_MAX"] = 1501; p["SDT_FRAC"] = .1;
            Assert.ThrowsException<InvalidOperationException>(() => ProjectServoPreview.Create(p));
        }
        [TestMethod]
        public void RcReversalAndAsymmetricTrimMapSemanticDemand()
        {
            Assert.AreEqual(1549, ProjectSurfaceMotion.RcTarget(1000, 1510, 1900, false, 1, .1));
            Assert.AreEqual(1471, ProjectSurfaceMotion.RcTarget(1000, 1510, 1900, true, 1, .1));
            Assert.ThrowsException<InvalidOperationException>(() => ProjectSurfaceMotion.RcTarget(1000, 1510, 1900, false, 1, 0));
        }
        [TestMethod]
        public void ParentCheckboxCannotBypassTwoPolarityChecks()
        {
            var c = new ProjectPreflightChecklist(); var connection = new object(); c.ObserveConnection(connection, 1, 1, true);
            c.Set(4, ProjectCheckResult.Confirmed); Assert.AreEqual(ProjectCheckResult.Unchecked, c[4]);
            c.SetPolarity(false, true); Assert.AreEqual(ProjectCheckResult.Unchecked, c[4]);
            c.SetPolarity(true, true); Assert.AreEqual(ProjectCheckResult.Confirmed, c[4]);
            c.SetPolarity(false, false); Assert.AreEqual(ProjectCheckResult.Unchecked, c[4]);
            c.SetPolarity(false, true); c.Set(4, ProjectCheckResult.Skipped); Assert.IsFalse(c.FbwaPolarityConfirmed); Assert.IsFalse(c.ManualPolarityConfirmed);
            c.SetPolarity(true, true); c.ObserveConnection(connection, 1, 1, false); Assert.IsFalse(c.ManualPolarityConfirmed);
        }
    }
}
