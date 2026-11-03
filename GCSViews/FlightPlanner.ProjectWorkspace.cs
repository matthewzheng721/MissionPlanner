using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GMap.NET.WindowsForms;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;

namespace MissionPlanner.GCSViews
{
    public partial class FlightPlanner
    {
        private bool projectInline;
        private CheckBox projectMapAddPoints;
        private bool projectPointerDown, projectPointerMoved, projectPointerWheel, projectPointerMarker;
        private Point projectPointerStart;
        // UI 适配只借用现有选择器和光标坐标控件，继续调用原事件和高程查询。
        public ComboBox ProjectMapProviderSelector => comboBoxMapType;
        public Coords ProjectCursorCoordinates => coords1;
        public bool ProjectPlanningActive { get; private set; }
        public bool ProjectMapAdding => ProjectPlanningActive && projectMapAddPoints.Checked;

        public void AttachProjectWorkspace(myGMAP map)
        {
            if (projectInline) return;
            projectInline = true;
            // 以原编辑器作为任务控制器；任务表和唯一地图均是真实原控件，不创建独立窗。
            Load -= FlightPlanner_Load;
            quickadd = true; config(false); quickadd = false;
            var previousMap = MainMap;
            foreach (var overlay in previousMap.Overlays.ToArray())
            {
                previousMap.Overlays.Remove(overlay);
                if (overlay.Id != "WPOverlay") overlay.Id = "ProjectPlan_" + overlay.Id;
                map.Overlays.Add(overlay);
            }
            MainMap = map;
            // 共享宿主不再触发旧页面 Load；补齐其原缩放范围初始化，避免18倍以上切页越界。
            TRK_zoom.Minimum = map.MapProvider.MinZoom; TRK_zoom.Maximum = map.MaxZoom;
            Zoomlevel.Minimum = map.MapProvider.MinZoom; Zoomlevel.Maximum = map.MaxZoom;
            TRK_zoom.Value = (float)map.Zoom; Zoomlevel.Value = Convert.ToDecimal(map.Zoom);
            MainMap.ContextMenuStrip = null;
            MainMap.DragButton = MouseButtons.Left;
            MainMap.MouseDown += ProjectMapMouseDown;
            MainMap.MouseMove += ProjectMapMouseMove;
            MainMap.MouseUp += ProjectMapMouseUp;
            MainMap.MouseWheel += (sender, args) => projectPointerWheel = true;
            MainMap.MouseCaptureChanged += (sender, args) => { if (!MainMap.Capture) ResetProjectPointer(); };
            MainMap.OnMarkerEnter += item => { if (ProjectMapAdding && IsProjectMarker(item)) MainMap_OnMarkerEnter(item); };
            MainMap.OnMarkerLeave += item => { if (IsProjectMarker(item)) MainMap_OnMarkerLeave(item); };
            MainMap.OnMarkerClick += (item, args) => { if (ProjectPlanningActive && IsProjectMarker(item)) MainMap_OnMarkerClick(item, args); };
            MainMap.OnPositionChanged += MainMap_OnCurrentPositionChanged;
            MainMap.OnMapZoomChanged += MainMap_OnMapZoomChanged;
            MainMap.OnMapTypeChanged += MainMap_OnMapTypeChanged;
            previousMap.Dispose();
            Controls.Clear();
            var layout = new TableLayoutPanel { Name = "ProjectMissionLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            var defaults = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
            projectMapAddPoints = new CheckBox { Name = "ProjectMapAddPoints", Text = "地图加点", Appearance = Appearance.Button, AutoSize = true, Margin = new Padding(3, 3, 16, 3) };
            projectMapAddPoints.CheckedChanged += (sender, args) =>
            {
                if (projectMapAddPoints.Checked && Commands.Rows.Count == 0) updateHome();
                ResetProjectPointer();
            };
            defaults.Controls.Add(projectMapAddPoints);
            defaults.Controls.Add(new Label { Text = "默认高度", AutoSize = true, Margin = new Padding(3, 10, 3, 3) });
            TXT_DefaultAlt.Width = 60; defaults.Controls.Add(TXT_DefaultAlt);
            CMB_altmode.Width = 145; defaults.Controls.Add(CMB_altmode);
            defaults.Controls.Add(new Label { Text = "关闭加点时只浏览；可连续单击加点。", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            layout.Controls.Add(defaults, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            foreach (var button in new[] { BUT_read, BUT_write, BUT_loadwpfile, BUT_saveWPFile })
            {
                button.Dock = DockStyle.None; button.Size = new Size(76, 30); button.Margin = new Padding(3);
                actions.Controls.Add(button);
            }
            BUT_read.Text = "读取任务"; BUT_write.Text = "写入任务";
            BUT_loadwpfile.Text = "加载文件"; BUT_saveWPFile.Text = "保存文件";
            layout.Controls.Add(actions, 0, 1);
            Commands.Dock = DockStyle.Fill; Commands.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            Commands.RowHeadersWidth = 38;
            foreach (DataGridViewColumn column in Commands.Columns) column.Width = 64;
            Command.Width = 120; Lat.Width = 100; Lon.Width = 100; Alt.Width = 65;
            // 只调整显示顺序，保留原列索引及所有参数编辑／校验路径。
            Command.DisplayIndex = 0; Alt.DisplayIndex = 1; Lat.DisplayIndex = 2; Lon.DisplayIndex = 3;
            layout.Controls.Add(Commands, 0, 2);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            var add = new Button { Text = "表格加点", AutoSize = true }; add.Click += BUT_Add_Click;
            var clear = new Button { Text = "清空任务", AutoSize = true }; clear.Click += clearMissionToolStripMenuItem_Click;
            var fit = new Button { Text = "查看航线", AutoSize = true }; fit.Click += (sender, args) => MainMap.ZoomAndCenterMarkers("WPOverlay");
            footer.Controls.AddRange(new Control[] { add, clear, fit });
            chk_usemavftp.Text = "MAVFTP"; chk_usemavftp.AutoSize = true; footer.Controls.Add(chk_usemavftp);
            layout.Controls.Add(footer, 0, 3);
            Controls.Add(layout);
            Activate(); timer1.Stop(); writeKML();
        }

        public void SetProjectPlanningActive(bool active)
        {
            if (!projectInline) return;
            if (ProjectPlanningActive != active || !active) projectMapAddPoints.Checked = false;
            ProjectPlanningActive = active;
            projectMapAddPoints.Enabled = active && !missionTransferRunning;
            ResetProjectPointer();
        }

        private bool IsProjectMarker(GMapMarker marker) => wpOverlay != null && ReferenceEquals(marker.Overlay, wpOverlay.overlay);

        private void ResetProjectPointer()
        {
            projectPointerDown = false; isMouseDown = false; isMouseDraging = false;
            CurentRectMarker = null; CurrentMidLine = null; CurrentRallyPt = null; CurrentPOIMarker = null;
            if (projectInline) MainMap.CanDragMap = true;
        }

        private void ProjectMapMouseDown(object sender, MouseEventArgs e)
        {
            if (!ProjectMapAdding || !Enabled || missionTransferRunning || e.Button != MouseButtons.Left) return;
            projectPointerDown = true; projectPointerMoved = false; projectPointerWheel = false;
            projectPointerStart = e.Location;
            // 每次按下重新命中，连续点击同一标记不依赖再次触发 MouseEnter。
            var hit = wpOverlay?.overlay.Markers.LastOrDefault(marker => marker.IsVisible && marker.IsHitTestVisible &&
                marker.LocalAreaInControlSpace.Contains(e.Location));
            if (hit != null) MainMap_OnMarkerEnter(hit);
            projectPointerMarker = hit != null;
            if (projectPointerMarker && CurentRectMarker == null && CurrentMidLine == null)
            {
                projectPointerDown = false;
                return;
            }
            if (projectPointerMarker) { MainMap.CanDragMap = false; MainMap_MouseDown(sender, e); }
        }

        private void ProjectMapMouseMove(object sender, MouseEventArgs e)
        {
            if (!projectPointerDown && e.Button == MouseButtons.None)
            {
                var point = MainMap.FromLocalToLatLng(e.X, e.Y);
                currentMarker.Position = point;
                SetMouseDisplay(point.Lat, point.Lng, 0);
            }
            if (!projectPointerDown) return;
            if (Math.Abs(e.X - projectPointerStart.X) >= SystemInformation.DragSize.Width / 2 ||
                Math.Abs(e.Y - projectPointerStart.Y) >= SystemInformation.DragSize.Height / 2) projectPointerMoved = true;
            if (projectPointerMarker && projectPointerMoved && !projectPointerWheel) MainMap_MouseMove(sender, e);
        }

        private void ProjectMapMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var apply = projectPointerDown && ProjectMapAdding && Enabled && !missionTransferRunning && !projectPointerWheel;
            if (apply && projectPointerMarker) MainMap_MouseUp(sender, e);
            else if (apply && !projectPointerMoved)
            {
                var location = MainMap.FromLocalToLatLng(e.X, e.Y);
                AddWPToMap(location.Lat, location.Lng, 0);
            }
            else if (projectPointerMarker) writeKML();
            ResetProjectPointer();
        }
    }
}
