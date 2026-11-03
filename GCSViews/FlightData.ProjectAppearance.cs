using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Controls;

namespace MissionPlanner.GCSViews
{
    public partial class FlightData
    {
        private Label projectBattery2Status, projectModeFeedback, projectArmedFeedback;
        private Button[] projectNavigation;
        private Panel projectActionDock;
        private Label projectMapNotice;
        private Label projectOperationNotice;
        private Label projectControlFeedback;
        private readonly Font projectModeFont = new Font("Segoe UI", 23, FontStyle.Regular);

        private string projectQuickDistanceUnit, projectQuickAltitudeUnit, projectQuickSpeedUnit;

        // 常驻工作台不再依靠切回飞行数据页触发 Activate；单位变化只更新显示标签。
        private void RefreshProjectQuickUnits()
        {
            if (projectQuickDistanceUnit == CurrentState.DistanceUnit &&
                projectQuickAltitudeUnit == CurrentState.AltUnit &&
                projectQuickSpeedUnit == CurrentState.SpeedUnit) return;
            // 主工作台常驻后不再切回原 Activate；主/副 HUD 沿用原静态单位字符串。
            // 数值换算、46项原绑定和刷新调度保持原样，单位变化只触发一次重绘。
            foreach (var instrument in new[] { hud1, projectHudCopy })
            {
                if (instrument == null || instrument.IsDisposed) continue;
                instrument.altunit = CurrentState.AltUnit;
                instrument.speedunit = CurrentState.SpeedUnit;
                instrument.Invalidate();
            }
            projectQuickDistanceUnit = CurrentState.DistanceUnit;
            projectQuickAltitudeUnit = CurrentState.AltUnit;
            projectQuickSpeedUnit = CurrentState.SpeedUnit;
            foreach (var quick in tableLayoutPanelQuick.Controls.OfType<QuickView>())
            {
                var field = quick.DataBindings["number"]?.BindingMemberInfo.BindingField;
                if (string.IsNullOrEmpty(field)) continue;
                // Tag 保存原字段名，不能把已翻译并带单位的标题当成字段。
                quick.Tag = field;
                quick.desc = MainV2.comPort.MAV.cs.GetNameandUnit(field);
            }
        }

