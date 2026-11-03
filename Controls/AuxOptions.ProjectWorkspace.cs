using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    public partial class AuxOptions
    {
        public void ConfigureProjectWorkspace()
        {
            Height = 146; MinimumSize = new Size(240, 146); Margin = Padding.Empty;
            TXT_rcchannel.Visible = true; TXT_rcchannel.Text = "等待命令回报";
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(0, 4, 0, 0) };
            for (var i = 0; i < 3; i++) { layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 1 ? 40 : 34)); }
            layout.Controls.Add(mavlinkComboBox1, 0, 0); layout.SetColumnSpan(mavlinkComboBox1, 3);
            layout.Controls.Add(BUT_Low, 0, 1); layout.Controls.Add(but_mid, 1, 1); layout.Controls.Add(BUT_High, 2, 1);
            layout.Controls.Add(TXT_low_value, 0, 2); layout.Controls.Add(txt_midvalue, 1, 2); layout.Controls.Add(TXT_highvalue, 2, 2);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); layout.Controls.Add(TXT_rcchannel, 0, 3); layout.SetColumnSpan(TXT_rcchannel, 3);
            foreach (Control control in layout.Controls) { control.Dock = DockStyle.Fill; control.Margin = new Padding(3, 4, 3, 4); }
            Controls.Add(layout); layout.BringToFront(); ProjectWorkspaceStyle.Apply(this);
        }
    }
}
