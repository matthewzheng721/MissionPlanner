using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.Controls;
using MissionPlanner.GCSViews.ConfigurationView;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews
{
    public partial class FlightData
    {
        private TabPage projectToolsTab, projectSettingsTab;
        private Panel projectToolContent;
        private Control projectToolMenu;
        private ConfigPlanner projectSettings;
        private ConfigSerialInjectGPS projectRtk;
        private MavFTPUI projectFtp;
        private Label projectFtpMessage;
        private string projectToolSelection;
        private bool projectRtkActive, projectSettingsActive;
        private object projectFtpStream, projectFtpVehicle;
        private MAVLinkInterface projectFtpConnection;

        private void InitializeProjectConfiguration(TabPage toolsTab, Control menu)
        {
            projectToolsTab = toolsTab;
            projectToolMenu = menu;
            menu.AutoSize = false;
            ((FlowLayoutPanel)menu).AutoScroll = true;
            menu.Controls.Add(new Label { Text = "文件与连接", AutoSize = true, Margin = new Padding(3, 20, 3, 8) });
            menu.Controls.Add(ProjectButton("ProjectMavFtp", "MAVFTP 文件管理", (s, e) => ShowProjectTool("ftp")));
            menu.Controls.Add(ProjectButton("ProjectRtk", "RTK / GPS 注入", (s, e) => ShowProjectTool("rtk")));
            projectToolContent = new Panel { Dock = DockStyle.Fill, Visible = false };
            toolsTab.Controls.Add(projectToolContent);
            toolsTab.Controls.Add(menu);
            projectSettingsTab = new TabPage("设置") { Name = "ProjectSettings" };
            projectOperationTabs.TabPages.Add(projectSettingsTab);
            projectOperationTabs.Selecting += (s, e) =>
            {
                if (e.TabPage == projectSettingsTab && !MainV2.instance.AuthorizeProjectConfiguration()) e.Cancel = true;
            };
            Disposed += (s, e) =>
            {
                projectRtk?.Deactivate();
                projectFtp?.RetireProjectClient();
            };
        }

        public void SelectProjectTools()
        {
            projectOperationSplit.Panel2Collapsed = false;
            projectOperationTabs.SelectedTab = projectToolsTab;
        }

        public void SelectProjectSettings()
        {
            projectOperationSplit.Panel2Collapsed = false;
            projectOperationTabs.SelectedTab = projectSettingsTab;
            UpdateProjectConfigurationVisibility();
        }

        public ConfigSerialInjectGPS GetProjectRtk()
        {
            // 原 RTK 连接为静态后台服务；全工作台及命令行共享同一个 UI 实例。
            if (projectRtk == null)
            {
                projectRtk = new ConfigSerialInjectGPS();
                ThemeManager.ApplyThemeTo(projectRtk); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(projectRtk);
            }
            return projectRtk;
        }

        private void ShowProjectTool(string selection)
        {
            if (selection != null && !MainV2.instance.AuthorizeProjectConfiguration()) return;
            SelectProjectTools();
            projectToolSelection = selection;
            projectToolMenu.Visible = selection == null;
            projectToolContent.Visible = selection != null;
            projectToolContent.Controls.Clear();
            if (selection != null)
            {
                var back = ProjectButton("ProjectToolBack", "‹ 返回工具", (s, e) => ShowProjectTool(null));
                back.Dock = DockStyle.Top;
                var body = new Panel { Name = "ProjectToolBody", Dock = DockStyle.Fill, AutoScroll = true };
                projectToolContent.Controls.Add(body);
                projectToolContent.Controls.Add(back);
                if (selection == "rtk")
                {
                    var rtk = GetProjectRtk();
                    // 保留原基站设置、地图和诊断区域；窄栏可横向滚动，避免删减原功能。
                    rtk.Dock = DockStyle.None; rtk.Location = Point.Empty; rtk.Size = new Size(754, 594);
                    body.Controls.Add(rtk);
                }
                else
                {
                    projectFtpMessage = new Label { Dock = DockStyle.Fill, Padding = new Padding(12), Text = "连接并获取完整参数后，按飞控 FTP 能力显示文件管理。" };
                    body.Controls.Add(projectFtpMessage);
                    if (projectFtp != null) AttachProjectFtp(body);
                    ObserveProjectConfiguration();
                }
            }
            ThemeManager.ApplyThemeTo(projectToolContent); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(projectToolContent);
            UpdateProjectConfigurationVisibility();
        }

        private void AttachProjectFtp(Control body)
        {
            projectFtpMessage.Visible = false;
            projectFtp.Dock = DockStyle.Fill;
            body.Controls.Add(projectFtp);
            projectFtp.BringToFront(); ProjectWorkspaceStyle.Apply(projectFtp);
        }

        private void UpdateProjectConfigurationVisibility()
        {
            if (projectToolsTab == null) return;
            var available = Visible && !projectOperationSplit.Panel2Collapsed;
            var rtkActive = available && projectOperationTabs.SelectedTab == projectToolsTab && projectToolSelection == "rtk";
            if (rtkActive != projectRtkActive)
            {
                projectRtkActive = rtkActive;
                if (rtkActive) GetProjectRtk().Activate(); else projectRtk?.Deactivate();
            }
            var settingsActive = available && projectOperationTabs.SelectedTab == projectSettingsTab;
            if (settingsActive && !projectSettingsActive)
            {
                if (projectSettings == null)
                {
                    projectSettings = new ConfigPlanner { Dock = DockStyle.Fill };
                    projectSettings.ConfigureProjectWorkspace();
                    projectSettingsTab.Controls.Add(projectSettings);
                    ThemeManager.ApplyThemeTo(projectSettings); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(projectSettings);
                }
                projectSettings.Activate();
            }
            projectSettingsActive = settingsActive;
        }

        private void ObserveProjectConfiguration()
        {
            if (projectToolsTab == null) return;
            var connection = MainV2.comPort;
            var ready = connection.BaseStream.IsOpen && !connection.logreadmode &&
                connection.MAV.param.TotalReceived >= connection.MAV.param.TotalReported && connection.MAV.param.TotalReceived > 0 &&
                (connection.MAV.cs.capabilities & (int)MAVLink.MAV_PROTOCOL_CAPABILITY.FTP) != 0;
            if (projectFtp != null && (!ready || !ReferenceEquals(projectFtpConnection, connection) ||
                !ReferenceEquals(projectFtpStream, connection.BaseStream) || !ReferenceEquals(projectFtpVehicle, connection.MAV)))
            {
                var old = projectFtp; projectFtp = null;
                old.Parent?.Controls.Remove(old);
                old.RetireProjectClient();
                if (projectFtpMessage != null) projectFtpMessage.Visible = true;
            }
            if (ready && projectToolSelection == "ftp" && projectFtp == null)
            {
                projectFtpConnection = connection; projectFtpStream = connection.BaseStream; projectFtpVehicle = connection.MAV;
                projectFtp = new MavFTPUI(connection);
                projectFtp.ConfigureProjectWorkspace();
                AttachProjectFtp(projectFtpMessage.Parent);
            }
            UpdateProjectConfigurationVisibility();
        }
    }
}
