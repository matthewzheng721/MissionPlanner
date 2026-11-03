using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MissionPlanner.Controls;

namespace MissionPlanner
{
    public partial class MainV2
    {
        private ToolStripLabel projectBrand;
        private ToolStripLabel projectFlightSummary;
        private ToolStripLabel projectTopMode, projectTopArm;
        private bool projectActivationTheme;
        private ToolStripButton projectConnectionEntry;
        private ToolStripLabel projectConnectionStats;
        private Form projectConnectionWindow;
        private Button projectConnectionAction;
        private ToolStrip projectConnectionStrip;
        private readonly ToolStripRenderer projectHeaderRenderer = new ProjectHeaderRenderer();

        public void ApplyProjectAppearance()
        {
            if (projectBrand == null)
            {
                // 原菜单点击处理器只切换旧纹理；项目绘制独立负责外观，业务Click仍保留。
                MainMenu.ItemClicked -= MainMenu_ItemClicked;
                ProjectWindowChrome.Apply(this);
                HandleCreated += (sender, args) => ProjectWindowChrome.Apply(this);
                var workspaceIcon = ProjectWindowChrome.CreateWorkspaceIcon();
                Icon = workspaceIcon;
                Disposed += (sender, args) => workspaceIcon.Dispose();
                // 原菜单和连接宿主继续持有原事件；这里只组织可见的状态与入口。
                projectBrand = new ToolStripLabel("单机地面站") { Name = "ProjectBrand", AutoSize = false, Margin = Padding.Empty };
                projectFlightSummary = new ToolStripLabel("未连接") { Name = "ProjectFlightSummary", AutoSize = false };
                projectTopMode = new ToolStripLabel("—") { Name = "ProjectTopMode", AutoSize = false };
                projectTopArm = new ToolStripLabel("状态未知") { Name = "ProjectTopArm", AutoSize = false };
                MainMenu.Items.Insert(0, projectBrand);
                MainMenu.Items.Insert(1, projectFlightSummary);
                MainMenu.Items.Insert(2, projectTopMode);
                MainMenu.Items.Insert(3, projectTopArm);
                projectConnectionEntry = new ToolStripButton("连接设备") { Name = "ProjectConnectionEntry", Alignment = ToolStripItemAlignment.Right, AutoSize = false, DisplayStyle = ToolStripItemDisplayStyle.Text };
                projectConnectionEntry.Click += (s, e) => ShowProjectConnection();
                MainMenu.Items.Add(projectConnectionEntry);
                projectConnectionStats = new ToolStripLabel("连接统计") { Name = "ProjectConnectionStats", Alignment = ToolStripItemAlignment.Right, IsLink = true, AutoSize = false };
                projectConnectionStats.Click += (s, e) => ShowConnectionStatsForm();
                MainMenu.Items.Add(projectConnectionStats);
                MainMenu.Items.Remove(MenuConnect);
                MainMenu.Items.Remove(toolStripConnectionControl);
                toolStripConnectionControl.Visible = false;
            }
            var scale = DeviceDpi / 96f;
            Func<int, int> px = value => (int)Math.Round(value * scale);
            BackColor = panel1.BackColor = MainMenu.BackColor = ProjectWorkspaceStyle.Surface;
            MainMenu.BackgroundImage = null;
            MainMenu.AutoSize = false;
            MainMenu.Height = panel1.Height = px(68);
            MainMenu.Padding = new Padding(px(12), px(8), px(12), px(8));
            MainMenu.Renderer = projectHeaderRenderer;
            projectBrand.Size = new Size(px(268), px(48));
            projectFlightSummary.Size = new Size(px(132), px(48));
            projectTopMode.Size = new Size(px(148), px(48));
            projectTopArm.Size = new Size(px(124), px(48));
            projectConnectionEntry.Size = new Size(px(136), px(40));
            projectConnectionEntry.Margin = new Padding(px(8), px(4), 0, px(4));
            projectConnectionStats.Size = new Size(px(94), px(40));
            projectConnectionStats.Margin = new Padding(0, px(4), px(8), px(4));
            foreach (ToolStripItem item in MainMenu.Items)
            {
                item.Font = ProjectWorkspaceStyle.TextFont;
                item.ForeColor = ProjectWorkspaceStyle.Text;
                item.BackColor = ProjectWorkspaceStyle.Surface;
                item.BackgroundImage = null;
            }
            projectConnectionStats.LinkColor = projectConnectionStats.ActiveLinkColor = projectConnectionStats.VisitedLinkColor = ProjectWorkspaceStyle.Muted;
            parameterRefreshStatus.ForeColor = ProjectWorkspaceStyle.Muted;
            parameterRefreshStatus.Margin = new Padding(px(12), 0, 0, 0);
            UpdateProjectFlightSummary(comPort.BaseStream.IsOpen, comPort.MAV.cs.mode ?? "", comPort.MAV.cs.armed);
            if (projectConnectionWindow != null && projectConnectionWindow.Visible) LayoutProjectConnection();
            FlightData?.ApplyProjectAppearance();
        }

