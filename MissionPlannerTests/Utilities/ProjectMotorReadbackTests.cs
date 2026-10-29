using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;
using MissionPlanner.GCSViews.ConfigurationView;
using System;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MissionPlannerTests.Utilities
{
    [TestClass]
    public class ProjectMotorReadbackTests
    {
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void EarlierStartReplyCannotConfirmRestore(bool floatCapability)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var page = new ConfigProjectMotor())
                    using (var port = new MAVLinkInterface())
                    {
                        var handle = page.Handle;
                        var mav = port.MAVlist[1, 1];
                        mav.apname = floatCapability ? MAVLink.MAV_AUTOPILOT.GENERIC : MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA;
                        mav.cs.capabilities = floatCapability ? (uint)MAVLink.MAV_PROTOCOL_CAPABILITY.PARAM_FLOAT : 0;
                        Set(page, "port", port); Set(page, "sysid", (byte)1); Set(page, "compid", (byte)1);
                        Set(page, "active", true); Set(page, "awaitingReadback", true); Set(page, "requestedValue", (int?)2000);
                        Receive(page, 1300);
                        Assert.IsTrue((bool)Get(page, "awaitingReadback"), "较早启动回包不能确认恢复。");
                        StringAssert.Contains(((Label)Get(page, "status")).Text, "等待确认 2000");
                        Receive(page, 2000);
                        Assert.IsFalse((bool)Get(page, "awaitingReadback"));
                        StringAssert.Contains(((Label)Get(page, "status")).Text, "飞控参数已回读：2000");
                    }
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)));
            if (error != null) throw error;
        }

        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static void Receive(ConfigProjectMotor page, float value)
        {
            var id = new byte[16];
            Encoding.ASCII.GetBytes("SERVO5_TRIM").CopyTo(id, 0);
            var message = new MAVLink.mavlink_param_value_t { param_id = id, param_value = value, param_type = (byte)MAVLink.MAV_PARAM_TYPE.INT16 };
            var packet = new MAVLink.MavlinkParse().GenerateMAVLinkPacket10(MAVLink.MAVLINK_MSG_ID.PARAM_VALUE, message, 1, 1);
            page.GetType().GetMethod("ReceiveParameter", Flags).Invoke(page, new object[] { null, new MAVLink.MAVLinkMessage(packet) });
            Application.DoEvents();
        }
    }
}