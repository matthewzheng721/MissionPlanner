using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Controls;
using MissionPlanner.GCSViews.ConfigurationView;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews
{
    public partial class FlightData
    {
        private bool projectWorkspaceReady;
        private SplitContainer projectOperationSplit;
        private TabControl projectOperationTabs;
        private TabPage projectParametersTab;
        private TabPage projectMissionTab;
        private ConfigRawParams projectParameters;
        private bool projectParametersActivated;
        private Label projectBatteryStatus;
        private Label projectLinkStatus;
        private Label projectGpsStatus;
        private Button projectEkfStatus;
        private Button projectVibeStatus;
        private Button projectArmButton;
        private bool projectPlannerAttached;
        private bool projectStatusWindowVisible;
        private bool projectCurveWindowVisible;
        private bool projectPreviousAutoPan;
        private Panel projectMessagePanel;
        private Button projectMessageToggle;
        private ProjectMonitoringWindows projectMonitoring;
        private ProjectOnboardCalibrationWindows projectCalibration;
        private ConfigProjectMotor projectMotor;
        private object projectMotorVehicle;
        private bool projectMotorParametersReady;
        private Button projectEngineStartButton;
        private Button projectEngineStopButton;
        private Button projectEngineRestoreButton;
        private Form projectScriptWindow;
        private Form projectHudWindow;
        private HUD projectHudCopy;
        private bool projectLayoutRestored;
        private bool projectLayoutChanging;

        public void RefreshProjectWorkspaceParameters()
        {
            projectParameters?.RefreshProjectWorkspaceIfClean();
        }

        private void SaveProjectWorkspaceLayout()
        {
            if (!projectLayoutRestored || projectLayoutChanging) return;
            Settings.Instance["FlightSplitter"] = MainH.SplitterDistance.ToString();
            Settings.Instance["ProjectHudHeight"] = SubMainLeft.SplitterDistance.ToString();
            if (!projectOperationSplit.Panel2Collapsed)
                Settings.Instance["ProjectOperationsWidth"] = projectOperationSplit.Panel2.Width.ToString();
            Settings.Instance["ProjectOperationsCollapsed"] = projectOperationSplit.Panel2Collapsed.ToString();
        }

        private void RestoreProjectWorkspaceLayout()
        {
            projectLayoutChanging = true;
            try
            {
                MainH.SplitterDistance = Math.Max(MainH.Panel1MinSize, Math.Min(Math.Min(Settings.Instance.GetInt32("FlightSplitter", Math.Min(340, (int)(Width * .22))), Width < 1500 ? (int)(Width * .24) : 480), MainH.Width - 580));
                SubMainLeft.SplitterDistance = Math.Max(SubMainLeft.Panel1MinSize,
                    Math.Min(Settings.Instance.GetInt32("ProjectHudHeight", Height >= 850 ? 290 : 245), SubMainLeft.Height - SubMainLeft.Panel2MinSize - SubMainLeft.SplitterWidth));
                projectOperationSplit.Panel2Collapsed = false;
                projectOperationSplit.SplitterDistance = Math.Max(projectOperationSplit.Panel1MinSize,
                    Math.Min(projectOperationSplit.Width - Math.Min(Settings.Instance.GetInt32("ProjectOperationsWidth", Math.Max(330, Math.Min(400, (int)(Width * .22)))), Width < 1500 ? (int)(Width * .27) : 540) - projectOperationSplit.SplitterWidth,
                        projectOperationSplit.Width - projectOperationSplit.Panel2MinSize - projectOperationSplit.SplitterWidth));
                projectOperationSplit.Panel2Collapsed = Settings.Instance.GetBoolean("ProjectOperationsCollapsed", false);
                projectLayoutRestored = true;
            }
            finally { projectLayoutChanging = false; }
        }

        public Rectangle GetProjectOperationScreenBounds()
        {
            return MainH.Panel2.RectangleToScreen(MainH.Panel2.ClientRectangle);
        }

        private void ShowProjectHudWindow()
        {
            if (projectHudWindow != null && !projectHudWindow.IsDisposed)
            {
                projectHudWindow.Activate();
                return;
            }
            projectHudCopy = new HUD { Dock = DockStyle.Fill, BackColor = hud1.BackColor };
            // 复制原 HUD 自身的显示选项，并复用同一真实遥测绑定源。
            foreach (var property in typeof(HUD).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                if (property.DeclaringType != typeof(HUD) || property.GetSetMethod() == null || property.GetIndexParameters().Length != 0) continue;
                var type = property.PropertyType;
                if (type.IsValueType || type == typeof(string)) property.SetValue(projectHudCopy, property.GetValue(hud1));
            }
            projectHudCopy.displayconninfo = false;
            projectHudCopy.CustomItems = hud1.CustomItems;
            foreach (Binding binding in hud1.DataBindings)
                projectHudCopy.DataBindings.Add(new Binding(binding.PropertyName, binding.DataSource,
                    binding.BindingMemberInfo.BindingMember, binding.FormattingEnabled, DataSourceUpdateMode.Never));
            projectHudCopy.ekfclick += hud1_ekfclick;
            projectHudCopy.vibeclick += hud1_vibeclick;
            projectHudCopy.prearmclick += hud1_prearmclick;
            projectHudCopy.ContextMenuStrip = contextMenuStripHud;
            projectHudWindow = new Form { Text = "HUD 独立监视", Size = new Size(640, 510),
                StartPosition = FormStartPosition.Manual, Location = GetProjectOperationScreenBounds().Location };
            projectHudWindow.Controls.Add(projectHudCopy);
            projectHudWindow.FormClosed += (s, e) =>
            {
                projectHudCopy.DataBindings.Clear();
                projectHudCopy.ContextMenuStrip = null;
                projectHudCopy = null;
                projectHudWindow = null;
            };
            projectHudWindow.Show(FindForm());
        }

        private static Button ProjectButton(string name, string caption, EventHandler handler)
        {
            var button = new ProjectActionButton { Name = name, Text = caption, AutoSize = true,
                MinimumSize = new Size(82, 30), Margin = new Padding(4), UseVisualStyleBackColor = true };
            button.Click += handler;
            return button;
        }

        private void InitializeProjectWorkspace()
        {
            SuspendLayout();
            projectWorkspaceReady = true;
            MainH.Panel1MinSize = 260;
            MainH.FixedPanel = FixedPanel.Panel1;
            MainH.SplitterDistance = Math.Min(320, Math.Max(260, MainH.Width / 3));
            SubMainLeft.Panel2.ContextMenuStrip = null;
            SubMainLeft.Panel1MinSize = 160;
            SubMainLeft.Panel2MinSize = 180;
            SubMainLeft.FixedPanel = FixedPanel.Panel1;
            SubMainLeft.SplitterDistance = 245;

            // 原 HUD 保留飞行姿态仪，健康读数集中到 HUD 与 Quick 之间的状态带。
            hud1.batteryon = hud1.batteryon2 = hud1.displaygps = hud1.displayekf = hud1.displayvibe = false;
            hud1.displayconninfo = false; hud1.ProjectInstrumentStyle = true;
            tabControlactions.TabPages.Remove(tabQuick);
            var monitor = new TableLayoutPanel { Name = "ProjectMonitor", Dock = DockStyle.Fill,
                ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            monitor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            monitor.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            monitor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            monitor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var health = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            projectBatteryStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            projectLinkStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            projectGpsStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            health.Controls.Add(projectBatteryStatus); health.Controls.Add(projectLinkStatus); health.Controls.Add(projectGpsStatus);
            var flags = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            projectEkfStatus = ProjectButton("ProjectEKF", "EKF", hud1_ekfclick);
            projectVibeStatus = ProjectButton("ProjectVIBE", "VIBE", hud1_vibeclick);
            flags.Controls.AddRange(new Control[] { projectEkfStatus, projectVibeStatus });
            health.Controls.Add(flags);
            health.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            health.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            health.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            health.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            monitor.Controls.Add(health, 0, 0);
            tableLayoutPanelQuick.Dock = DockStyle.Fill;
            monitor.Controls.Add(tableLayoutPanelQuick, 0, 1);
            projectMessagePanel = new Panel { Name = "ProjectMessages", Dock = DockStyle.Fill, Height = 34, Margin = Padding.Empty };
            projectMessageToggle = ProjectButton("ProjectMessageToggle", "消息  ▸", (sender, args) =>
            {
                var expanded = !txt_messagebox.Visible;
                txt_messagebox.Visible = expanded;
                projectMessagePanel.Height = expanded ? 160 : 34;
                projectMessageToggle.Text = expanded ? "消息  ▾" : "消息  ▸";
                if (expanded) Messagetabtimer_Tick(null, EventArgs.Empty);
            });
            projectMessageToggle.Dock = DockStyle.Top; projectMessageToggle.Height = 32;
            txt_messagebox.Dock = DockStyle.Fill; txt_messagebox.Visible = false;
            projectMessagePanel.Controls.Add(txt_messagebox); projectMessagePanel.Controls.Add(projectMessageToggle);
            monitor.Controls.Add(projectMessagePanel, 0, 2);
            SubMainLeft.Panel2.Controls.Add(monitor); monitor.BringToFront();

            projectOperationSplit = new SplitContainer { Name = "ProjectOperationSplit", Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel2, Orientation = Orientation.Vertical, Size = new Size(900, 600) };
            projectOperationSplit.Panel1MinSize = 240;
            projectOperationSplit.Panel2MinSize = 320;
            projectOperationSplit.SplitterDistance = 530;
            MainH.Panel2.Controls.Add(projectOperationSplit);
            projectOperationSplit.Panel1.Controls.Add(tableMap);
            tableMap.Dock = DockStyle.Fill;
            projectOperationTabs = new ProjectPages { Name = "ProjectOperationTabs", Dock = DockStyle.Fill };
            projectOperationSplit.Panel2.Controls.Add(projectOperationTabs);

            var actionTab = new TabPage("动作") { Name = "ProjectActions" };
            projectMissionTab = new TabPage("航点") { Name = "ProjectMission" };
            projectParametersTab = new TabPage("参数") { Name = "ProjectParameters" };
            var toolsTab = new TabPage("工具") { Name = "ProjectTools" };
            projectOperationTabs.TabPages.AddRange(new[] { actionTab, projectMissionTab, projectParametersTab, toolsTab });

            // 动作和 AUX 直接排在同一页，不再藏入第二层标签。
            var actionRows = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
            actionRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            actionRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            var rows = new[] { new Control[] { CMB_action, BUTactiondo }, new Control[] { CMB_setwp, BUT_setwp },
                new Control[] { CMB_modes, BUT_setmode }, new Control[] { BUT_quickauto, BUT_ARM } };
            for (var i = 0; i < rows.Length; i++)
            {
                actionRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                for (var j = 0; j < 2; j++)
                {
                    var control = rows[i][j]; control.Dock = DockStyle.Fill; control.Margin = new Padding(4);
                    actionRows.Controls.Add(control, j, i);
                }
            }
            var auxLabel = new Label { Text = "AUX 辅助功能", AutoSize = true, Margin = new Padding(4, 12, 4, 6) };
            actionRows.Controls.Add(auxLabel, 0, 4); actionRows.SetColumnSpan(auxLabel, 2);
            flowLayoutPanel1.Dock = DockStyle.Fill; flowLayoutPanel1.FlowDirection = FlowDirection.TopDown;
            flowLayoutPanel1.WrapContents = false; flowLayoutPanel1.AutoScroll = true;
            actionRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            actionRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
            actionRows.Controls.Add(flowLayoutPanel1, 0, 5); actionRows.SetColumnSpan(flowLayoutPanel1, 2);
            flowLayoutPanel1.SizeChanged += (sender, args) =>
            {
                foreach (Control aux in flowLayoutPanel1.Controls) aux.Width = Math.Max(260, flowLayoutPanel1.ClientSize.Width - 24);
            };
            projectMotor = new ConfigProjectMotor { Dock = DockStyle.Fill };
            projectMotor.SizeChanged += (sender, args) => LayoutProjectMotor();
            // 底栏保留唯一发动机入口；动作页不再重复开机／恢复／停车按钮。
            projectMotor.Visible = false; actionTab.Controls.Add(projectMotor);
            actionTab.AutoScroll = true; actionTab.Controls.Add(actionRows);
            var statusHost = new TabControl { Dock = DockStyle.Fill };
            tabControlactions.TabPages.Remove(tabStatus); statusHost.TabPages.Add(tabStatus);
            projectMonitoring = new ProjectMonitoringWindows(this, statusHost, zg1,
                visible => { projectStatusWindowVisible = visible; if (visible) tabStatus.Invalidate(); },
                visible => { projectCurveWindowVisible = visible; CB_tuning.Checked = visible; });
            // 与 EKF／VIBE 同处左监视区，始终可见，不依赖打开右栏工具页。
            flags.Controls.Add(ProjectButton("ProjectMonitorEntry", "更多监视", (sender, args) => projectMonitoring.ShowMenu()));
            projectCalibration = new ProjectOnboardCalibrationWindows(this);
            Disposed += (sender, args) => { projectMonitoring.Dispose(); projectCalibration.Dispose(); };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
            tools.Controls.Add(new Label { Text = "机上校准", AutoSize = true });
            tools.Controls.Add(ProjectButton("ProjectLevelCalibration", "水平校准", (sender, args) => projectCalibration.ShowLevel()));
            tools.Controls.Add(ProjectButton("ProjectCompassCalibration", "大飞机罗盘校准", (sender, args) => projectCalibration.ShowCompass()));
            tools.Controls.Add(new Label { Text = "日志与脚本", AutoSize = true, Margin = new Padding(3, 20, 3, 8) });
            tools.Controls.Add(ProjectButton("ProjectLogDownload", "下载 BIN 日志", BUT_DFMavlink_Click));
            tools.Controls.Add(ProjectButton("ProjectLogBrowse", "查看 BIN / LOG", BUT_logbrowse_Click));
            tools.Controls.Add(ProjectButton("ProjectScripts", "脚本工具", (sender, args) => ShowProjectScripts()));
            InitializeProjectConfiguration(toolsTab, tools);
            tools.Controls.Add(new Label { Text = "诊断", AutoSize = true, Margin = new Padding(3, 20, 3, 8) });
            tools.Controls.Add(ProjectButton("ProjectMavlinkInspector", "MAVLink Inspector", (sender, args) => MainV2.instance.ShowProjectMavlinkInspector()));
            tools.Controls.Add(ProjectButton("ProjectMessageInterval", "Message Interval", (sender, args) => temp.ShowMessageInterval(FindForm())));
            // 剩余旧标签不再提供入口；自动日志记录不受 UI 移除影响。
            tabControlactions.Visible = false;
            CB_tuning.Checked = false; CB_tuning.Visible = false;
            splitContainer1.Panel1Collapsed = true;
            projectOperationTabs.SelectedIndexChanged += (sender, args) =>
            {
                ActivateProjectParameters(); UpdateProjectPlanningMode(); UpdateProjectConfigurationVisibility();
            };

            var footer = new FlowLayoutPanel { Name = "ProjectFooter", Dock = DockStyle.Bottom, Height = 68, WrapContents = true };
            footer.Controls.Add(ProjectButton("ProjectChecks", "平台 / 起飞检查", (s, e) => OpenProjectChecks()));
            projectEngineStartButton = ProjectButton("ProjectEngineStart", "一键开机", (s, e) => RunProjectEngine(motor => motor.StartEngine()));
            projectEngineStopButton = ProjectButton("ProjectEngineStop", "一键停车", (s, e) => RunProjectEngine(motor => motor.StopEngine()));
            projectEngineRestoreButton = ProjectButton("ProjectEngineRestore", "恢复", (s, e) => RunProjectEngine(motor => motor.Restore()));
            footer.Controls.AddRange(new Control[] { projectEngineStartButton, projectEngineRestoreButton, projectEngineStopButton });
            projectArmButton = ProjectButton("ProjectArm", "解锁", BUT_ARM_Click);
            footer.Controls.Add(projectArmButton);
            footer.Controls.Add(ProjectButton("ProjectAuto", "自动", BUT_quickauto_Click));
            footer.Controls.Add(ProjectButton("ProjectToggleOperations", "展开 / 收起右栏", (s, e) =>
            {
                projectOperationSplit.Panel2Collapsed = !projectOperationSplit.Panel2Collapsed;
                UpdateProjectPlanningMode();
                UpdateProjectConfigurationVisibility();
                SaveProjectWorkspaceLayout();
            }));
            var motorStatus = projectMotor.Controls.OfType<Label>().First(label => label.Name == "status");
            motorStatus.AutoSize = true; motorStatus.Margin = new Padding(8, 0, 4, 0);
            motorStatus.MinimumSize = new Size(Width - 30, 20);
            footer.SizeChanged += (sender, args) => motorStatus.MinimumSize = new Size(Math.Max(100, footer.ClientSize.Width - 30), 20);
            footer.Controls.Add(motorStatus);
            Controls.Add(footer);
            footer.SendToBack();
            MainH.SplitterMoved += (s, e) => SaveProjectWorkspaceLayout();
            SubMainLeft.SplitterMoved += (s, e) => SaveProjectWorkspaceLayout();
            projectOperationSplit.SplitterMoved += (s, e) => SaveProjectWorkspaceLayout();
            Load += (s, e) => BeginInvoke(new Action(() => { RestoreProjectWorkspaceLayout(); AttachProjectPlanner(); }));
            ProjectPreflight.Checklist.Changed += ProjectChecklistChanged;
            Disposed += (s, e) => ProjectPreflight.Checklist.Changed -= ProjectChecklistChanged;
            UpdateProjectWorkspaceStatus();
            InitializeProjectAppearance();
            ResumeLayout(true);
        }

        private void LayoutProjectMotor()
        {
            // 嵌入窄右栏时，仅调整原控件排版，不复制发动机发送逻辑。
            var buttons = projectMotor.Controls.OfType<Button>().ToArray();
            var width = Math.Max(70, (projectMotor.ClientSize.Width - 16) / 3);
            for (var i = 0; i < buttons.Length; i++)
            {
                buttons[i].Location = new Point(4 + i * (width + 4), 4);
                buttons[i].Size = new Size(width, 30);
            }
            var top = 44;
            foreach (var label in projectMotor.Controls.OfType<Label>().OrderBy(item => item.Top))
            {
                label.MaximumSize = new Size(Math.Max(80, projectMotor.ClientSize.Width - 8), 0);
                label.Location = new Point(4, top);
                top += Math.Max(28, label.PreferredHeight + 8);
            }
        }

        private void RunProjectEngine(Action<ConfigProjectMotor> operation)
        {
            projectOperationSplit.Panel2Collapsed = false;
            projectOperationTabs.SelectedIndex = 0;
            operation(projectMotor);
        }

        private void ProjectChecklistChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) this.BeginInvokeIfRequired(UpdateProjectArmAvailability);
        }

        private void OpenProjectChecks()
        {
            ProjectPreflight.Show(this);
            var wizard = Application.OpenForms.OfType<ProjectPreflightWizard>().FirstOrDefault();
            if (wizard != null) wizard.Location = new Point(Math.Max(Screen.FromControl(this).WorkingArea.Left, Math.Min(MainH.Panel2.PointToScreen(Point.Empty).X, Screen.FromControl(this).WorkingArea.Right - wizard.Width)), Math.Max(Screen.FromControl(this).WorkingArea.Top, Math.Min(MainH.Panel2.PointToScreen(Point.Empty).Y, Screen.FromControl(this).WorkingArea.Bottom - wizard.Height)));
        }

        private void UpdateProjectArmAvailability()
        {
            ProjectPreflight.RefreshConnection();
            var connected = MainV2.comPort.BaseStream.IsOpen && !MainV2.comPort.logreadmode;
            var enabled = connected && (MainV2.comPort.MAV.cs.armed || ProjectPreflight.Checklist.CanArm);
            BUT_ARM.Enabled = enabled;
            projectArmButton.Enabled = enabled;
            toolTip1.SetToolTip(projectArmButton, enabled ? "执行前需要确认" : "完成平台与起飞条件的全部人工核对后可解锁");
        }

        private void ActivateProjectParameters()
        {
            if (projectOperationTabs.SelectedTab != projectParametersTab) return;
            if (projectParameters == null)
            {
                projectParameters = new ConfigRawParams { Dock = DockStyle.Fill };
                projectParameters.ConfigureProjectWorkspace();
                projectParametersTab.Controls.Add(projectParameters);
                ThemeManager.ApplyThemeTo(projectParameters); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(projectParameters);
            }
            if (!projectParametersActivated)
            {
                projectParametersActivated = true;
                projectParameters.ObserveProjectWorkspaceConnection();
            }
        }

        public void SelectProjectParameters()
        {
            projectOperationSplit.Panel2Collapsed = false;
            projectOperationTabs.SelectedTab = projectParametersTab;
            ActivateProjectParameters();
        }

        public void SelectProjectMission()
        {
            projectOperationSplit.Panel2Collapsed = false;
            projectOperationTabs.SelectedTab = projectMissionTab;
            AttachProjectPlanner();
            UpdateProjectPlanningMode();
        }

        private void ShowProjectScripts()
        {
            if (projectScriptWindow != null && !projectScriptWindow.IsDisposed) { projectScriptWindow.Activate(); return; }
            var host = new TabControl { Dock = DockStyle.Fill };
            host.TabPages.Add(tabScripts);
            projectScriptWindow = new Form { Text = "脚本工具", Size = new Size(720, 420), StartPosition = FormStartPosition.CenterParent };
            projectScriptWindow.Controls.Add(host);
            projectScriptWindow.FormClosing += (sender, args) =>
            {
                // 归还原页面及脚本状态，关闭外窗不销毁可重开的原控件。
                host.TabPages.Remove(tabScripts);
                if (!tabControlactions.IsDisposed) tabControlactions.TabPages.Add(tabScripts);
            };
            ThemeManager.ApplyThemeTo(projectScriptWindow); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(projectScriptWindow);
            projectScriptWindow.Show(FindForm());
        }

        private void AttachProjectPlanner()
        {
            if (projectPlannerAttached || MainV2.instance?.FlightPlanner == null) return;
            var planner = MainV2.instance.FlightPlanner;
            // 停用原飞行地图输入（包括飞至／引导），共享地图只有规划输入处理。
            gMapControl1.ContextMenuStrip = null;
            gMapControl1.MouseDown -= gMapControl1_MouseDown;
            gMapControl1.MouseMove -= gMapControl1_MouseMove;
            gMapControl1.MouseUp -= gMapControl1_MouseUp;
            gMapControl1.MouseLeave -= gMapControl1_MouseLeave;
            gMapControl1.OnMarkerEnter -= gMapControl1_OnMarkerEnter;
            gMapControl1.OnMarkerLeave -= gMapControl1_OnMarkerLeave;
            planner.AttachProjectWorkspace(gMapControl1);
            planner.Dock = DockStyle.Fill; projectMissionTab.Controls.Add(planner);
            ThemeManager.ApplyThemeTo(planner); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(planner); planner.Show();
            projectPlannerAttached = true;
            InitializeProjectMapWorkspace(planner);
            UpdateProjectPlanningMode();
        }

        private void UpdateProjectPlanningMode()
        {
            if (!projectPlannerAttached) return;
            var planner = MainV2.instance.FlightPlanner;
            var active = Visible && !projectOperationSplit.Panel2Collapsed && projectOperationTabs.SelectedTab == projectMissionTab;
            if (active && !planner.ProjectPlanningActive) { projectPreviousAutoPan = CHK_autopan.Checked; CHK_autopan.Checked = false; }
            if (!active && planner.ProjectPlanningActive) CHK_autopan.Checked = projectPreviousAutoPan;
            planner.SetProjectPlanningActive(active);
        }

        private static string ProjectGpsFix(float fix)
        {
            // 定位类型按原 HUD 的同一 MAVLink 数值映射显示。
            switch ((int)fix)
            {
                case 0: return "无 GPS";
                case 1: return "未定位";
                case 2: return "2D";
                case 3: return "3D";
                case 4: return "DGPS";
                case 5: return "RTK Float";
                case 6: return "RTK Fixed";
                default: return fix.ToString();
            }
        }

        private void UpdateProjectWorkspaceStatus()
        {
            if (!projectWorkspaceReady) return;
            ObserveProjectConfiguration();
            if (projectMessagePanel != null && txt_messagebox.Visible) Messagetabtimer_Tick(null, EventArgs.Empty);
            var state = MainV2.comPort.MAV.cs;
            if (projectMapNotice != null)
            {
                var missing = !string.IsNullOrEmpty(gMapControl1.MissingTileStatus);
                // 原地图只报告存在缺失瓦片，不能把局部缺图说成整个区域没有地图。
                projectMapNotice.Text = !missing ? "" : GMap.NET.GMaps.Instance.Mode == GMap.NET.AccessMode.CacheOnly
                    ? "地图缓存不完整 · 当前视图有缺失瓦片" : "地图瓦片未完全加载，请检查地图服务与连接";
                projectMapNotice.Visible = missing;
            }
            if (projectHudCopy != null)
            {
                projectHudCopy.bgimage = hud1.bgimage;
                projectHudCopy.CustomItems = hud1.CustomItems;
                projectHudCopy.Invalidate();
            }
            var connected = MainV2.comPort.BaseStream.IsOpen || MainV2.comPort.logreadmode;
            MainV2.instance?.UpdateProjectFlightSummary(connected, state.mode ?? "", state.armed);
            if (!connected) { hud1.connected = false; if (projectHudCopy != null) projectHudCopy.connected = false; }
            foreach (var quick in tableLayoutPanelQuick.Controls.OfType<QuickView>())
            {
                if (quick.DataUnavailable != !connected) { quick.DataUnavailable = !connected; quick.Invalidate(); }
            }
            projectBatteryStatus.Text = connected ? string.Format("{0:0.00} V", state.battery_voltage) : "—";
            if (projectBattery2Status != null) projectBattery2Status.Text = connected ? string.Format("{0:0.00} V", state.battery_voltage2) : "—";
            if (projectModeFeedback != null) { projectModeFeedback.Text = connected ? state.mode : "等待连接"; projectArmedFeedback.Text = connected ? (state.armed ? "已解锁" : "已锁定") : "断开连接"; }
            projectLinkStatus.Text = connected ? state.linkqualitygcs + "%" : "未连接";
            projectGpsStatus.Text = connected ? string.Format("{0} · {1}", ProjectGpsFix(state.gpsstatus), state.satcount) : "—";
            // 与原 HUD 相同的阈值和颜色，点击仍打开原 EKF/VIBE 详情。
            projectEkfStatus.BackColor = !connected ? ProjectWorkspaceStyle.ControlColor : state.ekfstatus > 0.8 ? Color.Red : state.ekfstatus > 0.5 ? Color.Orange : ProjectWorkspaceStyle.ControlColor;
            var vibration = Math.Max(hud1.vibex, Math.Max(hud1.vibey, hud1.vibez));
            projectVibeStatus.BackColor = !connected ? ProjectWorkspaceStyle.ControlColor : vibration > 60 ? Color.Red : vibration > 30 ? Color.Orange : ProjectWorkspaceStyle.ControlColor;
            UpdateProjectMonitorAppearance(connected);
            UpdateProjectMapAppearance(connected);
            var motorTarget = MainV2.comPort.BaseStream.IsOpen && !MainV2.comPort.logreadmode ? MainV2.comPort.MAV : null;
            var motorParametersReady = motorTarget != null && MainV2.comPort.MAV.param.ContainsKey("SERVO5_TRIM");
            if (!ReferenceEquals(projectMotorVehicle, motorTarget) || projectMotorParametersReady != motorParametersReady)
            {
                projectMotor.Deactivate();
                projectMotorVehicle = motorTarget;
                projectMotorParametersReady = motorParametersReady;
                if (motorTarget != null) projectMotor.Activate();
            }
            projectMotor.Enabled = motorTarget != null;
            projectEngineStartButton.Enabled = motorTarget != null;
            projectEngineStopButton.Enabled = motorTarget != null;
            projectEngineRestoreButton.Enabled = motorTarget != null;
            projectArmButton.Text = state.armed ? "锁定" : "解锁";
            UpdateProjectArmAvailability();
        }
    }
}


