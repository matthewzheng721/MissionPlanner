using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;

namespace MissionPlannerTests.Utilities
{
    [TestClass]
    public class ProjectPreflightTests
    {
        [TestMethod]
        public void BothGroupsNeedExplicitConfirmationAndSkipDoesNotPass()
        {
            var checks = new ProjectPreflightChecklist();
            checks.ObserveConnection(new object(), 1, 1, true);
            checks.SetPolarity(false, true); checks.SetPolarity(true, true);
            for (var i = 0; i < 5; i++) checks.Set(i, ProjectCheckResult.Confirmed);
            Assert.IsFalse(checks.CanArm);
            checks.Set(5, ProjectCheckResult.Confirmed); checks.Set(6, ProjectCheckResult.Confirmed);
            checks.Set(7, ProjectCheckResult.Skipped); Assert.IsFalse(checks.CanArm);
            checks.Set(7, ProjectCheckResult.Confirmed); Assert.IsTrue(checks.CanArm);
            checks.Set(2, ProjectCheckResult.Unchecked); Assert.IsFalse(checks.CanArm);
        }
        [TestMethod]
        public void DisconnectOrVehicleChangeClearsPreviousChecks()
        {
            var checks = new ProjectPreflightChecklist(); var connection = new object();
            checks.ObserveConnection(connection, 1, 1, true);
            checks.SetPolarity(false, true); checks.SetPolarity(true, true);
            for (var i = 0; i < 8; i++) checks.Set(i, ProjectCheckResult.Confirmed);
            checks.ObserveConnection(connection, 1, 1, true); Assert.IsTrue(checks.CanArm);
            checks.ObserveConnection(connection, 2, 1, true); Assert.IsFalse(checks.CanArm);
            Assert.AreEqual(ProjectCheckResult.Unchecked, checks[0]);
            checks.ObserveConnection(connection, 2, 1, false);
            checks.Set(0, ProjectCheckResult.Confirmed); Assert.AreEqual(ProjectCheckResult.Unchecked, checks[0]);
        }
    }
}