        private void LayoutProjectConnection()
        {
            var connection = toolStripConnectionControl.ConnectionControl;
            var scale = projectConnectionWindow.DeviceDpi / 96f;
            Func<int, int> px = value => (int)Math.Round(value * scale);
            connection.AutoScaleMode = AutoScaleMode.None;
            connection.BackgroundImage = null;
            ProjectWorkspaceStyle.Apply(connection);
            toolStripConnectionControl.AutoSize = false;
            toolStripConnectionControl.Margin = toolStripConnectionControl.Padding = Padding.Empty;
            toolStripConnectionControl.Size = connection.Size = new Size(px(458), px(48));
            var choices = new[] { connection.cmb_sysid, connection.CMB_serialport, connection.CMB_baudrate };
            var widths = new[] { 150, 120, 100 };
            var x = 0;
            for (var i = 0; i < choices.Length; i++)
            {
                choices[i].Anchor = AnchorStyles.Top | AnchorStyles.Left;
                choices[i].Location = new Point(px(x), px(8));
                choices[i].Width = px(widths[i]);
                x += widths[i] + 8;
            }
            foreach (Control child in connection.Controls)
                if (child is LinkLabel link) { link.Text = "统计"; link.Location = new Point(px(397), px(10)); link.Size = new Size(px(54), px(25)); }
        }

