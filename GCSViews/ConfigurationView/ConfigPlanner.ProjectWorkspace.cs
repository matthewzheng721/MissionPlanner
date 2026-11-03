using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    public partial class ConfigPlanner
    {
        public void ConfigureProjectWorkspace()
        {
            // 只重排原控件；事件、即时保存和 Activate 的 startup 保护继续沿用。
            var rows = new Control[][]
            {
                new Control[] { label102, CMB_rateattitude }, new Control[] { label103, CMB_rateposition },
                new Control[] { label104, CMB_ratestatus }, new Control[] { label107, CMB_raterc },
                new Control[] { label33, CMB_ratesensors }, new Control[] { label94, CMB_osdcolor },
                new Control[] { CHK_hudshow }, new Control[] { label98, CMB_speedunits },
                new Control[] { label97, CMB_distunits }, new Control[] { label6, CMB_altunits }, new Control[] { label99 },
                new Control[] { label23, NUM_tracklength }, new Control[] { CHK_maprotation },
                new Control[] { label2, CHK_disttohomeflightdata }, new Control[] { label3, txt_log_dir },
                new Control[] { BUT_logdirbrowse }, new Control[] { label13, CMB_mapCache },
                new Control[] { BUT_mapCacheDir }, new Control[] { CHK_showairports }, new Control[] { chk_ADSB },
                new Control[] { chk_tfr }, new Control[] { chk_shownofly }, new Control[] { chk_displaycog },
                new Control[] { chk_displayheading }, new Control[] { chk_displaynavbearing },
                new Control[] { chk_displayradius }, new Control[] { chk_displaytarget },
                new Control[] { label11, num_linelength }, new Control[] { chk_displaytooltip }
            };
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            for (int row = 0; row < rows.Length; row++)
            {
                panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                for (int col = 0; col < rows[row].Length; col++)
                {
                    var control = rows[row][col];
                    control.Dock = DockStyle.Fill; control.Margin = new Padding(4, 6, 4, 6);
                    control.MinimumSize = new Size(0, 26); control.Visible = true;
                    if (control is Label || control is CheckBox) control.AutoSize = true;
                    panel.Controls.Add(control, col, row);
                    if (rows[row].Length == 1) panel.SetColumnSpan(control, 2);
                }
            }
            Controls.Clear(); AutoScroll = true; Controls.Add(panel);
        }
    }
}
