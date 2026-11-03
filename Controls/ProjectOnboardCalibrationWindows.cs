using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.GCSViews.ConfigurationView;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    // 只承载两个已经精简的原校准页面，不复制校准命令或改变原保护。
    public sealed class ProjectOnboardCalibrationWindows : IDisposable
    {
        private readonly Control owner;
        private Form menu, level, compass;
        private bool disposed;
        public ProjectOnboardCalibrationWindows(Control owner) { this.owner = owner; owner.Disposed += OwnerDisposed; }
        public void ShowMenu()
        {
            if (disposed) return;
            if (menu == null || menu.IsDisposed)
            {
                menu = ProjectMonitoringWindows.CreateWindow("机上校准", new Size(360, 120));
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), FlowDirection = FlowDirection.TopDown, WrapContents = false };
                ProjectMonitoringWindows.AddButton(buttons, "校准水平", ShowLevel);
                ProjectMonitoringWindows.AddButton(buttons, "大飞机罗盘校准", ShowCompass);
                menu.Controls.Add(buttons); ThemeManager.ApplyThemeTo(menu); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(menu);
            }
            ShowOwned(menu);
        }
        public void ShowLevel()
        {
            if (disposed) return;
            if (level == null || level.IsDisposed) level = CreateCalibration("校准水平", new ConfigAccelerometerCalibration());
            ShowOwned(level);
        }
        public void ShowCompass()
        {
            if (disposed) return;
            if (compass == null || compass.IsDisposed) compass = CreateCalibration("大飞机罗盘校准", new ConfigHWCompass2());
            ShowOwned(compass);
        }
        private Form CreateCalibration(string title, MyUserControl page)
        {
            var form = ProjectMonitoringWindows.CreateWindow(title, new Size(640, 245));
            page.Dock = DockStyle.Fill; page.AutoScroll = true;
            // 原页面隐藏了其他功能；只重新排列仍可见的原按钮和说明，保留事件与数据处理。
            var shown = page.Controls.Cast<Control>().Where(control => control.Visible).OrderBy(control => control.Top).ToArray();
            var y = 18;
            foreach (var control in shown)
            {
                control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                control.Location = new Point(18, y);
                if (control is Label label) { label.MaximumSize = new Size(580, 0); label.AutoSize = true; }
                if (control is Button button) { button.Text = title; button.AutoSize = true; button.MinimumSize = new Size(160, 32); }
                y += control.PreferredSize.Height + 16;
            }
            var connection = new Label { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(12, 8, 0, 0) };
            form.Controls.Add(page); form.Controls.Add(connection);
            var timer = new Timer { Interval = 500 };
            Action refresh = () =>
            {
                var port = MainV2.comPort;
                var connected = port != null && port.BaseStream.IsOpen && !port.logreadmode;
                page.Enabled = connected;
                connection.Text = connected ? "校准保护和完成反馈沿用原 MP 页面。" : "请先连接飞控；回放状态不能执行校准。";
            };
            timer.Tick += (sender, args) => refresh();
            bool activated = false;
            form.Shown += (sender, args) => { ((IActivate)page).Activate(); activated = true; refresh(); timer.Start(); };
            form.FormClosing += (sender, args) => { timer.Stop(); if (activated) { ((IDeactivate)page).Deactivate(); activated = false; } };
            form.Disposed += (sender, args) => { timer.Dispose(); if (activated) ((IDeactivate)page).Deactivate(); };
            ThemeManager.ApplyThemeTo(form); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(form); refresh();
            return form;
        }
        private void ShowOwned(Form form) { if (form.Visible) form.Activate(); else form.Show(owner); }
        private void OwnerDisposed(object sender, EventArgs args) { Dispose(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            owner.Disposed -= OwnerDisposed;
            menu?.Close(); level?.Close(); compass?.Close();
            menu?.Dispose(); level?.Dispose(); compass?.Dispose();
        }
    }
}