        public void ShowProjectConnection()
        {
            if (projectConnectionWindow != null && !projectConnectionWindow.IsDisposed)
            {
                projectConnectionWindow.Show(this); LayoutProjectConnection(); projectConnectionWindow.Activate(); return;
            }
            projectConnectionWindow = new Form { Name = "ProjectConnection", Text = "连接设备", ClientSize = new Size(510, 242), MinimumSize = new Size(526, 281), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 12) };
            var description = ProjectWorkspaceStyle.Heading("目标 / 连接方式 / 波特率", 36);
            var note = new Label { Text = "选择 COM、TCP 或 UDP 连接方式。\r\n打开此窗口不会自动连接设备。", Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(4), ForeColor = ProjectWorkspaceStyle.Muted };
            projectConnectionStrip = new ToolStrip { Name = "ProjectConnectionStrip", Dock = DockStyle.Top, AutoSize = false, Height = 48, GripStyle = ToolStripGripStyle.Hidden, Padding = Padding.Empty, Renderer = projectHeaderRenderer };
            projectConnectionStrip.Items.Add(toolStripConnectionControl);
            toolStripConnectionControl.Visible = true;
            body.Controls.Add(projectConnectionStrip); body.Controls.Add(description); body.Controls.Add(note);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 4, 16, 4) };
            var close = new ProjectActionButton { Text = "关闭", Size = new Size(90, 36) };
            close.Click += (s, e) => projectConnectionWindow.Close();
            projectConnectionAction = new ProjectActionButton { Name = "ProjectConnectionAction", Kind = ProjectActionKind.Primary, Text = comPort.BaseStream.IsOpen ? "断开连接" : "连接", Size = new Size(130, 36) };
            projectConnectionAction.Click += (s, e) => MenuConnect.PerformClick();
            actions.Controls.Add(close); actions.Controls.Add(projectConnectionAction);
            projectConnectionWindow.Controls.Add(body); projectConnectionWindow.Controls.Add(actions);
            projectConnectionWindow.FormClosing += (s, e) =>
            {
                // 原连接对象属于主窗。普通关闭只隐藏，防止丢失事件与连接配置。
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; projectConnectionWindow.Hide(); }
            };
            ProjectWorkspaceStyle.Apply(projectConnectionWindow);
            LayoutProjectConnection(); projectConnectionWindow.Show(this); LayoutProjectConnection();
        }

        public void UpdateProjectFlightSummary(bool connected, string mode, bool armed)
        {
            if (projectBrand == null) return;
            // 仅呈现真实连接对象；日志回放不能被标记成在线设备。
            var playback = comPort.logreadmode;
            var live = comPort.BaseStream.IsOpen && !playback;
            projectFlightSummary.Text = live ? "设备已连接" : playback ? "日志回放" : "未连接";
            projectFlightSummary.ForeColor = live ? ProjectWorkspaceStyle.Success : playback ? ProjectWorkspaceStyle.Warning : ProjectWorkspaceStyle.Muted;
            projectTopMode.Text = live || playback ? (mode ?? "—").ToUpperInvariant() : "—";
            projectTopArm.Text = live || playback ? (armed ? "已解锁" : "已锁定") : "状态未知";
            projectTopArm.ForeColor = live || playback ? (armed ? ProjectWorkspaceStyle.Warning : ProjectWorkspaceStyle.Success) : ProjectWorkspaceStyle.Muted;
            projectConnectionEntry.Text = live ? "连接设置" : "连接设备";
            projectConnectionStats.Enabled = live;
            if (projectConnectionAction != null) projectConnectionAction.Text = comPort.BaseStream.IsOpen ? "断开连接" : "连接";
        }

        private sealed class ProjectHeaderRenderer : ToolStripProfessionalRenderer
        {
            private static GraphicsPath Outline(RectangleF bounds, float radius)
            {
                var path = new GraphicsPath();
                var diameter = radius * 2;
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure(); return path;
            }
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                // 弹窗连接宿主与父面板同色，避免宿主留白呈现为黑色竖缝。
                if (e.ToolStrip.Name == "ProjectConnectionStrip")
                {
                    e.Graphics.Clear(ProjectWorkspaceStyle.Panel);
                    return;
                }
                e.Graphics.Clear(ProjectWorkspaceStyle.Surface);
                using (var pen = new Pen(ProjectWorkspaceStyle.Edge))
                    e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
            }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                var item = e.Item;
                var scale = e.Graphics.DpiX / 96f;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var color = item.Pressed ? ProjectWorkspaceStyle.ControlColor : item.Selected ? ProjectWorkspaceStyle.Group : ProjectWorkspaceStyle.Panel;
                using (var path = Outline(new RectangleF(.5f, .5f, item.Width - 1, item.Height - 1), 5 * scale))
                using (var fill = new SolidBrush(color))
                using (var border = new Pen(item.Enabled ? ProjectWorkspaceStyle.Cyan : ProjectWorkspaceStyle.Edge))
                { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                var item = e.Item; var g = e.Graphics;
                var scale = g.DpiX / 96f;
                Func<int, int> px = value => (int)Math.Round(value * scale);
                var center = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (item.Name == "ProjectBrand")
                {
                    using (var pen = new Pen(ProjectWorkspaceStyle.Cyan, 1.6f * scale))
                    {
                        var x = px(15); var y = item.Height / 2;
                        g.DrawEllipse(pen, x - px(9), y - px(9), px(18), px(18));
                        g.DrawLine(pen, x, y - px(14), x, y + px(14));
                        g.DrawLine(pen, x - px(14), y, x + px(14), y);
                    }
                    using (var font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold))
                    using (var caption = new Font("Segoe UI", 8.5f))
                    {
                        TextRenderer.DrawText(g, item.Text, font, new Rectangle(px(40), px(1), item.Width - px(44), px(28)), ProjectWorkspaceStyle.Text, center);
                        TextRenderer.DrawText(g, "飞行任务工作台  /  V17-A", caption, new Rectangle(px(41), px(28), item.Width - px(44), px(18)), ProjectWorkspaceStyle.Muted, center);
                    }
                    return;
                }
                if (item.Name == "ProjectFlightSummary" || item.Name == "ProjectTopMode" || item.Name == "ProjectTopArm")
                {
                    using (var edge = new Pen(ProjectWorkspaceStyle.Edge)) g.DrawLine(edge, 0, px(10), 0, item.Height - px(10));
                    var caption = item.Name == "ProjectFlightSummary" ? "连接状态" : item.Name == "ProjectTopMode" ? "飞行模式" : "解锁状态";
                    using (var small = new Font("Microsoft YaHei UI", 8))
                        TextRenderer.DrawText(g, caption, small, new Rectangle(px(14), px(3), item.Width - px(22), px(18)), ProjectWorkspaceStyle.Muted, center);
                    var offset = 14;
                    if (item.Name != "ProjectTopMode")
                    {
                        using (var brush = new SolidBrush(item.ForeColor)) g.FillEllipse(brush, px(14), px(31), px(6), px(6));
                        offset = 28;
                    }
                    TextRenderer.DrawText(g, item.Text, item.Font, new Rectangle(px(offset), px(21), item.Width - px(offset + 6), px(25)), item.ForeColor, center);
                    return;
                }
                e.TextColor = !item.Enabled ? ProjectWorkspaceStyle.Muted : item.Name == "ProjectConnectionEntry" ? ProjectWorkspaceStyle.Cyan : item.ForeColor;
                base.OnRenderItemText(e);
            }
        }
    }
}
