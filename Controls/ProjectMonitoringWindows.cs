using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    // 独立窗口只承载原控件；状态和曲线刷新仍由 FlightData 原逻辑负责。
    public sealed class ProjectMonitoringWindows : IDisposable
    {
        private readonly Control owner;
        private readonly BorrowedWindow status, curve;
        private Form menu, ecu;
        private bool disposed;
        public ProjectMonitoringWindows(Control owner, Control statusContent, Control curveContent,
            Action<bool> statusVisibility, Action<bool> curveVisibility)
        {
            this.owner = owner;
            status = new BorrowedWindow(owner, "全部状态", statusContent, statusVisibility, new Size(920, 620));
            curve = new BorrowedWindow(owner, "实时曲线", curveContent, curveVisibility, new Size(900, 540));
            owner.Disposed += OwnerDisposed;
        }
        public void ShowMenu()
        {
            if (disposed) return;
            if (menu == null || menu.IsDisposed)
            {
                menu = CreateWindow("更多监视", new Size(340, 185));
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), FlowDirection = FlowDirection.TopDown, WrapContents = false };
                AddButton(buttons, "全部状态", ShowStatus); AddButton(buttons, "实时曲线", ShowCurve); AddButton(buttons, "ECU 信息", ShowEcu);
                menu.Controls.Add(buttons); ThemeManager.ApplyThemeTo(menu); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(menu);
            }
            ShowOwned(menu);
        }
        public void ShowStatus() { if (!disposed) status.Show(); }
        public void ShowCurve() { if (!disposed) curve.Show(); }
        public void ShowEcu()
        {
            if (disposed) return;
            if (ecu == null || ecu.IsDisposed)
            {
                ecu = CreateWindow("ECU 信息", new Size(660, 480));
                ecu.Controls.Add(new ProjectEcuMonitorPanel { Dock = DockStyle.Fill });
                ThemeManager.ApplyThemeTo(ecu); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(ecu);
            }
            ShowOwned(ecu);
        }
        private void ShowOwned(Form form) { if (form.Visible) form.Activate(); else form.Show(owner); }
        internal static Form CreateWindow(string title, Size size) => new Form { Text = title, Name = "Project" + title,
            ClientSize = size, MinimumSize = new Size(Math.Min(size.Width, 400), Math.Min(size.Height + 40, 300)),
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
        internal static void AddButton(Control parent, string title, Action action)
        {
            var button = new Button { Text = title, AutoSize = true, MinimumSize = new Size(160, 32), Margin = new Padding(4) };
            button.Click += (sender, args) => action(); parent.Controls.Add(button);
        }
        private void OwnerDisposed(object sender, EventArgs args) { Dispose(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            owner.Disposed -= OwnerDisposed;
            menu?.Dispose(); ecu?.Dispose(); status.Dispose(); curve.Dispose();
        }
        private sealed class BorrowedWindow : IDisposable
        {
            private readonly Control owner, content, originalParent;
            private readonly DockStyle originalDock;
            private readonly Rectangle originalBounds;
            private readonly int originalIndex;
            private readonly bool originalVisible;
            private readonly Action<bool> visibility;
            private readonly string title;
            private readonly Size size;
            private Form window;
            private bool active;
            public BorrowedWindow(Control owner, string title, Control content, Action<bool> visibility, Size size)
            {
                this.owner = owner; this.title = title; this.content = content; this.visibility = visibility; this.size = size;
                originalParent = content.Parent; originalDock = content.Dock; originalBounds = content.Bounds; originalVisible = content.Visible;
                originalIndex = originalParent == null ? 0 : originalParent.Controls.GetChildIndex(content);
            }
            public void Show()
            {
                if (content.IsDisposed) return;
                if (window != null && !window.IsDisposed) { window.Activate(); return; }
                window = CreateWindow(title, size);
                window.Controls.Add(content); content.Dock = DockStyle.Fill; content.Visible = true;
                window.FormClosing += (sender, args) => Restore();
                window.FormClosed += (sender, args) => Restore();
                ThemeManager.ApplyThemeTo(window); MissionPlanner.Controls.ProjectWorkspaceStyle.Apply(window);
                active = true; visibility?.Invoke(true);
                window.Show(owner);
            }
            private void Restore()
            {
                if (!active) return; active = false;
                visibility?.Invoke(false);
                // 先移出再销毁窗口，保证关闭与重开使用同一实时控件及原历史数据。
                if (!content.IsDisposed)
                {
                    content.Parent?.Controls.Remove(content);
                    if (originalParent != null && !originalParent.IsDisposed)
                    {
                        originalParent.Controls.Add(content);
                        originalParent.Controls.SetChildIndex(content, Math.Min(originalIndex, originalParent.Controls.Count - 1));
                    }
                    content.Dock = originalDock; content.Bounds = originalBounds; content.Visible = originalVisible;
                }
            }
            public void Dispose()
            {
                Restore(); window?.Dispose();
                if (!content.IsDisposed && content.Parent == null) content.Dispose();
            }
        }
    }
}
