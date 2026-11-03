using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    public enum ProjectActionKind { Normal, Primary, Danger, Navigation }

    // 保留标准按钮的点击、快捷键、对话框结果与无障碍语义，只负责呈现。
    public class ProjectActionButton : Button
    {
        private ProjectActionKind kind;
        private bool selected, hovered, mousePressed, keyPressed;

        public ProjectActionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Font = ProjectWorkspaceStyle.TextFont;
            BackColor = ProjectWorkspaceStyle.ControlColor;
            ForeColor = ProjectWorkspaceStyle.Text;
            Padding = new Padding(8, 0, 8, 0);
            MinimumSize = new Size(0, 32);
            Size = new Size(100, 32);
        }

        // 仅改变健康按钮的呈现；原阈值、颜色赋值和点击事件保留。
        public bool StatusIndicator { get; set; }

        [DefaultValue(ProjectActionKind.Normal)]
        public ProjectActionKind Kind
        {
            get { return kind; }
            set { if (kind != value) { kind = value; RefreshAppearance(); } }
        }

        // 选中仅改变导航按钮的视觉状态，不自动执行点击或修改页面选择。
        [DefaultValue(false)]
        public bool Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; RefreshAppearance(); } }
        }

        internal void RefreshAppearance()
        {
            ProjectUiPaint.InvalidateVisible(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (StatusIndicator)
            {
                var unknown = Text.Contains("断联");
                var status = unknown ? ProjectWorkspaceStyle.Muted : BackColor == Color.Red ? ProjectWorkspaceStyle.Danger : BackColor == Color.Orange ? ProjectWorkspaceStyle.Warning : ProjectWorkspaceStyle.Success;
                e.Graphics.Clear(hovered && Enabled ? ProjectWorkspaceStyle.ControlColor : ProjectWorkspaceStyle.Well);
                using(var brush = new SolidBrush(status)) e.Graphics.FillEllipse(brush, 10, Height / 2 - 4, 8, 8);
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(24, 0, Width - 28, Height), Enabled ? ProjectWorkspaceStyle.Text : ProjectWorkspaceStyle.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
                return;
            }
            ProjectUiPaint.DrawAction(e.Graphics, this, kind, selected, hovered,
                keyPressed || (mousePressed && hovered), Focused && ShowFocusCues, ShowKeyboardCues);
        }

        protected override void OnMouseEnter(EventArgs e)
        { hovered = true; base.OnMouseEnter(e); RefreshAppearance(); }
        protected override void OnMouseLeave(EventArgs e)
        { hovered = false; base.OnMouseLeave(e); RefreshAppearance(); }
        protected override void OnMouseDown(MouseEventArgs e)
        { if (e.Button == MouseButtons.Left) mousePressed = true; base.OnMouseDown(e); RefreshAppearance(); }
        protected override void OnMouseUp(MouseEventArgs e)
        { if (e.Button == MouseButtons.Left) mousePressed = false; base.OnMouseUp(e); RefreshAppearance(); }
        protected override void OnMouseCaptureChanged(EventArgs e)
        { if (!Capture) mousePressed = false; base.OnMouseCaptureChanged(e); RefreshAppearance(); }
        protected override void OnKeyDown(KeyEventArgs e)
        { if (e.KeyCode == Keys.Space) keyPressed = true; base.OnKeyDown(e); RefreshAppearance(); }
        protected override void OnKeyUp(KeyEventArgs e)
        { if (e.KeyCode == Keys.Space) keyPressed = false; base.OnKeyUp(e); RefreshAppearance(); }
        protected override void OnGotFocus(EventArgs e)
        { base.OnGotFocus(e); RefreshAppearance(); }
        protected override void OnLostFocus(EventArgs e)
        { mousePressed = keyPressed = false; base.OnLostFocus(e); RefreshAppearance(); }
        protected override void OnEnabledChanged(EventArgs e)
        { mousePressed = keyPressed = false; base.OnEnabledChanged(e); RefreshAppearance(); }
    }

    // 标题属于容器自身，子控件直接加入 Controls；顶部留白由标题和说明自动计算。
    public class ProjectSectionPanel : Panel
    {
        private string title = string.Empty, caption = string.Empty;
        private Padding contentPadding = new Padding(12);

        public ProjectSectionPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = ProjectWorkspaceStyle.TextFont;
            BackColor = ProjectWorkspaceStyle.Group;
            ForeColor = ProjectWorkspaceStyle.Text;
            BorderStyle = BorderStyle.None;
            UpdateContentPadding();
        }

        [DefaultValue("")]
        public string Title
        {
            get { return title; }
            set { value = value ?? string.Empty; if (title != value) { title = value; UpdateContentPadding(); } }
        }

        [DefaultValue("")]
        public string Caption
        {
            get { return caption; }
            set { value = value ?? string.Empty; if (caption != value) { caption = value; UpdateContentPadding(); } }
        }

        // 使用 96 DPI 的逻辑尺寸；由控件转换为当前屏幕的物理尺寸。
        public Padding ContentPadding
        {
            get { return contentPadding; }
            set { if (contentPadding != value) { contentPadding = value; UpdateContentPadding(); } }
        }

        private int HeaderHeight
        {
            get { return title.Length > 0 || caption.Length > 0 ? 24 : 0; }
        }

        private void UpdateContentPadding()
        {
            var scale = Math.Max(1f, DeviceDpi / 96f);
            var value = new Padding(ProjectUiPaint.Pixels(contentPadding.Left, scale),
                ProjectUiPaint.Pixels(contentPadding.Top + HeaderHeight, scale),
                ProjectUiPaint.Pixels(contentPadding.Right, scale), ProjectUiPaint.Pixels(contentPadding.Bottom, scale));
            if (Padding != value) Padding = value;
            ProjectUiPaint.InvalidateVisible(this);
        }

        protected override void OnHandleCreated(EventArgs e)
        { base.OnHandleCreated(e); UpdateContentPadding(); }
        protected override void OnDpiChangedAfterParent(EventArgs e)
        { base.OnDpiChangedAfterParent(e); UpdateContentPadding(); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var background = new SolidBrush(Parent != null ? Parent.BackColor : ProjectWorkspaceStyle.Panel))
                e.Graphics.FillRectangle(background, ClientRectangle);
            var scale = Math.Max(e.Graphics.DpiX, DeviceDpi) / 96f;
            var bounds = new RectangleF(.5f, .5f, Math.Max(0, Width - 1f), Math.Max(0, Height - 1f));
            if (bounds.Width < 1 || bounds.Height < 1) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = ProjectUiPaint.RoundRect(bounds, 6f * scale))
            using (var fill = new SolidBrush(BackColor))
            using (var edge = new Pen(ProjectWorkspaceStyle.Edge))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(edge, path); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var scale = Math.Max(e.Graphics.DpiX, DeviceDpi) / 96f;
            var x = ProjectUiPaint.Pixels(contentPadding.Left, scale);
            var y = ProjectUiPaint.Pixels(contentPadding.Top, scale);
            var width = Math.Max(0, Width - x - ProjectUiPaint.Pixels(contentPadding.Right, scale));
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (title.Length > 0)
            {
                var titleWidth = Math.Min(width, TextRenderer.MeasureText(e.Graphics, title, ProjectWorkspaceStyle.SectionFont,
                    Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width);
                TextRenderer.DrawText(e.Graphics, title, ProjectWorkspaceStyle.SectionFont,
                    new Rectangle(x, y, titleWidth, ProjectUiPaint.Pixels(20, scale)), ForeColor, flags);
                x += titleWidth + ProjectUiPaint.Pixels(12, scale);
                width = Math.Max(0, width - titleWidth - ProjectUiPaint.Pixels(12, scale));
            }
            if (caption.Length > 0 && width > 0)
                TextRenderer.DrawText(e.Graphics, caption, Font,
                    new Rectangle(x, y, width, ProjectUiPaint.Pixels(20, scale)), ProjectWorkspaceStyle.Muted, flags | TextFormatFlags.Right);
        }
    }

    internal static class ProjectUiPaint
    {
        internal static int Pixels(int value, float scale) { return (int)Math.Round(value * scale); }

        internal static void InvalidateVisible(Control control)
        {
            if (!control.IsDisposed && control.IsHandleCreated && control.Visible) control.Invalidate();
        }

        internal static GraphicsPath RoundRect(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0) return path;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void DrawAction(Graphics graphics, Button button, ProjectActionKind kind,
            bool selected, bool hovered, bool pressed, bool focused, bool showMnemonic)
        {
            if (button.Width < 2 || button.Height < 2) return;
            var scale = Math.Max(graphics.DpiX, button.DeviceDpi) / 96f;
            var navigationSelected = kind == ProjectActionKind.Navigation && selected;
            var fill = kind == ProjectActionKind.Normal ? button.BackColor : ProjectWorkspaceStyle.ControlColor;
            var foreground = kind == ProjectActionKind.Normal ? button.ForeColor : ProjectWorkspaceStyle.Text;
            var edge = ProjectWorkspaceStyle.Edge;
            // EKF、振动等原按钮使用背景色表达实时状态；悬停与按下不得盖掉该状态。
            var statusColor = kind == ProjectActionKind.Normal && fill != ProjectWorkspaceStyle.ControlColor && fill != ProjectWorkspaceStyle.Panel && fill != SystemColors.Control;
            if (kind == ProjectActionKind.Primary)
            { fill = ProjectWorkspaceStyle.Cyan; foreground = ProjectWorkspaceStyle.Surface; edge = fill; }
            else if (kind == ProjectActionKind.Danger)
            { fill = Color.FromArgb(57, 36, 46); foreground = ProjectWorkspaceStyle.Danger; edge = Color.FromArgb(132, 65, 81); }
            else if (kind == ProjectActionKind.Navigation)
            {
                fill = navigationSelected ? Color.FromArgb(19, 57, 78) : ProjectWorkspaceStyle.Panel;
                foreground = navigationSelected ? ProjectWorkspaceStyle.Cyan : ProjectWorkspaceStyle.Muted;
                edge = navigationSelected ? Color.FromArgb(36, 102, 135) : ProjectWorkspaceStyle.Edge;
            }
            if (!button.Enabled)
            { fill = ProjectWorkspaceStyle.Group; foreground = ProjectWorkspaceStyle.Muted; edge = ProjectWorkspaceStyle.Edge; }
            else if (pressed)
            {
                if (!statusColor) fill = kind == ProjectActionKind.Primary ? Color.FromArgb(35, 146, 194) :
                    kind == ProjectActionKind.Danger ? Color.FromArgb(91, 41, 55) : Color.FromArgb(27, 65, 88);
            }
            else if (hovered)
            {
                if (!statusColor) fill = kind == ProjectActionKind.Primary ? Color.FromArgb(89, 200, 247) :
                    kind == ProjectActionKind.Danger ? Color.FromArgb(75, 39, 51) : Color.FromArgb(39, 65, 85);
                if (kind != ProjectActionKind.Primary && kind != ProjectActionKind.Danger) edge = Color.FromArgb(72, 109, 137);
            }

            using (var background = new SolidBrush(button.Parent != null ? button.Parent.BackColor : ProjectWorkspaceStyle.Panel))
                graphics.FillRectangle(background, button.ClientRectangle);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(.5f, .5f, button.Width - 1f, button.Height - 1f);
            using (var path = RoundRect(bounds, 5f * scale))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(edge))
            { graphics.FillPath(brush, path); graphics.DrawPath(pen, path); }

            if (navigationSelected && button.Enabled)
            {
                var inset = Pixels(10, scale);
                using (var marker = new SolidBrush(ProjectWorkspaceStyle.Cyan))
                    graphics.FillRectangle(marker, inset, button.Height - Pixels(4, scale), Math.Max(0, button.Width - inset * 2), Math.Max(2, Pixels(2, scale)));
            }

            var pad = Math.Max(Pixels(8, scale), button.Padding.Left);
            var textRect = Rectangle.FromLTRB(pad, Math.Max(2, button.Padding.Top),
                button.Width - Math.Max(Pixels(8, scale), button.Padding.Right), button.Height - Math.Max(2, button.Padding.Bottom));
            if (button.Image != null)
            {
                var imageSize = button.Image.Size;
                var imageRect = new Rectangle(textRect.Left, (button.Height - imageSize.Height) / 2, imageSize.Width, imageSize.Height);
                if (button.ImageAlign == ContentAlignment.MiddleRight) imageRect.X = textRect.Right - imageSize.Width;
                else if (button.ImageAlign == ContentAlignment.MiddleCenter) imageRect.X = (button.Width - imageSize.Width) / 2;
                if (button.Enabled) graphics.DrawImage(button.Image, imageRect);
                else ControlPaint.DrawImageDisabled(graphics, button.Image, imageRect.X, imageRect.Y, fill);
                if (button.TextImageRelation == TextImageRelation.ImageBeforeText) textRect.X += imageSize.Width + Pixels(4, scale);
                if (button.TextImageRelation == TextImageRelation.ImageBeforeText || button.TextImageRelation == TextImageRelation.TextBeforeImage)
                    textRect.Width = Math.Max(0, textRect.Width - imageSize.Width - Pixels(4, scale));
            }
            if (pressed) textRect.Offset(0, Math.Max(1, Pixels(1, scale)));
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (button.TextAlign == ContentAlignment.MiddleLeft || button.TextAlign == ContentAlignment.TopLeft || button.TextAlign == ContentAlignment.BottomLeft) flags |= TextFormatFlags.Left;
            else if (button.TextAlign == ContentAlignment.MiddleRight || button.TextAlign == ContentAlignment.TopRight || button.TextAlign == ContentAlignment.BottomRight) flags |= TextFormatFlags.Right;
            else flags |= TextFormatFlags.HorizontalCenter;
            if (!button.UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            else if (!showMnemonic) flags |= TextFormatFlags.HidePrefix;
            if (button.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(graphics, button.Text, button.Font, textRect, foreground, flags);
            if (focused && button.Enabled)
            {
                var focus = Rectangle.Inflate(button.ClientRectangle, -Pixels(4, scale), -Pixels(4, scale));
                if (focus.Width > 0 && focus.Height > 0) ControlPaint.DrawFocusRectangle(graphics, focus, foreground, fill);
            }
        }
    }

    // 仅附加到原窗口句柄，句柄重建时重新连接；不替换原控件或接管输入消息。
    internal abstract class ProjectNativeAdapter : NativeWindow, IDisposable
    {
        protected readonly Control Owner;
        protected ProjectNativeAdapter(Control owner)
        {
            Owner = owner;
            owner.HandleCreated += HandleCreated;
            owner.HandleDestroyed += HandleDestroyed;
            owner.Disposed += OwnerDisposed;
        }
        protected void Attach()
        {
            if (Owner.IsHandleCreated && Handle == IntPtr.Zero) { AssignHandle(Owner.Handle); OnAttached(); }
        }
        protected virtual void OnAttached() { }
        private void HandleCreated(object sender, EventArgs e) { Attach(); }
        private void HandleDestroyed(object sender, EventArgs e) { if (Handle != IntPtr.Zero) ReleaseHandle(); }
        private void OwnerDisposed(object sender, EventArgs e) { Dispose(); }
        public virtual void Dispose()
        {
            Owner.HandleCreated -= HandleCreated;
            Owner.HandleDestroyed -= HandleDestroyed;
            Owner.Disposed -= OwnerDisposed;
            if (Handle != IntPtr.Zero) ReleaseHandle();
        }
    }

    internal sealed class ProjectButtonAdapter : ProjectNativeAdapter
    {
        private readonly Button button;
        private bool hovered, mousePressed, keyPressed;
        internal ProjectActionKind Kind { get; private set; }
        internal bool Selected { get; private set; }

        internal ProjectButtonAdapter(Button button) : base(button)
        {
            this.button = button;
            button.MouseEnter += MouseEnter;
            button.MouseLeave += MouseLeave;
            button.MouseDown += MouseDown;
            button.MouseUp += MouseUp;
            button.MouseCaptureChanged += CaptureChanged;
            button.KeyDown += KeyDown;
            button.KeyUp += KeyUp;
            button.GotFocus += StateChanged;
            button.LostFocus += ResetPressed;
            button.EnabledChanged += ResetPressed;
            Attach();
        }

        internal void SetKind(ProjectActionKind kind, bool selected)
        {
            if (Kind == kind && Selected == selected) return;
            Kind = kind; Selected = selected;
            ProjectUiPaint.InvalidateVisible(button);
        }
        private void MouseEnter(object sender, EventArgs e) { hovered = true; StateChanged(sender, e); }
        private void MouseLeave(object sender, EventArgs e) { hovered = false; StateChanged(sender, e); }
        private void MouseDown(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { mousePressed = true; StateChanged(sender, e); } }
        private void MouseUp(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { mousePressed = false; StateChanged(sender, e); } }
        private void CaptureChanged(object sender, EventArgs e) { if (!button.Capture) { mousePressed = false; StateChanged(sender, e); } }
        private void KeyDown(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Space) { keyPressed = true; StateChanged(sender, e); } }
        private void KeyUp(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Space) { keyPressed = false; StateChanged(sender, e); } }
        private void ResetPressed(object sender, EventArgs e) { mousePressed = keyPressed = false; StateChanged(sender, e); }
        private void StateChanged(object sender, EventArgs e) { ProjectUiPaint.InvalidateVisible(button); }

        private void PaintButton(Graphics graphics)
        {
            ProjectUiPaint.DrawAction(graphics, button, Kind, Selected, hovered,
                keyPressed || (mousePressed && hovered), button.Focused, true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x000F && button.Width > 0 && button.Height > 0)
            {
                ProjectNativeMethods.PaintStruct paint;
                var hdc = ProjectNativeMethods.BeginPaint(Handle, out paint);
                try
                {
                    using (var graphics = Graphics.FromHdc(hdc))
                    using (var buffer = BufferedGraphicsManager.Current.Allocate(graphics, button.ClientRectangle))
                    { PaintButton(buffer.Graphics); buffer.Render(graphics); }
                }
                finally { ProjectNativeMethods.EndPaint(Handle, ref paint); }
                m.Result = IntPtr.Zero;
                return;
            }
            if ((m.Msg == 0x0317 || m.Msg == 0x0318) && m.WParam != IntPtr.Zero)
            {
                using (var graphics = Graphics.FromHdc(m.WParam)) PaintButton(graphics);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x0014) { m.Result = (IntPtr)1; return; }
            base.WndProc(ref m);
        }

        public override void Dispose()
        {
            button.MouseEnter -= MouseEnter;
            button.MouseLeave -= MouseLeave;
            button.MouseDown -= MouseDown;
            button.MouseUp -= MouseUp;
            button.MouseCaptureChanged -= CaptureChanged;
            button.KeyDown -= KeyDown;
            button.KeyUp -= KeyUp;
            button.GotFocus -= StateChanged;
            button.LostFocus -= ResetPressed;
            button.EnabledChanged -= ResetPressed;
            base.Dispose();
        }
    }

    internal sealed class ProjectComboAdapter : ProjectNativeAdapter
    {
        private readonly ComboBox combo;
        private readonly bool ownsItems;
        private bool hovered;

        internal ProjectComboAdapter(ComboBox combo) : base(combo)
        {
            this.combo = combo;
            // 原有自绘列表仍交给原处理器，只适配外边框及箭头。
            ownsItems = combo.DrawMode == DrawMode.Normal;
            if (ownsItems)
            {
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.DrawItem += DrawItem;
            }
            combo.MouseEnter += MouseEnter;
            combo.MouseLeave += MouseLeave;
            combo.GotFocus += StateChanged;
            combo.LostFocus += StateChanged;
            combo.DropDown += DropDownOpening;
            combo.DropDownClosed += StateChanged;
            combo.EnabledChanged += StateChanged;
            combo.FontChanged += FontChanged;
            combo.DpiChangedAfterParent += FontChanged;
            ApplyAppearance();
            Attach();
        }

        internal void ApplyAppearance()
        {
            // 关闭 WinForms 的扁平边框补绘，边框统一由附加绘制完成。
            if (combo.FlatStyle != FlatStyle.Standard) combo.FlatStyle = FlatStyle.Standard;
            if (combo.BackColor != ProjectWorkspaceStyle.ControlColor) combo.BackColor = ProjectWorkspaceStyle.ControlColor;
            if (combo.ForeColor != ProjectWorkspaceStyle.Text) combo.ForeColor = ProjectWorkspaceStyle.Text;
            if (ownsItems)
            {
                var height = combo.Font.Height + ProjectUiPaint.Pixels(8, combo.DeviceDpi / 96f);
                if (combo.ItemHeight != height) combo.ItemHeight = height;
            }
            if (combo.IsHandleCreated) RemoveEditFrame();
        }

        protected override void OnAttached() { RemoveEditFrame(); }

        private void RemoveEditFrame()
        {
            if (combo.DropDownStyle != ComboBoxStyle.DropDown) return;
            var info = new ProjectNativeMethods.ComboBoxInfo { Size = Marshal.SizeOf(typeof(ProjectNativeMethods.ComboBoxInfo)) };
            if (!ProjectNativeMethods.GetComboBoxInfo(combo.Handle, ref info) || info.Edit == IntPtr.Zero) return;
            // 仅移除原编辑子窗的三维边缘，光标、选区、输入法和自动完成仍由原窗口处理。
            var style = ProjectNativeMethods.GetWindowLong(info.Edit, -16);
            var extended = ProjectNativeMethods.GetWindowLong(info.Edit, -20);
            var flatStyle = style & ~0x00800000;
            var flatExtended = extended & ~0x00000200;
            if (style == flatStyle && extended == flatExtended) return;
            ProjectNativeMethods.SetWindowLong(info.Edit, -16, flatStyle);
            ProjectNativeMethods.SetWindowLong(info.Edit, -20, flatExtended);
            ProjectNativeMethods.SetWindowPos(info.Edit, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        }

        private void FontChanged(object sender, EventArgs e) { ApplyAppearance(); }
        private void DropDownOpening(object sender, EventArgs e)
        {
            // 只在展开时计算，长动作名称可完整阅读，输入框及原选择保持原样。
            var limit = Math.Max(combo.Width, Screen.FromControl(combo).WorkingArea.Width - ProjectUiPaint.Pixels(16, combo.DeviceDpi / 96f));
            var width = Math.Min(limit, Math.Max(combo.Width, combo.DropDownWidth));
            var extra = SystemInformation.VerticalScrollBarWidth + ProjectUiPaint.Pixels(16, combo.DeviceDpi / 96f);
            using (var graphics = combo.CreateGraphics())
            {
                foreach (var item in combo.Items)
                {
                    var measured = TextRenderer.MeasureText(graphics, combo.GetItemText(item), combo.Font, Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + extra;
                    width = Math.Min(limit, Math.Max(width, measured));
                    if (width >= limit) break;
                }
            }
            if (combo.DropDownWidth != width) combo.DropDownWidth = width;
            StateChanged(sender, e);
        }
        private void MouseEnter(object sender, EventArgs e) { hovered = true; StateChanged(sender, e); }
        private void MouseLeave(object sender, EventArgs e) { hovered = false; StateChanged(sender, e); }
        private void StateChanged(object sender, EventArgs e) { ProjectUiPaint.InvalidateVisible(combo); }

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
            var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            var fill = selected ? Color.FromArgb(24, 65, 90) : ProjectWorkspaceStyle.ControlColor;
            using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, e.Bounds);
            var value = e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
            var bounds = Rectangle.Inflate(e.Bounds, -ProjectUiPaint.Pixels(6, e.Graphics.DpiX / 96f), 0);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (combo.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
            TextRenderer.DrawText(e.Graphics, value, e.Font ?? combo.Font, bounds, combo.Enabled ? ProjectWorkspaceStyle.Text : ProjectWorkspaceStyle.Muted, flags);
            if ((e.State & DrawItemState.Focus) != 0 && selected)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), ProjectWorkspaceStyle.Cyan, fill);
        }

        private void PaintChrome(Graphics graphics)
        {
            if (combo.Width < 2 || combo.Height < 2 || combo.DropDownStyle == ComboBoxStyle.Simple) return;
            var scale = Math.Max(graphics.DpiX, combo.DeviceDpi) / 96f;
            var border = combo.Enabled && (combo.ContainsFocus || combo.DroppedDown) ? ProjectWorkspaceStyle.Cyan :
                hovered && combo.Enabled ? Color.FromArgb(72, 109, 137) : ProjectWorkspaceStyle.Edge;
            var arrowWidth = ProjectUiPaint.Pixels(28, scale);
            var arrow = new Rectangle(combo.Width - arrowWidth - 1, 1, arrowWidth, combo.Height - 2);
            if (combo.RightToLeft == RightToLeft.Yes) arrow.X = 1;
            var info = new ProjectNativeMethods.ComboBoxInfo { Size = Marshal.SizeOf(typeof(ProjectNativeMethods.ComboBoxInfo)) };
            if (ProjectNativeMethods.GetComboBoxInfo(combo.Handle, ref info))
            {
                var nativeArrow = Rectangle.FromLTRB(info.Button.Left, info.Button.Top, info.Button.Right, info.Button.Bottom);
                if (nativeArrow.Width > 0 && combo.ClientRectangle.Contains(nativeArrow)) arrow = nativeArrow;
            }
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(.5f, .5f, combo.Width - 1f, combo.Height - 1f);
            using (var path = ProjectUiPaint.RoundRect(bounds, 4f * scale))
            {
                if (ownsItems && combo.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    // 底色必须实心覆盖；抗锯齿矩形会把旧边线混入首行和首列像素。
                    graphics.SmoothingMode = SmoothingMode.None;
                    using (var background = new SolidBrush(combo.Parent != null ? combo.Parent.BackColor : ProjectWorkspaceStyle.Panel))
                        graphics.FillRectangle(background, combo.ClientRectangle);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var fill = new SolidBrush(ProjectWorkspaceStyle.ControlColor)) graphics.FillPath(fill, path);
                    var textBounds = Rectangle.FromLTRB(ProjectUiPaint.Pixels(8, scale), 1, Math.Max(1, arrow.Left - ProjectUiPaint.Pixels(4, scale)), combo.Height - 1);
                    var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                    if (combo.RightToLeft == RightToLeft.Yes)
                    {
                        textBounds = Rectangle.FromLTRB(arrow.Right + ProjectUiPaint.Pixels(4, scale), 1, combo.Width - ProjectUiPaint.Pixels(8, scale), combo.Height - 1);
                        flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
                    }
                    TextRenderer.DrawText(graphics, combo.Text, combo.Font, textBounds,
                        combo.Enabled ? ProjectWorkspaceStyle.Text : ProjectWorkspaceStyle.Muted, flags);
                }
                if (combo.DropDownStyle == ComboBoxStyle.DropDown)
                {
                    // 覆盖原生边框的全部厚度，止于编辑区之前，保留原输入窗口。
                    var rim = Math.Max(3, (int)Math.Ceiling(2 * scale) + 1);
                    graphics.SmoothingMode = SmoothingMode.None;
                    using (var fill = new SolidBrush(ProjectWorkspaceStyle.ControlColor))
                    {
                        graphics.FillRectangle(fill, 0, 0, combo.Width, rim);
                        graphics.FillRectangle(fill, 0, combo.Height - rim, combo.Width, rim);
                        graphics.FillRectangle(fill, 0, 0, rim, combo.Height);
                        graphics.FillRectangle(fill, combo.Width - rim, 0, rim, combo.Height);
                    }
                    if (combo.RightToLeft != RightToLeft.Yes) { arrow.X -= rim; arrow.Width += rim; }
                    else arrow.Width += rim;
                }
                using (var fill = new SolidBrush(combo.Enabled && (hovered || combo.DroppedDown) ? Color.FromArgb(39, 65, 85) : ProjectWorkspaceStyle.ControlColor))
                    graphics.FillRectangle(fill, arrow);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var edge = new Pen(border)) graphics.DrawPath(edge, path);
            }
            var centerX = arrow.Left + arrow.Width / 2f;
            var centerY = arrow.Top + arrow.Height / 2f;
            using (var pen = new Pen(combo.Enabled ? ProjectWorkspaceStyle.Text : ProjectWorkspaceStyle.Muted, Math.Max(1.5f, scale)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                graphics.DrawLines(pen, new[] { new PointF(centerX - 4f * scale, centerY - 2f * scale), new PointF(centerX, centerY + 2f * scale), new PointF(centerX + 4f * scale, centerY - 2f * scale) });
            }
        }

        private void PaintNonClientFrame()
        {
            if (!combo.IsHandleCreated || combo.DropDownStyle == ComboBoxStyle.Simple) return;
            ProjectNativeMethods.NativeRect window, client;
            if (!ProjectNativeMethods.GetWindowRect(combo.Handle, out window) || !ProjectNativeMethods.GetClientRect(combo.Handle, out client)) return;
            var origin = new ProjectNativeMethods.NativePoint();
            ProjectNativeMethods.ClientToScreen(combo.Handle, ref origin);
            var left = origin.X - window.Left;
            var top = origin.Y - window.Top;
            var width = window.Right - window.Left;
            var height = window.Bottom - window.Top;
            var right = left + client.Right;
            var bottom = top + client.Bottom;
            if (width < 2 || height < 2 || (left == 0 && top == 0 && right == width && bottom == height)) return;
            var hdc = ProjectNativeMethods.GetWindowDC(combo.Handle);
            if (hdc == IntPtr.Zero) return;
            try
            {
                using (var graphics = Graphics.FromHdc(hdc))
                using (var fill = new SolidBrush(ProjectWorkspaceStyle.ControlColor))
                {
                    graphics.FillRectangle(fill, 0, 0, width, top);
                    graphics.FillRectangle(fill, 0, bottom, width, Math.Max(0, height - bottom));
                    graphics.FillRectangle(fill, 0, top, left, Math.Max(0, bottom - top));
                    graphics.FillRectangle(fill, right, top, Math.Max(0, width - right), Math.Max(0, bottom - top));
                    var border = combo.Enabled && (combo.ContainsFocus || combo.DroppedDown) ? ProjectWorkspaceStyle.Cyan : ProjectWorkspaceStyle.Edge;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = ProjectUiPaint.RoundRect(new RectangleF(.5f, .5f, width - 1, height - 1), 4f * combo.DeviceDpi / 96f))
                    using (var edge = new Pen(border)) graphics.DrawPath(edge, path);
                }
            }
            finally { ProjectNativeMethods.ReleaseDC(combo.Handle, hdc); }
        }

        protected override void WndProc(ref Message m)
        {
            var message = m.Msg;
            base.WndProc(ref m);
            if (message == 0x000F && combo.IsHandleCreated)
            { using (var graphics = Graphics.FromHwnd(combo.Handle)) PaintChrome(graphics); PaintNonClientFrame(); }
            else if (message == 0x0085) PaintNonClientFrame();
            else if ((message == 0x0317 || message == 0x0318) && m.WParam != IntPtr.Zero)
            { using (var graphics = Graphics.FromHdc(m.WParam)) PaintChrome(graphics); }
        }

        public override void Dispose()
        {
            if (ownsItems) combo.DrawItem -= DrawItem;
            combo.MouseEnter -= MouseEnter;
            combo.MouseLeave -= MouseLeave;
            combo.GotFocus -= StateChanged;
            combo.LostFocus -= StateChanged;
            combo.DropDown -= DropDownOpening;
            combo.DropDownClosed -= StateChanged;
            combo.EnabledChanged -= StateChanged;
            combo.FontChanged -= FontChanged;
            combo.DpiChangedAfterParent -= FontChanged;
            base.Dispose();
        }
    }

    internal static class ProjectNativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativePoint { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct PaintStruct
        {
            internal IntPtr Hdc;
            internal int Erase;
            internal NativeRect Paint;
            internal int Restore, IncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] internal byte[] Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct ComboBoxInfo
        {
            internal int Size;
            internal NativeRect Item, Button;
            internal uint ButtonState;
            internal IntPtr Combo, Edit, List;
        }
        [DllImport("user32.dll")] internal static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paint);
        [DllImport("user32.dll")] internal static extern bool EndPaint(IntPtr window, ref PaintStruct paint);
        [DllImport("user32.dll")] internal static extern bool GetComboBoxInfo(IntPtr window, ref ComboBoxInfo info);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowDC(IntPtr window);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
    }
}
