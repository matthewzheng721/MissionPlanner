using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    public sealed class ProjectPreflightWizard : Form
    {
        private readonly ComboBox group = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly Label heading = new Label { Name = "ProjectRegionTitle", AutoSize = true, Margin = new Padding(3, 10, 3, 6) };
        private readonly Label readings = new Label { AutoSize = true, MaximumSize = new Size(810, 0) };
        private readonly CheckBox confirmed = new CheckBox { Text = "操作员已核对本项", AutoSize = true };
        private readonly Label summary = new Label { AutoSize = true, MaximumSize = new Size(810, 0) };
        private readonly Panel embedded = new Panel { Dock = DockStyle.Fill, MinimumSize = new Size(660, 310) };
        private readonly Timer timer = new Timer { Interval = 500 };
        private int index;
        private bool rendering;
        private ProjectEcuStatus ecu = new ProjectEcuStatus();
        private object ecuConnection;
        private int ecuSystem, ecuComponent;
        private Button skipButton, nextButton;
        private Button platformTab, takeoffTab;
        public ProjectPreflightWizard()
        {
            Text = "飞前检查"; var available = Screen.PrimaryScreen.WorkingArea; MinimumSize = new Size(740, 620); Size = new Size(Math.Min(900, available.Width - 40), Math.Min(820, available.Height - 40)); StartPosition = FormStartPosition.CenterParent;
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, AutoScroll = true, Padding = new Padding(16) };
            for (var row = 0; row < 7; row++) body.RowStyles.Add(new RowStyle(row == 3 ? SizeType.Percent : SizeType.AutoSize, row == 3 ? 100 : 0));
            group.Items.AddRange(new object[] { "平台自检", "起飞条件" });
            var groupTabs = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            platformTab = AddButton(groupTabs, "平台自检", () => group.SelectedIndex = 0);
            takeoffTab = AddButton(groupTabs, "起飞条件", () => group.SelectedIndex = 1);
            platformTab.MinimumSize = takeoffTab.MinimumSize = new Size(130, 36);
            var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, Height = 50, ColumnCount = 4 };
            for (var col = 0; col < 4; col++) navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            AddButton(navigation, "上一步", () => { index = Math.Max(Start, index - 1); Render(); });
            skipButton = AddButton(navigation, "跳过", () => { ProjectPreflight.Checklist.Set(index, ProjectCheckResult.Skipped); Next(); });
            nextButton = AddButton(navigation, "下一步", () => { if (index > End) Close(); else Next(); });
            nextButton.Name = "ProjectPrimaryAction";
            AddButton(navigation, "关闭", Close);
            var controls = new Control[] { groupTabs, heading, readings, embedded, confirmed, navigation, summary };
            for (var row = 0; row < controls.Length; row++) body.Controls.Add(controls[row], 0, row);
            Controls.Add(body);
            Resize += (s, e) => { var width = Math.Max(640, ClientSize.Width - 50); readings.MaximumSize = summary.MaximumSize = new Size(width, 0); };
            group.SelectedIndexChanged += (s, e) => { index = Start; Render(); };
            confirmed.CheckedChanged += (s, e) => { if (!rendering) ProjectPreflight.Checklist.Set(index, confirmed.Checked ? ProjectCheckResult.Confirmed : ProjectCheckResult.Unchecked); RefreshReadings(); };
            timer.Tick += (s, e) => RefreshReadings();
            group.SelectedIndex = 0; timer.Start();
        }
        private int Start => group.SelectedIndex == 0 ? 0 : 5;
        private int End => group.SelectedIndex == 0 ? 4 : 7;
        private void Next() { index = Math.Min(End + 1, index + 1); Render(); }
        private static Button AddButton(Control parent, string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(90, 34), Margin = new Padding(4, 5, 4, 5) }; if (parent is TableLayoutPanel) button.Dock = DockStyle.Fill; button.Click += (s, e) => click(); parent.Controls.Add(button); return button;
        }
        private void Render()
        {
            foreach (Control control in embedded.Controls.Cast<Control>().ToArray()) control.Dispose();
            if (index <= End && index == 3) embedded.Controls.Add(new ProjectServoTestPanel { Dock = DockStyle.Fill });
            if (index <= End && index == 4) embedded.Controls.Add(new ProjectPolarityPanel { Dock = DockStyle.Fill });
            // 动态嵌入后补用现有主题，避免表格继承白字却仍使用默认白底。
            if (embedded.Controls.Count > 0) ThemeManager.ApplyThemeTo(embedded);
            embedded.Visible = index <= End && (index == 3 || index == 4);
            confirmed.Visible = index <= End && index != 4; skipButton.Visible = index <= End;
            nextButton.Text = index > End ? "完成" : "下一步";
            heading.Text = index > End ? "检查记录" : (index - Start + 1) + " / " + (End - Start + 1) + "  " + ProjectPreflightChecklist.Titles[index];
            RefreshReadings();
            ProjectCheckStyles.Apply(this);
            ProjectWorkspaceStyle.Accent(platformTab, group.SelectedIndex == 0); ProjectWorkspaceStyle.Accent(takeoffTab, group.SelectedIndex == 1);
        }
        private void RefreshReadings()
        {
            ProjectPreflight.RefreshConnection();
            var checks = ProjectPreflight.Checklist;
            rendering = true; confirmed.Checked = index <= End && checks[index] == ProjectCheckResult.Confirmed; confirmed.Enabled = checks.Connected; rendering = false;
            summary.Text = checks.CanArm ? "两组人工核对已完成；解锁仍由飞控预解锁检查决定，不会自动解锁。" : "未完成：" + checks.Missing;
            if (index > End) { readings.Text = string.Join("\n", Enumerable.Range(Start, End - Start + 1).Select(i => ProjectPreflightChecklist.Titles[i] + "：" + (checks[i] == ProjectCheckResult.Confirmed ? "已核对" : checks[i] == ProjectCheckResult.Skipped ? "已跳过" : "未核对"))); return; }
            if (!checks.Connected) { ecu = new ProjectEcuStatus(); readings.Text = "未连接飞控；不使用离线缓存读数确认本项。"; return; }
            var port = MainV2.comPort;
            if (!ReferenceEquals(ecuConnection, port.BaseStream) || ecuSystem != port.MAV.sysid || ecuComponent != port.MAV.compid) { ecu = new ProjectEcuStatus(); ecuConnection = port.BaseStream; ecuSystem = port.MAV.sysid; ecuComponent = port.MAV.compid; }
            var cs = port.MAV.cs;
            try { foreach (var message in cs.messages.ToArray()) ecu.Observe(message.message, message.time); }
            catch (InvalidOperationException) { /* 接收线程更新列表时，下次刷新再读取。 */ }
            switch (index)
            {
                case 0: readings.Text = $"电池 1：{cs.battery_voltage:F2} V　电池 2：{cs.battery_voltage2:F2} V\n飞控模式：{cs.mode}　解锁：{cs.armed}\nGPS 定位类型：{cs.gpsstatus}　卫星数：{cs.satcount}\n航向角：{cs.yaw:F1}°\n电池物理用途尚待核对；数值为 MP 当前状态，不自动判通过。"; break;
                case 1: readings.Text = ecu.ThrottleText + "\n" + ecu.TemperatureText + "\n请留意反馈时间；valid=1 由操作员核对，不自动勾选。"; break;
                case 2: readings.Text = $"当前空速：{cs.airspeed:F2}（MP 当前单位）\n请现场检查空速响应；自动通过标准尚未配置。"; break;
                case 3: readings.Text = "SDT v3 控制 SERVO1～4；停止后回各路 TRIM 保持 1 秒再释放。"; break;
                case 4: readings.Text = "分别核对稳定纠偏与主动操纵；两组都已核对才完成本步骤。"; break;
                case 5: readings.Text = $"EKF：{cs.ekfstatus}\nVIBE X / Y / Z：{cs.vibex:F2} / {cs.vibey:F2} / {cs.vibez:F2}\n结合飞控消息人工核对。"; break;
                case 6: readings.Text = ecu.TemperatureText + "\n起飞前四缸均须低于 50 ℃；请核对反馈时间及现场状态。"; break;
                default: readings.Text = "请查看飞控消息与当前状态，人工核对预解锁条件。没有消息不代表检查通过。"; break;
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