        private int ProjectPixels(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        private Control ProjectOperationRow(string caption, Control input, Button action)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = ProjectPixels(44), ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ProjectPixels(64)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ProjectPixels(76)));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var label = new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = ProjectWorkspaceStyle.Muted };
            input.Dock = DockStyle.Fill; input.Margin = new Padding(0, ProjectPixels(5), ProjectPixels(8), ProjectPixels(5));
            action.Dock = DockStyle.Fill; action.Margin = new Padding(0, ProjectPixels(4), 0, ProjectPixels(4));
            action.MinimumSize = Size.Empty; action.AutoSize = false;
            row.Controls.Add(label, 0, 0); row.Controls.Add(input, 1, 0); row.Controls.Add(action, 2, 0);
            return row;
        }

        private Control ProjectOperationCard(string title, string caption, params Control[] rows)
        {
            var card = new ProjectSectionPanel { Title = title, Caption = caption, ContentPadding = new Padding(12, 8, 12, 8), Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, ProjectPixels(8)) };
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var height = 0;
            for (var i = 0; i < rows.Length; i++)
            {
                height += rows[i].Height;
                body.RowStyles.Add(new RowStyle(SizeType.Absolute, rows[i].Height));
                rows[i].Dock = DockStyle.Fill; rows[i].Margin = Padding.Empty;
                body.Controls.Add(rows[i], 0, i);
            }
            card.Controls.Add(body);
            card.Height = height + card.Padding.Vertical;
            return card;
        }

        private void StyleProjectActionState()
        {
            if (projectModeFeedback == null) return;
            projectModeFeedback.Font = projectModeFont;
            projectModeFeedback.ForeColor = projectModeFeedback.Text == "等待连接" ? ProjectWorkspaceStyle.Muted : ProjectWorkspaceStyle.Text;
            projectArmedFeedback.ForeColor = projectArmedFeedback.Text == "已解锁" ? ProjectWorkspaceStyle.Warning : projectArmedFeedback.Text == "已锁定" ? ProjectWorkspaceStyle.Success : ProjectWorkspaceStyle.Muted;
            ProjectWorkspaceStyle.StyleAction(BUT_setmode, ProjectActionKind.Primary);
            ProjectWorkspaceStyle.StyleAction(BUTactiondo, ProjectActionKind.Primary);
            ProjectWorkspaceStyle.StyleAction(BUT_setwp, ProjectActionKind.Normal);
            ProjectWorkspaceStyle.StyleAction(BUT_ARM, projectArmedFeedback.Text == "已解锁" ? ProjectActionKind.Danger : ProjectActionKind.Normal);
            if (projectOperationNotice != null)
            {
                var live = MainV2.comPort.BaseStream.IsOpen && !MainV2.comPort.logreadmode;
                projectOperationNotice.Text = live ? "状态来自飞控回报。发送请求后请核对反馈。" : "未连接设备 · 等待飞控状态与操作回报";
                projectOperationNotice.ForeColor = ProjectWorkspaceStyle.Muted;
            }
        }

        private static TableLayoutPanel ProjectRow(params Control[] controls)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 42, ColumnCount = controls.Length, RowCount = 1, Margin = Padding.Empty };
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (var i = 0; i < controls.Length; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
                controls[i].Dock = DockStyle.Fill; controls[i].Margin = new Padding(3, 4, 3, 4); row.Controls.Add(controls[i], i, 0);
            }
            return row;
        }
        private static Control ProjectMetricCell(string title, Label value)
        {
            return new ProjectTelemetryCell(title, value);
        }

        // 状态词沿用现有颜色判定；这里只补足断联与异常的文字呈现。
        private void UpdateProjectMonitorAppearance(bool connected)
        {
            RefreshProjectQuickUnits();
            // 原业务回报仍直接显示；断联时不把上次的读取提示留作当前状态。
            if (!connected && projectControlFeedback != null)
                projectControlFeedback.Text = "未连接设备 · 等待控制回报";
            foreach (var item in new[] { projectEkfStatus, projectVibeStatus })
            {
                var prefix = item == projectEkfStatus ? "EKF" : "VIBE";
                item.Text = prefix + "  " + (!connected ? "断联" : item.BackColor == Color.Red ? "异常" : item.BackColor == Color.Orange ? "注意" : "正常");
            }
        }

        private static Control ProjectGroup(string title, params Control[] controls)
        {
            var group = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(8), Margin = new Padding(0, 0, 0, 8) };
            group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var heading = ProjectWorkspaceStyle.Heading(title, 28); group.Controls.Add(heading);
            foreach (var control in controls) { control.Dock = DockStyle.Top; group.Controls.Add(control); }
            group.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, group.ClientRectangle, ProjectWorkspaceStyle.Edge, ButtonBorderStyle.Solid);
            return group;
        }

        private void InitializeProjectAppearance()
        {
            Padding = new Padding(12, 8, 12, 8);
            MainH.SplitterWidth = 8; projectOperationSplit.SplitterWidth = 8;
            MainH.Panel1.Padding = new Padding(8, 4, 8, 8); MainH.Panel2.Padding = Padding.Empty;
            SubMainLeft.Panel2MinSize = 370;
            SubMainLeft.SplitterWidth = 8;
            MainH.BorderStyle = SubMainLeft.BorderStyle = projectOperationSplit.BorderStyle = BorderStyle.None;
            tableLayoutPanelQuick.MinimumSize = new Size(0, 168);
            // 原第六格默认绑定为 DistToHome；保留用户保存的字段供原 Activate 使用。
            var savedSixth = MissionPlanner.Utilities.Settings.Instance[quickView6.Name];
            quickView6.Tag = string.IsNullOrEmpty(savedSixth) ? "DistToHome" : savedSixth;
            quickView6.desc = MainV2.comPort.MAV.cs.GetNameandUnit(quickView6.Tag.ToString());
            projectOperationSplit.Panel2.Padding = new Padding(8);

            var leftTitle = new Panel { Name = "ProjectMonitorTitle", Dock = DockStyle.Top, Height = 36 };
            var more = Controls.Find("ProjectMonitorEntry", true).First(); more.Dock = DockStyle.Right; more.AutoSize = false; more.Width = 92; more.Height = 32;
            var monitorTitle = ProjectWorkspaceStyle.Heading("飞行监视", 36); monitorTitle.Dock = DockStyle.Fill;
            monitorTitle.Name = "ProjectRegionTitle";
            leftTitle.Controls.Add(monitorTitle); leftTitle.Controls.Add(more);
            MainH.Panel1.Controls.Add(leftTitle); leftTitle.SendToBack();


            var monitor = (TableLayoutPanel)Controls.Find("ProjectMonitor", true).First();
            var oldHealth = monitor.GetControlFromPosition(0, 0);
            var flags = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            flags.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            flags.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); flags.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            projectEkfStatus.Dock = projectVibeStatus.Dock = DockStyle.Fill;
            foreach (var indicator in new[] { projectEkfStatus, projectVibeStatus })
            {
                indicator.MinimumSize = Size.Empty; indicator.AutoSize = false; indicator.Margin = new Padding(2);
            }
            flags.Controls.Add(projectEkfStatus, 0, 0); flags.Controls.Add(projectVibeStatus, 1, 0);
            ((ProjectActionButton)projectEkfStatus).StatusIndicator = true;
            ((ProjectActionButton)projectVibeStatus).StatusIndicator = true;
            projectBattery2Status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            var batteries = ProjectRow(ProjectMetricCell("电池 1", projectBatteryStatus), ProjectMetricCell("电池 2", projectBattery2Status)); batteries.Height = 36;
            var signals = ProjectRow(ProjectMetricCell("数传", projectLinkStatus), ProjectMetricCell("GPS", projectGpsStatus)); signals.Height = 36;
            signals.ColumnStyles[0].Width = 38; signals.ColumnStyles[1].Width = 62;
            foreach (Control cell in batteries.Controls.Cast<Control>().Concat(signals.Controls.Cast<Control>())) { cell.Margin = new Padding(2, 1, 2, 1);  }
            var health = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            health.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            health.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); health.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); health.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); health.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            health.Controls.Add(ProjectWorkspaceStyle.Heading("平台状态", 26), 0, 0); health.Controls.Add(batteries, 0, 1); health.Controls.Add(signals, 0, 2); health.Controls.Add(flags, 0, 3);
            monitor.Controls.Remove(oldHealth); monitor.Controls.Add(health, 0, 0); monitor.RowStyles[0].Height = 136;
            var quickHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            quickHost.Controls.Add(tableLayoutPanelQuick); quickHost.Controls.Add(ProjectWorkspaceStyle.Heading("快速数据", 28));
            monitor.Controls.Add(quickHost, 0, 1);
            projectMessagePanel.Margin = Padding.Empty;
            tableLayoutPanelQuick.Margin = Padding.Empty;
            monitor.RowStyles[2].SizeType = SizeType.Absolute;
            monitor.RowStyles[2].Height = 34;
            // 消息展开时为六张数据卡保留真实空间；不让最小高度挤出最后一行。
            Action arrangeMonitor = () =>
            {
                var expanded = txt_messagebox.Visible;
                // 150%下1920×1080窗口只有720逻辑高；压紧指标间距，六张卡片仍完整可见。
                var compactMonitor = SubMainLeft.Height < 600;
                var healthHeight = compactMonitor ? 114 : 136;
                health.RowStyles[0].Height = compactMonitor ? 24 : 28;
                health.GetControlFromPosition(0, 0).Height = compactMonitor ? 22 : 26;
                for (var row = 1; row < 4; row++) health.RowStyles[row].Height = compactMonitor ? 30 : 36;
                monitor.RowStyles[0].Height = healthHeight;
                var available = Math.Max(0, SubMainLeft.Height - SubMainLeft.Panel1MinSize - SubMainLeft.SplitterWidth);
                // 消息沿用原文本框与滚动条；短窗口按剩余空间展开，不遮住快速数据。
                var maximumMessage = Math.Max(34, available - healthHeight - 28 - 168 - 4);
                var messageHeight = expanded ? Math.Min(120, maximumMessage) : 34;
                var minimum = healthHeight + 28 + 168 + messageHeight + 4;
                SubMainLeft.Panel2MinSize = Math.Min(minimum, available);
                monitor.RowStyles[2].Height = messageHeight;
            };
            txt_messagebox.VisibleChanged += (sender, args) => arrangeMonitor();
            SubMainLeft.SizeChanged += (sender, args) => arrangeMonitor();
            arrangeMonitor();
            projectMessageToggle.Height = 34;
            txt_messagebox.BorderStyle = BorderStyle.None;

            // 页签仅负责选择原页面；原控件实例、数据源和事件均保留。
            projectOperationSplit.Panel2.Padding = new Padding(ProjectPixels(12));
            var operationHeader = new Panel { Name = "ProjectOperationHeader", Dock = DockStyle.Top, Height = ProjectPixels(92) };
            var operationTitle = ProjectWorkspaceStyle.Heading("飞行操作", ProjectPixels(36));
            operationTitle.Name = "ProjectRegionTitle";
            var wide = new ProjectActionButton { Name = "ProjectWideOperations", Text = "展开", Width = ProjectPixels(64), Dock = DockStyle.Right, AutoSize = false, MinimumSize = Size.Empty };
            wide.Click += (sender, args) =>
            {
                var expanded = projectOperationSplit.Panel2.Width > ProjectPixels(460);
                var width = ProjectPixels(expanded ? (Width >= ProjectPixels(1700) ? 400 : 330) : 540);
                projectOperationSplit.SplitterDistance = Math.Max(projectOperationSplit.Panel1MinSize, projectOperationSplit.Width - width - projectOperationSplit.SplitterWidth);
                wide.Text = expanded ? "展开" : "收窄";
                SaveProjectWorkspaceLayout();
            };
            var titleRow = new Panel { Dock = DockStyle.Top, Height = ProjectPixels(36), Padding = new Padding(0, 0, 0, ProjectPixels(4)) };
            operationTitle.Dock = DockStyle.Fill;
            titleRow.Controls.Add(operationTitle); titleRow.Controls.Add(wide);
            var navigation = new TableLayoutPanel { Name = "ProjectNavigationBar", Dock = DockStyle.Bottom, Height = ProjectPixels(48), ColumnCount = 5, RowCount = 1, Padding = new Padding(0, ProjectPixels(4), 0, ProjectPixels(6)) };
            navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            projectNavigation = new Button[5];
            for (var i = 0; i < 5; i++)
            {
                var index = i;
                navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
                var button = new ProjectActionButton { Name = "ProjectNavigation" + i, Kind = ProjectActionKind.Navigation, Text = projectOperationTabs.TabPages[i].Text,
                    AutoSize = false, MinimumSize = Size.Empty, Dock = DockStyle.Fill, Margin = new Padding(i == 0 ? 0 : ProjectPixels(2), 0, i == 4 ? 0 : ProjectPixels(2), 0) };
                button.Click += (sender, args) => projectOperationTabs.SelectedIndex = index;
                projectNavigation[i] = button; navigation.Controls.Add(button, i, 0);
            }
            operationHeader.Controls.Add(titleRow); operationHeader.Controls.Add(navigation);
            projectOperationSplit.Panel2.Controls.Add(operationHeader); operationHeader.SendToBack();
            projectOperationTabs.Appearance = TabAppearance.FlatButtons; projectOperationTabs.SizeMode = TabSizeMode.Fixed; projectOperationTabs.ItemSize = new Size(0, 1);
            projectOperationTabs.SelectedIndexChanged += (sender, args) => UpdateProjectNavigation();

            var actionTab = projectOperationTabs.TabPages[0];
            actionTab.Padding = Padding.Empty;
            var oldRows = actionTab.Controls.OfType<TableLayoutPanel>().First();
            projectModeFeedback = new Label { Name = "ProjectModeValue", Text = "等待连接", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            projectArmedFeedback = new Label { Name = "ProjectArmValue", Text = "断开连接", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true };
            projectModeFeedback.TextChanged += (sender, args) => StyleProjectActionState();
            projectArmedFeedback.TextChanged += (sender, args) => StyleProjectActionState();
            var stateRow = new TableLayoutPanel { Name = "ProjectModeSummary", Dock = DockStyle.Top, Height = ProjectPixels(60), ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 0, 0, ProjectPixels(8)) };
            stateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67)); stateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            stateRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            stateRow.Controls.Add(projectModeFeedback, 0, 0); stateRow.Controls.Add(projectArmedFeedback, 1, 0);
            BUT_setmode.Text = "切换"; BUTactiondo.Text = "执行"; BUT_setwp.Text = "设定"; BUT_quickauto.Text = "AUTO";
            var shortcut = ProjectRow(BUT_quickauto, BUT_ARM); shortcut.Height = ProjectPixels(40);
            var mode = ProjectOperationCard("飞行模式", "飞控状态", stateRow, ProjectOperationRow("目标模式", CMB_modes, BUT_setmode), shortcut);
            var mission = ProjectOperationCard("任务与动作", "", ProjectOperationRow("动作", CMB_action, BUTactiondo), ProjectOperationRow("航点", CMB_setwp, BUT_setwp));
            var auxSelector = new ComboBox { Name = "ProjectAuxGroup", AccessibleName = "AUX 组", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            var auxSelectorRow = new TableLayoutPanel { Height = ProjectPixels(38), Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1 };
            auxSelectorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ProjectPixels(64))); auxSelectorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            auxSelectorRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            auxSelectorRow.Controls.Add(new Label { Text = "功能组", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            auxSelector.Margin = new Padding(0, ProjectPixels(5), 0, ProjectPixels(5)); auxSelectorRow.Controls.Add(auxSelector, 1, 0);
            var auxiliaries = flowLayoutPanel1.Controls.Cast<AuxOptions>().ToArray();
            for (var i = 0; i < auxiliaries.Length; i++) { auxSelector.Items.Add("AUX " + (i + 1)); auxiliaries[i].ConfigureProjectWorkspace(); auxiliaries[i].Visible = false; }
            flowLayoutPanel1.Dock = DockStyle.Top; flowLayoutPanel1.Height = ProjectPixels(146); flowLayoutPanel1.AutoScroll = false;
            auxSelector.SelectedIndexChanged += (sender, args) => { for (var i = 0; i < auxiliaries.Length; i++) auxiliaries[i].Visible = i == auxSelector.SelectedIndex; };
            var auxiliary = ProjectOperationCard("AUX Function", "辅助功能", auxSelectorRow, flowLayoutPanel1);
            auxSelector.SelectedIndex = 0;
            projectOperationNotice = new Label { Name = "ProjectOperationNotice", Dock = DockStyle.Top, Height = ProjectPixels(32), Padding = new Padding(ProjectPixels(4)), TextAlign = ContentAlignment.TopLeft };
            var stack = new TableLayoutPanel { Name = "ProjectActionGroups", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 4, Padding = Padding.Empty, Margin = Padding.Empty };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 4; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(mode, 0, 0); stack.Controls.Add(mission, 0, 1); stack.Controls.Add(auxiliary, 0, 2); stack.Controls.Add(projectOperationNotice, 0, 3);
            actionTab.Controls.Remove(oldRows); actionTab.Controls.Add(stack);
            Disposed += (sender, args) => projectModeFont.Dispose();

            // 底部按钮只重排原有入口；保持相同确认和发送流程。
            var oldFooter = (FlowLayoutPanel)Controls.Find("ProjectFooter", true).First();
            var check = oldFooter.Controls.Find("ProjectChecks", true).First(); check.Text = "飞前检查";
            var auto = oldFooter.Controls.Find("ProjectAuto", true).First(); auto.Text = "AUTO";
            var collapse = oldFooter.Controls.Find("ProjectToggleOperations", true).First(); collapse.Text = "收起右栏"; collapse.AutoSize = false; collapse.Size = new Size(92, 30);
            splitContainer1.Panel2.Controls.Add(collapse); collapse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Action placeCollapse = () => { if (collapse.Parent != splitContainer1.Panel2) return; collapse.Location = new Point(Math.Max(4, splitContainer1.Panel2.Width - 102), 6); collapse.BringToFront(); };
            splitContainer1.Panel2.SizeChanged += (s, e) => placeCollapse();
            collapse.Click += (s, e) => collapse.Text = projectOperationSplit.Panel2Collapsed ? "显示右栏" : "收起右栏"; placeCollapse();
            projectEngineStopButton.Text = "停止发动机";
            var flightRow = ProjectRow(check, projectArmButton, auto); var engineRow = ProjectRow(projectEngineStartButton, projectEngineRestoreButton, projectEngineStopButton);
            engineRow.ColumnStyles[0].Width = 36; engineRow.ColumnStyles[1].Width = 24; engineRow.ColumnStyles[2].Width = 40;
            var commandRows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(7, 2, 7, 2) };
            commandRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); commandRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            commandRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); commandRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            commandRows.Controls.Add(new ProjectControlHeading("飞行控制", "FLIGHT CONTROL"), 0, 0); commandRows.Controls.Add(new ProjectControlHeading("发动机控制", "ENGINE CONTROL"), 1, 0);
            commandRows.Controls.Add(flightRow, 0, 1); commandRows.Controls.Add(engineRow, 1, 1);
            foreach (Control button in flightRow.Controls.Cast<Control>().Concat(engineRow.Controls.Cast<Control>())) { button.MinimumSize = Size.Empty; button.AutoSize = false; button.Margin = new Padding(2, 3, 2, 3); }
            var status = oldFooter.Controls.OfType<Label>().First(); status.MinimumSize = Size.Empty; status.AutoSize = false; status.Dock = DockStyle.Bottom; status.Height = 24; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true;
            projectControlFeedback = status;
            status.TextChanged += (s,e) => toolTip1.SetToolTip(status,status.Text); status.Padding = new Padding(10, 0, 0, 0);
            projectActionDock = new Panel { Name = "ProjectActionDock", Height = 104, Dock = DockStyle.Bottom, Padding = new Padding(0, 8, 0, 0) };
            projectActionDock.Controls.Add(commandRows); projectActionDock.Controls.Add(status);
            projectActionDock.Paint += (s,e) => { using (var edge = new Pen(ProjectWorkspaceStyle.Edge)) e.Graphics.DrawLine(edge,0,0,projectActionDock.Width,0); };
            Controls.Remove(oldFooter);
            // 中央地图和控制台各占独立布局行，避免缩窄时底部改高与 Dock 布局相互覆盖。
            var center = new TableLayoutPanel { Name = "ProjectCenterHost", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            tableMap.Margin = Padding.Empty; projectActionDock.Margin = Padding.Empty; projectActionDock.Dock = DockStyle.Fill;
            center.Controls.Add(tableMap, 0, 0); center.Controls.Add(projectActionDock, 0, 1);
            projectOperationSplit.Panel1.Controls.Add(center);
            var flightCaption = commandRows.GetControlFromPosition(0, 0); var engineCaption = commandRows.GetControlFromPosition(1, 0);
            bool? stacked = null;
            Action arrangeCommands = () =>
            {
                var compact = center.ClientSize.Width < 560; if (stacked == compact) return; stacked = compact;
                commandRows.SuspendLayout(); commandRows.ColumnStyles.Clear(); commandRows.RowStyles.Clear();
                commandRows.ColumnCount = compact ? 1 : 2; commandRows.RowCount = compact ? 4 : 2;
                commandRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, compact ? 100 : 48));
                if (!compact) commandRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
                for (var row = 0; row < (compact ? 4 : 2); row++) commandRows.RowStyles.Add(new RowStyle(SizeType.Absolute, row % 2 == 0 ? 26 : 42));
                commandRows.SetCellPosition(flightCaption, new TableLayoutPanelCellPosition(0, 0)); commandRows.SetCellPosition(flightRow, new TableLayoutPanelCellPosition(0, 1));
                commandRows.SetCellPosition(engineCaption, new TableLayoutPanelCellPosition(compact ? 0 : 1, compact ? 2 : 0)); commandRows.SetCellPosition(engineRow, new TableLayoutPanelCellPosition(compact ? 0 : 1, compact ? 3 : 1));
                center.RowStyles[1].Height = compact ? 172 : 104; commandRows.ResumeLayout(true);
            };
            center.SizeChanged += (s, e) => arrangeCommands(); arrangeCommands();
            distanceBar1.Visible = false;
            gMapControl1.EmptyTileColor = ProjectWorkspaceStyle.Well; gMapControl1.EmptyTileBorders.Color = ProjectWorkspaceStyle.Edge;
            // 原 FlightData_Load 会恢复灰色缺图底色，待原初始化完成后应用项目显示。
            Load += (sender, args) =>
            {
                gMapControl1.EmptyTileColor = ProjectWorkspaceStyle.Well;
                gMapControl1.EmptyTileBorders.Color = ProjectWorkspaceStyle.Well;
                gMapControl1.Invalidate();
            };
            gMapControl1.CompactMissingTiles = true; gMapControl1.ShowTileGridLines = false;
            projectMapNotice = new Label { Name = "ProjectMapNotice", Height = 28, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 4, 0), Location = new Point(8, 8), Visible = false };
            splitContainer1.Panel2.Controls.Add(projectMapNotice); projectMapNotice.BringToFront();
            Action sizeNotice = () => projectMapNotice.Width = Math.Max(120, splitContainer1.Panel2.Width - 122);
            splitContainer1.Panel2.SizeChanged += (s, e) => sizeNotice(); sizeNotice();
            ApplyProjectAppearance();
            SizeChanged += (s, e) =>
            {
                if (!projectLayoutRestored || projectLayoutChanging || Width < 900) return;
                projectLayoutChanging = true;
                try
                {
                    if (Width < 1500)
                    {
                        MainH.SplitterDistance = Math.Min(MainH.SplitterDistance, Math.Max(260, (int)(Width * .24)));
                        if (!projectOperationSplit.Panel2Collapsed && projectOperationSplit.Panel2.Width > Width * .27)
                            projectOperationSplit.SplitterDistance = Math.Max(projectOperationSplit.Panel1MinSize, projectOperationSplit.Width - (int)(Width * .27) - projectOperationSplit.SplitterWidth);
                    }
                    SubMainLeft.SplitterDistance = Math.Max(SubMainLeft.Panel1MinSize, Math.Min(SubMainLeft.SplitterDistance, SubMainLeft.Height - SubMainLeft.Panel2MinSize - SubMainLeft.SplitterWidth));
                }
                finally { projectLayoutChanging = false; }
            };
        }

        public void ApplyProjectAppearance()
        {
            ProjectWorkspaceStyle.Apply(this); BackColor = MainH.BackColor = projectOperationSplit.BackColor = ProjectWorkspaceStyle.Surface;
            StyleProjectActionState();
            if (projectEngineStopButton != null) ProjectWorkspaceStyle.StyleAction(projectEngineStopButton, ProjectActionKind.Danger);
            var check = Controls.Find("ProjectChecks", true).FirstOrDefault() as Button; if (check != null) ProjectWorkspaceStyle.Accent(check, true);
            UpdateProjectNavigation();
        }

        private void UpdateProjectNavigation()
        {
            if (projectNavigation == null) return;
            for (var i = 0; i < projectNavigation.Length; i++) ProjectWorkspaceStyle.Accent(projectNavigation[i], i == projectOperationTabs.SelectedIndex);
        }
    }
}
