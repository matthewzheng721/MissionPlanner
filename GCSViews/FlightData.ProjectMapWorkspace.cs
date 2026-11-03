using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Controls;
using GMap.NET;

namespace MissionPlanner.GCSViews
{
    public partial class FlightData
    {
        private bool projectMapToolsReady;
        private ProjectWindView projectWindView;
        private ProjectActionButton projectMapLocate;
        private void UpdateProjectMapAppearance(bool connected)
        {
            if (projectMapLocate != null) projectMapLocate.Enabled = connected;
            projectWindView?.Invalidate();
        }
        private ProjectActionButton ProjectMapButton(string name, string caption, int width, EventHandler click)
        {
            var button = new ProjectActionButton { Name = name, Text = caption, Width = width, Height = 34,
                AutoSize = false, MinimumSize = Size.Empty, Margin = new Padding(2), Kind = ProjectActionKind.Normal };
            button.Click += click;
            return button;
        }

        private void InitializeProjectMapWorkspace(FlightPlanner planner)
        {
            if (projectMapToolsReady) return;
            projectMapToolsReady = true;
            tableMap.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
            splitContainer1.BorderStyle = BorderStyle.None;
            splitContainer1.Margin = Padding.Empty; panel1.Margin = Padding.Empty; panel1.AutoSize = false;
            tableMap.RowStyles[1].Height = 32;
            panel1.Padding = new Padding(8, 2, 8, 2);
            coords1.Dock = DockStyle.Fill;
            var cursorCoordinates = planner.ProjectCursorCoordinates;
            cursorCoordinates.Dock = DockStyle.Fill; cursorCoordinates.Vertical = false; cursorCoordinates.ProjectHideUnavailableAltitude = true;
            var coordinateHost = new Panel { Name = "ProjectCoordinateHost", Dock = DockStyle.Fill, Margin = Padding.Empty };
            coordinateHost.Controls.Add(coords1); coordinateHost.Controls.Add(cursorCoordinates);
            coords1.Visible = false; cursorCoordinates.Visible = true;
            var coordinateMode = ProjectMapButton("ProjectCoordinateMode", "光标", 52, (s,e) =>
            {
                var showCursor = !cursorCoordinates.Visible;
                cursorCoordinates.Visible = showCursor; coords1.Visible = !showCursor;
                ((Button)s).Text = showCursor ? "光标" : "飞机";
            });
            toolTip1.SetToolTip(coordinateMode, "切换光标地形坐标与飞机遥测坐标");
            CB_tuning.Visible = false;
            CHK_autopan.Visible = false;

            var follow = ProjectMapButton("ProjectMapFollow", "跟随飞机", 92, (s, e) => CHK_autopan.Checked = !CHK_autopan.Checked);
            follow.Dock = DockStyle.Right; follow.Margin = Padding.Empty; follow.Kind = ProjectActionKind.Navigation;
            Action syncFollow = () => follow.Selected = CHK_autopan.Checked;
            CHK_autopan.CheckedChanged += (s, e) => syncFollow(); syncFollow();
            var coordinateRow = new TableLayoutPanel { Name = "ProjectCoordinateRow", Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            coordinateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
            coordinateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            coordinateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            coordinateRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            coordinateMode.Dock = DockStyle.Fill; coordinateMode.Margin = Padding.Empty;
            follow.Dock = DockStyle.Fill;
            coordinateRow.Controls.Add(coordinateMode,0,0); coordinateRow.Controls.Add(coordinateHost,1,0); coordinateRow.Controls.Add(follow,2,0);
            panel1.Controls.Add(coordinateRow); coordinateRow.BringToFront();
            bool? compactCoordinates = null;
            Action arrangeCoordinates = () =>
            {
                var compact = panel1.Width < 600; if (compactCoordinates == compact) return; compactCoordinates = compact;
                coordinateRow.SuspendLayout(); coordinateRow.RowStyles.Clear();
                coordinateRow.RowCount = compact ? 2 : 1;
                coordinateRow.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
                if (compact) coordinateRow.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
                coordinateRow.SetColumnSpan(coordinateHost,1);
                coordinateRow.SetCellPosition(coordinateHost,new TableLayoutPanelCellPosition(compact?0:1,compact?1:0));
                coordinateRow.SetColumnSpan(coordinateHost,compact?3:1);
                tableMap.RowStyles[1].Height = compact?56:32;
                coordinateRow.ResumeLayout(true);
            };
            // 子行SizeChanged可能仍处于外层表格布局中；排到本次布局结束后再切换行高。
            var coordinateLayoutQueued = false;
            panel1.SizeChanged += (s,e) =>
            {
                if (coordinateLayoutQueued || !panel1.IsHandleCreated || panel1.IsDisposed) return;
                coordinateLayoutQueued = true;
                panel1.BeginInvoke(new Action(() =>
                {
                    coordinateLayoutQueued = false;
                    if (panel1.IsDisposed) return;
                    arrangeCoordinates(); tableMap.PerformLayout();
                }));
            };
            arrangeCoordinates();

            var toolbar = new FlowLayoutPanel { Name = "ProjectMapToolbar", Location = new Point(8,8), Height = 42,
                AutoSize = false, WrapContents = false, Padding = new Padding(2), BackColor = ProjectWorkspaceStyle.Panel };
            var selector = planner.ProjectMapProviderSelector;
            selector.AccessibleName = "地图图层"; selector.Width = 164; selector.Height = 30;
            selector.Margin = new Padding(4,7,4,4); selector.DropDownStyle = ComboBoxStyle.DropDownList;
            toolbar.Controls.Add(selector);
            toolbar.Controls.Add(ProjectMapButton("ProjectMapRoute", "航线", 52, (s,e) => gMapControl1.ZoomAndCenterMarkers("WPOverlay")));
            var windButton = ProjectMapButton("ProjectMapWind", "风", 40, null);
            windButton.Kind = ProjectActionKind.Navigation; toolbar.Controls.Add(windButton);
            var collapse = Controls.Find("ProjectToggleOperations", true).First();
            collapse.Dock = DockStyle.None; collapse.Anchor = AnchorStyles.None; collapse.Width = 88; collapse.Height = 34;
            collapse.Margin = new Padding(2); toolbar.Controls.Add(collapse);
            splitContainer1.Panel2.Controls.Add(toolbar);

            var scalePanel = new Panel { Name = "ProjectMapScalePanel", Size = new Size(176,180), Visible = false,
                Padding = new Padding(8), BackColor = ProjectWorkspaceStyle.Panel };
            // 保留原缩放数值与滑条的输入事件，悬浮面板只改变宿主位置。
            label1.Visible = false;
            Zoomlevel.Anchor = AnchorStyles.None; Zoomlevel.Dock = DockStyle.Top; Zoomlevel.Visible = true; Zoomlevel.Width = 160;
            TRK_zoom.Anchor = AnchorStyles.None; TRK_zoom.Dock = DockStyle.Fill; TRK_zoom.Visible = true;
            scalePanel.Controls.Add(TRK_zoom); scalePanel.Controls.Add(Zoomlevel);
            splitContainer1.Panel2.Controls.Add(scalePanel);

            var windPanel = new Panel { Name = "ProjectMapWindPanel", Size = new Size(160,130), Visible = false,
                Padding = new Padding(8), BackColor = ProjectWorkspaceStyle.Panel };
            // 原风向仪继续持有绑定；项目视图只适配其显示，隐藏的是旧绘制而非功能。
            windDir1.Visible = false;
            projectWindView = new ProjectWindView(windDir1,
                () => MainV2.comPort.BaseStream.IsOpen && !MainV2.comPort.logreadmode, () => hud1.speedunit) { Dock = DockStyle.Fill };
            windPanel.Controls.Add(projectWindView); splitContainer1.Panel2.Controls.Add(windPanel);
            windButton.Click += (s,e) => { windPanel.Visible = !windPanel.Visible; windButton.Selected = windPanel.Visible; windPanel.BringToFront(); };

            var navigation = new FlowLayoutPanel { Name = "ProjectMapNavigation", Width = 60, Height = 168,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = Padding.Empty, BackColor = ProjectWorkspaceStyle.Panel };
            navigation.Controls.Add(ProjectMapButton("ProjectMapZoomIn", "+", 56, (s,e) => Zoomlevel.Value = Math.Min(Zoomlevel.Maximum, Zoomlevel.Value + 1)));
            navigation.Controls.Add(ProjectMapButton("ProjectMapZoomOut", "−", 56, (s,e) => Zoomlevel.Value = Math.Max(Zoomlevel.Minimum, Zoomlevel.Value - 1)));
            var locate = ProjectMapButton("ProjectMapLocate", "定位", 56, (s,e) =>
            {
                var connection = MainV2.comPort;
                if (!connection.BaseStream.IsOpen || connection.logreadmode) return;
                var state = connection.MAV.cs;
                if (state.lat == 0 && state.lng == 0) return;
                // 仅移动地图视图，不设置引导点、不发送飞控命令。
                gMapControl1.Position = new PointLatLng(state.lat, state.lng);
            });
            projectMapLocate = locate;
            toolTip1.SetToolTip(locate, "将视图移到飞机当前位置"); navigation.Controls.Add(locate);
            var scale = ProjectMapButton("ProjectMapScale", "比例", 56, (s,e) => { scalePanel.Visible = !scalePanel.Visible; scalePanel.BringToFront(); });
            navigation.Controls.Add(scale); splitContainer1.Panel2.Controls.Add(navigation);
            Action arrange = () =>
            {
                toolbar.Width = Math.Max(100, Math.Min(410, splitContainer1.Panel2.ClientSize.Width - 16));
                toolbar.WrapContents = toolbar.Width < 400; toolbar.Height = toolbar.Width < 400 ? 80 : 42;
                // 原折叠按钮原先由地图Resize定位，这里在同一事件末尾归还工具栏流式布局。
                toolbar.PerformLayout();
                navigation.Location = new Point(Math.Max(8,splitContainer1.Panel2.ClientSize.Width - 68), Math.Max(toolbar.Bottom + 8,splitContainer1.Panel2.ClientSize.Height - 180));
                scalePanel.Location = new Point(Math.Max(8,navigation.Left - 184), Math.Max(toolbar.Bottom+8,navigation.Bottom-scalePanel.Height));
                windPanel.Location = new Point(8, toolbar.Bottom + 6);
                projectMapNotice.Location = new Point(8, toolbar.Bottom + 6); projectMapNotice.Width = Math.Max(120,splitContainer1.Panel2.Width-88);
                toolbar.BringToFront(); navigation.BringToFront();
            };
            splitContainer1.Panel2.SizeChanged += (s,e) => arrange(); arrange();
            ProjectWorkspaceStyle.Apply(toolbar); ProjectWorkspaceStyle.Apply(navigation); ProjectWorkspaceStyle.Apply(scalePanel); ProjectWorkspaceStyle.Apply(windPanel); ProjectWorkspaceStyle.Apply(panel1);
            UpdateProjectMapAppearance(MainV2.comPort.BaseStream.IsOpen && !MainV2.comPort.logreadmode);
        }
    }
}


