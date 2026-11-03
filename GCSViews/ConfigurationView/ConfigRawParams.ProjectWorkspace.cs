using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    public partial class ConfigRawParams
    {
        private bool projectWorkspaceMode;
        private Form projectComparisonWindow;
        private object projectParameterStream;
        private object projectParameterVehicle;
        private bool projectParameterConnected;
        private int projectParameterCount = -1;
        private Label projectParameterNotice;

        public void ConfigureProjectWorkspace()
        {
            projectWorkspaceMode = true;
            SuspendLayout();
            foreach (DataGridViewColumn column in Params.Columns)
                column.Visible = column == Fav || column == Command || column == Value;
            Fav.DisplayIndex = 0;
            Fav.HeaderText = "FAV";
            Fav.Width = 42;
            Command.DisplayIndex = 1;
            Command.HeaderText = "参数名";
            Command.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            Command.FillWeight = 65;
            Value.DisplayIndex = 2;
            Value.HeaderText = "值";
            Value.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            Value.FillWeight = 35;
            Params.Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            txt_search.Dock = DockStyle.Fill;
            txt_search.Margin = new Padding(5);
            txt_search.AccessibleName = "搜索参数";
            layout.Controls.Add(txt_search, 0, 0);
            layout.Controls.Add(Params, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
            foreach (var button in new[] { BUT_writePIDS, BUT_rerequestparams, BUT_compare, BUT_save, BUT_load })
            {
                button.Dock = DockStyle.None;
                button.AutoSize = false;
                button.Size = new Size(68, 30);
                button.MinimumSize = new Size(62, 30);
                button.Margin = new Padding(3);
                buttons.Controls.Add(button);
            }
            BUT_writePIDS.Text = "写入";
            writeButtonText = BUT_writePIDS.Text;
            BUT_rerequestparams.Text = "刷新";
            BUT_compare.Text = "比较";
            BUT_save.Text = "保存";
            BUT_load.Text = "加载";
            layout.Controls.Add(buttons, 0, 2);
            projectParameterNotice = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "切页保留编辑；连接或目标变化后重新读取。" };
            layout.Controls.Add(projectParameterNotice, 0, 3);
            splitContainer1.Visible = false;
            Controls.Add(layout);
            layout.BringToFront();
            ResumeLayout(true);
            Disposed += (s, e) => projectComparisonWindow?.Close();
        }

        private void ShowProjectParameterComparison(Form comparison)
        {
            if (!projectWorkspaceMode)
            {
                using (comparison) comparison.ShowDialog(FindForm());
                return;
            }
            if (projectComparisonWindow != null && !projectComparisonWindow.IsDisposed)
                projectComparisonWindow.Close();
            projectComparisonWindow = comparison;
            var stream = MainV2.comPort.BaseStream;
            var vehicle = MainV2.comPort.MAV;
            var connected = stream.IsOpen;
            var parameterComparison = comparison as MissionPlanner.Controls.ParamCompare;
            if (parameterComparison != null)
                parameterComparison.CanApplyChanges = () => !IsDisposed && ReferenceEquals(MainV2.comPort.BaseStream, stream) &&
                    ReferenceEquals(MainV2.comPort.MAV, vehicle) && stream.IsOpen == connected;
            // 对比保持非模态，Continue 仍只写回原表，统一由写入按钮发送。
            comparison.StartPosition = FormStartPosition.Manual;
            var owner = FindForm();
            var point = PointToScreen(Point.Empty);
            var bounds = Screen.FromControl(this).WorkingArea;
            if (projectWorkspaceMode && FlightData.instance != null)
                bounds = Rectangle.Intersect(bounds, FlightData.instance.GetProjectOperationScreenBounds());
            comparison.Size = new Size(Math.Min(comparison.Width, bounds.Width), Math.Min(comparison.Height, bounds.Height));
            comparison.Location = new Point(Math.Max(bounds.Left, Math.Min(point.X, bounds.Right - comparison.Width)),
                Math.Max(bounds.Top, Math.Min(point.Y, bounds.Bottom - comparison.Height)));
            comparison.Show(owner);
        }

        public void ObserveProjectWorkspaceConnection()
        {
            if (!projectWorkspaceMode || IsDisposed) return;
            var port = MainV2.comPort;
            var connected = port.BaseStream.IsOpen && !port.logreadmode;
            var changed = !ReferenceEquals(projectParameterStream, port.BaseStream) ||
                !ReferenceEquals(projectParameterVehicle, port.MAV) || projectParameterConnected != connected;
            if (changed)
            {
                rowLoadCancellation?.Cancel();
                Params.CancelEdit();
                _changes.Clear();
                projectComparisonWindow?.Close();
                Params.Rows.Clear();
                rowlist.Clear();
                startup = true;
                projectParameterStream = port.BaseStream;
                projectParameterVehicle = port.MAV;
                projectParameterConnected = connected;
                projectParameterCount = -1;
                projectParameterNotice.Text = connected ? "连接或目标已变化，旧编辑已清除，正在读取当前参数。" : "未连接，旧编辑已清除；连接后读取当前参数。";
            }
            BUT_writePIDS.Enabled = connected && !writingParameters;
            BUT_rerequestparams.Enabled = connected;
            if (connected && !loadingParameters && !writingParameters && _changes.Count == 0 && !Params.IsCurrentCellInEditMode &&
                projectParameterCount != port.MAV.param.Count())
            {
                projectParameterCount = port.MAV.param.Count();
                Activate();
            }
        }

        public void RefreshProjectWorkspaceIfClean()
        {
            if (_changes.Count == 0 && !Params.IsCurrentCellInEditMode)
                projectParameterCount = -1;
            else
                projectParameterNotice.Text = "参数已重新读取；保留当前未写编辑，写入或参数页刷新后重新排序。";
            ObserveProjectWorkspaceConnection();
        }
    }
}
