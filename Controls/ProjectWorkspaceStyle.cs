using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    public sealed class ProjectPages : TabControl
    {
        protected override void WndProc(ref Message m) { if (m.Msg == 0x1328 && !DesignMode) { m.Result = (IntPtr)1; return; } base.WndProc(ref m); }
    }

    // 工作台局部视觉样式；不改变遥测绑定、按钮事件或业务状态。
    public static class ProjectWorkspaceStyle
    {
        public static readonly Color Surface = ColorTranslator.FromHtml("#0B111B"), Panel = ColorTranslator.FromHtml("#111D2A"),
            Well = ColorTranslator.FromHtml("#0B1520"), Group = ColorTranslator.FromHtml("#182737"), ControlColor = ColorTranslator.FromHtml("#203246"),
            Text = ColorTranslator.FromHtml("#E6EEF6"), Muted = ColorTranslator.FromHtml("#91A8BB"), Edge = ColorTranslator.FromHtml("#2B4053"),
            Cyan = ColorTranslator.FromHtml("#35B9F2"), Success = ColorTranslator.FromHtml("#34D3A0"), Warning = ColorTranslator.FromHtml("#F2B84B"), Danger = ColorTranslator.FromHtml("#F06470");
        public static readonly Font TextFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        internal static readonly Font SectionFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        private static readonly Font HeadingFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point),
            TitleFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
        private sealed class AppliedState { internal bool Applied; }
        private static readonly ConditionalWeakTable<Control, AppliedState> AppliedControls = new ConditionalWeakTable<Control, AppliedState>();
        private static readonly ConditionalWeakTable<Button, ProjectButtonAdapter> Buttons = new ConditionalWeakTable<Button, ProjectButtonAdapter>();
        private static readonly ConditionalWeakTable<ComboBox, ProjectComboAdapter> Combos = new ConditionalWeakTable<ComboBox, ProjectComboAdapter>();

        public static void Apply(Control control)
        {
            if (control == null || control.IsDisposed || control is HUD || control.GetType().Name.Contains("GMap")) return;
            var state = AppliedControls.GetValue(control, item => new AppliedState());
            var firstApplication = !state.Applied;
            state.Applied = true;
            if (control is Form window)
            {
                if (firstApplication) window.HandleCreated += (sender, args) => ProjectWindowChrome.Apply(window);
                ProjectWindowChrome.Apply(window);
            }


            // AUX 默认灰色仅表示等待；实际红、橙、绿回报及后续状态由原业务保留。
            if (firstApplication && control.Name == "TXT_rcchannel" && control.Text == "等待命令回报" &&
                control.BackColor.ToArgb() == Color.Gray.ToArgb())
            {
                control.BackColor = control.Parent != null ? control.Parent.BackColor : Panel;
                control.ForeColor = Muted;
            }

            // 初次设置通用缺省值；后续应用保留页面设置的字号、状态色和语义组件状态。
            if (firstApplication && !(control is ProjectActionButton) && !(control is ProjectSectionPanel))
            {
                var projectLabel = control is Label && control.Name.StartsWith("Project", StringComparison.Ordinal);
                if (!projectLabel || control.Font.Equals(Control.DefaultFont)) control.Font = TextFont;
                if (!IsBusinessFeedback(control)) control.BackColor = control.Parent != null && control.Parent.BackColor == Group ? Group : Panel;
                if ((!projectLabel && !IsBusinessFeedback(control)) || control.ForeColor == SystemColors.ControlText) control.ForeColor = Text;
                if (control.Name == "ProjectHeading") { control.Font = HeadingFont; control.ForeColor = Text; }
                if (control.Name == "ProjectRegionTitle") { control.Font = TitleFont; control.ForeColor = Text; }
                if (control is Panel panel) panel.BorderStyle = BorderStyle.None;
            }

            // 原主题在工具窗口重开时会再次进入；修复普通外观，但保留业务反馈和已经明确设置的项目色。
            if (!IsBusinessFeedback(control))
            {
                if (control is ProjectSectionPanel)
                { control.BackColor = Group; control.ForeColor = Text; }
                else if (control is Panel || control is ContainerControl || control is TabControl || control is TabPage || control is GroupBox || control is SplitContainer)
                {
                    if (!IsProjectBackground(control.BackColor)) control.BackColor = control.Parent != null && control.Parent.BackColor == Group ? Group : Panel;
                    if (!IsProjectForeground(control.ForeColor)) control.ForeColor = Text;
                }
                else if (control is Label)
                {
                    if (!IsProjectBackground(control.BackColor) && control.BackColor != Color.Transparent)
                        control.BackColor = control.Parent != null ? control.Parent.BackColor : Panel;
                    if (!IsProjectForeground(control.ForeColor)) control.ForeColor = Text;
                }
                else if (control is Button)
                {
                    if (!IsProjectBackground(control.BackColor)) control.BackColor = ControlColor;
                    if (!IsProjectForeground(control.ForeColor)) control.ForeColor = Text;
                }
            }

            if (control is ProjectActionButton projectButton)
            {
                // 主要动作和导航选中态由调用方持有，递归换肤不得重置。
                if (firstApplication && projectButton.Kind == ProjectActionKind.Normal && IsPrimaryName(control.Name))
                    projectButton.Kind = ProjectActionKind.Primary;
            }
            else if (control is Button button)
            {
                var adapter = Buttons.GetValue(button, item => new ProjectButtonAdapter(item));
                if (firstApplication)
                {
                    var minHeight = (int)Math.Round(32 * button.DeviceDpi / 96f);
                    button.MinimumSize = new Size(button.MinimumSize.Width, Math.Max(minHeight, button.MinimumSize.Height));
                    button.UseVisualStyleBackColor = false;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                    if (!IsBusinessFeedback(button) || button.BackColor == SystemColors.Control || button.BackColor == Panel) button.BackColor = ControlColor;
                    if (IsPrimaryName(control.Name) && adapter.Kind == ProjectActionKind.Normal) adapter.SetKind(ProjectActionKind.Primary, false);
                }
            }
            if (control is ComboBox combo) Combos.GetValue(combo, item => new ProjectComboAdapter(item)).ApplyAppearance();

            if (!IsBusinessFeedback(control))
            {
                if (control is TextBoxBase || control is NumericUpDown || control is ListView || control is TreeView) control.BackColor = Well;
                if (control is TextBoxBase || control is NumericUpDown || control is ListView || control is TreeView) control.ForeColor = Text;
                if (control is TextBoxBase input) input.BorderStyle = BorderStyle.FixedSingle;
                if (control is LinkLabel link) { link.LinkColor = Cyan; link.ActiveLinkColor = Cyan; link.VisitedLinkColor = Cyan; }
                if (control is QuickView quick) { quick.BackColor = Well; quick.numberColor = Text; quick.numberColorBackup = Text; quick.CompactDisplay = true; quick.ProjectTelemetryStyle = true; quick.ProjectMutedColor = Muted; quick.ProjectEdgeColor = Edge; quick.ProjectAccentColor = Cyan; }
                if (control is DataGridView grid)
                {
                    grid.EnableHeadersVisualStyles = false; grid.BackgroundColor = Well; grid.BorderStyle = BorderStyle.None;
                    grid.DefaultCellStyle.BackColor = Panel; grid.DefaultCellStyle.ForeColor = Text;
                    grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(24, 65, 90); grid.DefaultCellStyle.SelectionForeColor = Text;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = ControlColor; grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                    grid.RowHeadersDefaultCellStyle.BackColor = ControlColor; grid.RowHeadersDefaultCellStyle.ForeColor = Text;
                    grid.AlternatingRowsDefaultCellStyle.BackColor = Panel; grid.AlternatingRowsDefaultCellStyle.ForeColor = Text;
                    grid.RowsDefaultCellStyle.BackColor = Panel; grid.RowsDefaultCellStyle.ForeColor = Text;
                    foreach (DataGridViewColumn column in grid.Columns) { column.DefaultCellStyle.BackColor = Panel; column.DefaultCellStyle.ForeColor = Text; }
                    grid.GridColor = Edge; grid.RowTemplate.Height = (int)Math.Round(28 * grid.DeviceDpi / 96f);
                }
                if (control is ZedGraph.ZedGraphControl graph)
                {
                    var pane = graph.GraphPane; pane.Fill = new ZedGraph.Fill(Panel); pane.Chart.Fill = new ZedGraph.Fill(Well);
                    pane.Title.FontSpec.FontColor = Text; pane.Legend.Fill = new ZedGraph.Fill(Panel); pane.Legend.FontSpec.FontColor = Text;
                    pane.XAxis.Color = pane.YAxis.Color = Edge; pane.XAxis.Scale.FontSpec.FontColor = pane.YAxis.Scale.FontSpec.FontColor = Muted;
                    pane.XAxis.Title.FontSpec.FontColor = pane.YAxis.Title.FontSpec.FontColor = Muted;
                }
            }
            foreach (Control child in control.Controls) Apply(child);
            if (control is ProjectTelemetryCell) { control.BackColor = Well; foreach (Control child in control.Controls) child.BackColor = Well; }
            if (control is ProjectEcuMonitorPanel ecu) ecu.StyleValues();
        }

        private static bool IsPrimaryName(string name) { return name == "ProjectPrimaryAction" || name == "ProjectConnectionAction"; }

        private static bool IsBusinessFeedback(Control control)
        {
            var name = control.Name ?? string.Empty;
            return name == "TXT_rcchannel" || name == "ProjectEKF" || name == "ProjectVIBE" ||
                name.StartsWith("ProjectStatus", StringComparison.Ordinal) || name.StartsWith("ProjectFeedback", StringComparison.Ordinal);
        }
        private static bool IsProjectBackground(Color color)
        { return color == Surface || color == Panel || color == Group || color == Well || color == ControlColor; }
        private static bool IsProjectForeground(Color color)
        { return color == Text || color == Muted || color == Cyan || color == Success || color == Warning || color == Danger; }

        public static void StyleAction(Button button, ProjectActionKind kind, bool selected = false)
        {
            if (button == null || button.IsDisposed) return;
            if (button is ProjectActionButton projectButton)
            { projectButton.Kind = kind; projectButton.Selected = kind == ProjectActionKind.Navigation && selected; }
            else Buttons.GetValue(button, item => new ProjectButtonAdapter(item)).SetKind(kind, kind == ProjectActionKind.Navigation && selected);
        }

        public static void Accent(Button button, bool selected)
        {
            if (button == null || button.IsDisposed) return;
            var projectButton = button as ProjectActionButton;
            ProjectButtonAdapter adapter;
            var navigation = projectButton != null ? projectButton.Kind == ProjectActionKind.Navigation :
                Buttons.TryGetValue(button, out adapter) && adapter.Kind == ProjectActionKind.Navigation;
            if (navigation) StyleAction(button, ProjectActionKind.Navigation, selected);
            else StyleAction(button, selected ? ProjectActionKind.Primary : ProjectActionKind.Normal);
        }

        public static Label Heading(string text, int height = 32) => new Label { Name = "ProjectHeading", Text = text, Dock = DockStyle.Top, Height = height, TextAlign = ContentAlignment.MiddleLeft, Font = HeadingFont, ForeColor = Text, Padding = new Padding(4, 0, 0, 0) };

        public static bool Confirm(IWin32Window owner, string action, string detail)
        {
            using (var dialog = new Form { Text = "操作确认", Name = "ProjectConfirmation", ClientSize = new Size(460, 190), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false })
            {
                var content = new Label { Text = "确认执行“" + action + "”？\n\n" + detail, Dock = DockStyle.Fill, Padding = new Padding(24), AutoSize = false };
                var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Size = new Size(100, 34) };
                var confirm = new Button { Text = "确认" + action, DialogResult = DialogResult.Yes, Size = new Size(120, 34) };
                row.Controls.Add(cancel); row.Controls.Add(confirm); dialog.Controls.Add(content); dialog.Controls.Add(row);
                dialog.AcceptButton = cancel; dialog.CancelButton = cancel; Apply(dialog); Accent(confirm, true);
                dialog.Shown += (s, e) => cancel.Focus();
                return dialog.ShowDialog(owner) == DialogResult.Yes;
            }
        }
    }
}
