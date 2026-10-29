using log4net;
using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;
using MissionPlanner.Controls.BackstageView;
using MissionPlanner.GCSViews.ConfigurationView;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews
{
    public partial class SoftwareConfig : MyUserControl, IActivate
    {
        internal static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static string lastpagename = "";
        [Flags]
        public enum pageOptions
        {
            none = 0,
            isConnected = 1,
            isDisConnected = 2,
            isTracker = 4,
            isCopter = 8,
            isCopter35plus = 16,
            isHeli = 32,
            isQuadPlane = 64,
            isPlane = 128,
            isRover = 256,
            gotAllParams = 512
        }

        public class pluginPage
        {
            public Type page;
            public string headerText;
            public pageOptions options;

            public pluginPage(Type page, string headerText, pageOptions options)
            {
                this.page = page;
                this.headerText = headerText;
                this.options = options;
            }
        }


        private static List<pluginPage> pluginViewPages = new List<pluginPage>();

        public static void AddPluginViewPage(Type page, string headerText, pageOptions options = pageOptions.none)
        {
            pluginViewPages.Add(new pluginPage(page, headerText, options));
        }
        public bool isConnected
        {
            get { return MainV2.comPort.BaseStream.IsOpen; }
        }
        public bool isTracker
        {
            get { return isConnected && MainV2.comPort.MAV.cs.firmware == Firmwares.ArduTracker; }
        }

        public bool isCopter
        {
            get { return isConnected && MainV2.comPort.MAV.cs.firmware == Firmwares.ArduCopter2; }
        }

        public bool isCopter35plus
        {
            get { return MainV2.comPort.MAV.cs.version >= Version.Parse("3.5"); }
        }

        public bool isHeli
        {
            get { return isConnected && MainV2.comPort.MAV.aptype == MAVLink.MAV_TYPE.HELICOPTER; }
        }

        public bool isQuadPlane
        {
            get
            {
                return isConnected && isPlane &&
                       MainV2.comPort.MAV.param.ContainsKey("Q_ENABLE") &&
                       (MainV2.comPort.MAV.param["Q_ENABLE"].Value == 1.0);
            }
        }

        public bool isPlane
        {
            get
            {
                return isConnected &&
                       (MainV2.comPort.MAV.cs.firmware == Firmwares.ArduPlane ||
                        MainV2.comPort.MAV.cs.firmware == Firmwares.Ateryx);
            }
        }

        public bool isRover
        {
            get { return isConnected && MainV2.comPort.MAV.cs.firmware == Firmwares.ArduRover; }
        }


        public bool gotAllParams
        {
            get
            {
                log.InfoFormat("TotalReceived {0} TotalReported {1}", MainV2.comPort.MAV.param.TotalReceived,
                    MainV2.comPort.MAV.param.TotalReported);
                if (MainV2.comPort.MAV.param.TotalReceived < MainV2.comPort.MAV.param.TotalReported)
                {
                    return false;
                }

                return true;
            }
        }
        public SoftwareConfig()
        {
            InitializeComponent();
        }

        public void Activate()
        {
        }

        public BackstageViewPage AddBackstageViewPage(Type userControl, string headerText,
            BackstageViewPage Parent = null, bool advanced = false)
        {
            try
            {
                return backstageView.AddPage(userControl, headerText, Parent, advanced);
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return null;
            }
        }

        private void SoftwareConfig_Load(object sender, EventArgs e)
        {
            try
            {
                BackstageViewPage start = null;

                // 本项目只提供已确认的配置页；参数未齐时继续显示原加载页。
                if (MainV2.comPort.BaseStream.IsOpen && gotAllParams)
                {
                    if (MainV2.comPort.MAV.cs.firmware == Firmwares.ArduCopter2)
                        start = AddBackstageViewPage(typeof(ConfigArducopter), Strings.ExtendedTuning);
                    else if (MainV2.comPort.MAV.cs.firmware == Firmwares.ArduPlane)
                        start = AddBackstageViewPage(typeof(ConfigArducopter), "QP " + Strings.ExtendedTuning);

                    if ((MainV2.comPort.MAV.cs.capabilities & (int)MAVLink.MAV_PROTOCOL_CAPABILITY.FTP) > 0)
                        AddBackstageViewPage(typeof(MavFTPUI), Strings.MAVFtp);
                    AddBackstageViewPage(typeof(ConfigUserDefined), Strings.User_Params);
                }
                if (!MainV2.comPort.BaseStream.IsOpen || gotAllParams)
                    AddBackstageViewPage(typeof(ConfigRawParams), Strings.FullParameterList, null, false);
                else
                    start = AddBackstageViewPage(typeof(ConfigParamLoading), Strings.Loading);

                var planner = AddBackstageViewPage(typeof(ConfigPlanner), Strings.Planner);
                if (start == null)
                    start = planner;

                // apply theme before trying to display it
                ThemeManager.ApplyThemeTo(this);

                // remeber last page accessed
                foreach (BackstageViewPage page in backstageView.Pages)
                {
                    if (page.LinkText == lastpagename)
                    {
                        backstageView.ActivatePage(page);
                        break;
                    }
                }


                if (backstageView.SelectedPage == null && start != null)
                    this.BeginInvoke((Action) delegate
                    {
                        try
                        {
                            backstageView.ActivatePage(start);
                        }
                        catch (Exception ex)
                        {
                            log.Error(ex);
                        }
                    });
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private void SoftwareConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (backstageView.SelectedPage != null)
                lastpagename = backstageView.SelectedPage.LinkText;

            backstageView.Close();
        }
    }
}