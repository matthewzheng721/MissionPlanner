using MissionPlanner.Utilities;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    public partial class AuxOptions : UserControl
    {
        static int index = 0;
        public int thisaux;
        private bool commandPending;
        private readonly Timer projectCheckTimer;
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            try
            {
                var paramname = "RC6_OPTION";
                var source = ParameterMetaDataRepository.GetParameterOptionsInt(paramname, MainV2.comPort.MAV.cs.firmware.ToString());

                mavlinkComboBox1.SelectedValueChanged -= MavlinkComboBox1_SelectedValueChanged;

                mavlinkComboBox1.DisplayMember = "Value";
                mavlinkComboBox1.ValueMember = "Key";
                mavlinkComboBox1.DataSource = source;

                mavlinkComboBox1.ParamName = paramname;
                mavlinkComboBox1.Name = paramname;

                mavlinkComboBox1.Enabled = true;
                mavlinkComboBox1.Visible = true;

                try
                {
                    mavlinkComboBox1.SelectedValue = int.Parse(Settings.Instance["Aux" + thisaux + "_desc", "0"]);
                }
                catch { }
                mavlinkComboBox1.SelectedValueChanged += MavlinkComboBox1_SelectedValueChanged;
            }
            catch { }
        }

        private void MavlinkComboBox1_SelectedValueChanged(object sender, EventArgs e)
        {
            if (mavlinkComboBox1.SelectedValue != null) Settings.Instance["Aux" + thisaux + "_desc"] = mavlinkComboBox1.SelectedValue.ToString();
        }

        public AuxOptions()
        {
            InitializeComponent();

            projectCheckTimer = new Timer(components ?? (components = new System.ComponentModel.Container())) { Interval = 250 };
            projectCheckTimer.Tick += (sender, args) => { if (Visible) UpdateProjectAvailability(); };
            projectCheckTimer.Start();
            mavlinkComboBox1.SelectedValueChanged += (sender, args) => UpdateProjectAvailability();
            TXT_low_value.TextChanged += (sender, args) => UpdateProjectAvailability();
            TXT_highvalue.TextChanged += (sender, args) => UpdateProjectAvailability();
            loadSettings();

            TXT_rcchannel.BackColor = Color.Gray;

            thisaux = index++;

            TXT_rcchannel.Text = thisaux.ToString();
        }

        void loadSettings()
        {
            string desc = Settings.Instance["Aux" + thisaux + "_desc"];
            string low = Settings.Instance["Aux" + thisaux + "_low"];
            string mid = Settings.Instance["Aux" + thisaux + "_mid"];
            string high = Settings.Instance["Aux" + thisaux + "_high"];

            string highdesc = Settings.Instance["Aux" + thisaux + "_highdesc"];
            string lowdesc = Settings.Instance["Aux" + thisaux + "_lowdesc"];

            if (!string.IsNullOrEmpty(low))
            {
                TXT_low_value.Text = low;
            }

            if (!string.IsNullOrEmpty(mid))
            {
                txt_midvalue.Text = mid;
            }

            if (!string.IsNullOrEmpty(high))
            {
                TXT_highvalue.Text = high;
            }

            if (!string.IsNullOrEmpty(desc))
            {
                TXT_rcchannel.Text = desc;
            }

            if (!string.IsNullOrEmpty(highdesc))
            {
                BUT_High.Text = highdesc;
            }

            if (!string.IsNullOrEmpty(lowdesc))
            {
                BUT_Low.Text = lowdesc;
            }
        }

        // 按实际发送档位判定，不按可自定义的按钮文字判定；固件来源见交付记录。
        public static bool? ProjectArmIntent(int option, int position)
        {
            if (option == 41 || option == 153 || option == 154)
            {
                if (position == 2) return true;
                if (position == 0) return false;
            }
            if (option == 81 && position == 2) return false;
            if (option == 165 && position == 2) return true;
            return null;
        }
        private bool TryPosition(int slot, out int position)
        {
            position = 0;
            int low, high;
            if (slot == 0) return int.TryParse(TXT_low_value.Text, out position);
            if (slot == 2) return int.TryParse(TXT_highvalue.Text, out position);
            if (!int.TryParse(TXT_low_value.Text, out low) || !int.TryParse(TXT_highvalue.Text, out high)) return false;
            // 沿用原 MP 中档计算，检查依据与实际发出的 param2 完全一致。
            position = (high - low) / 2 + low;
            return true;
        }
        private void UpdateProjectAvailability()
        {
            if (IsDisposed || BUT_Low == null || BUT_High == null || but_mid == null) return;
            ProjectPreflight.RefreshConnection();
            int option;
            bool selected = int.TryParse(Convert.ToString(mavlinkComboBox1.SelectedValue), out option);
            var buttons = new Button[] { BUT_Low, but_mid, BUT_High };
            for (var slot = 0; slot < 3; slot++)
            {
                int position;
                buttons[slot].Enabled = !commandPending && selected && TryPosition(slot, out position) &&
                    (ProjectArmIntent(option, position) != true || ProjectPreflight.Checklist.CanArm);
            }
        }
        private async void ExecuteProjectAux(int slot, Color color)
        {
            if (commandPending) return;
            UpdateProjectAvailability();
            int option, position;
            if (!int.TryParse(Convert.ToString(mavlinkComboBox1.SelectedValue), out option) || !TryPosition(slot, out position)) return;
            var target = MainV2.comPort; var stream = target.BaseStream;
            var system = target.MAV.sysid; var component = target.MAV.compid;
            var intent = ProjectArmIntent(option, position);
            if (intent == true && !ProjectPreflight.Checklist.CanArm) return;
            if (intent.HasValue && !ProjectPreflight.ConfirmArm(this, intent.Value)) return;
            Func<bool> sameTarget = () => !IsDisposed && ReferenceEquals(MainV2.comPort, target) && ReferenceEquals(target.BaseStream, stream) &&
                stream.IsOpen && target.MAV.sysid == system && target.MAV.compid == component;
            if (!sameTarget() || (intent == true && !ProjectPreflight.Checklist.CanArm)) return;
            commandPending = true; UpdateProjectAvailability();
            try
            {
                var accepted = await Task.Run(() =>
                {
                    Action checkTarget = () => { if (!sameTarget() || (intent == true && !ProjectPreflight.Checklist.CanArm)) throw new OperationCanceledException(); };
                    checkTarget();
                    try { return target.doCommand(system, component, MAVLink.MAV_CMD.DO_AUX_FUNCTION, option, position, 0, 0, 0, 0, 0, true, checkTarget); }
                    finally { target.giveComport = false; }
                });
                if (!sameTarget()) return;
                if (accepted) { TXT_rcchannel.BackColor = color; TXT_rcchannel.Text = "飞控已接受：" + (slot == 0 ? "低" : slot == 1 ? "中" : "高") + "档"; }
                else CustomMessageBox.Show(Strings.CommandFailed, Strings.ERROR);
            }
            catch (OperationCanceledException) { /* 确认后或等待应答时换目标，取消旧请求而不改新连接。 */ }
            catch (Exception ex) { if (sameTarget()) CustomMessageBox.Show(Strings.CommandFailed + ex, Strings.ERROR); }
            finally { commandPending = false; if (!IsDisposed) UpdateProjectAvailability(); }
        }
        private void BUT_Low_Click(object sender, EventArgs e) { ExecuteProjectAux(0, Color.Red); }
        private void BUT_High_Click(object sender, EventArgs e) { ExecuteProjectAux(2, Color.Green); }
        private void TXT_pwm_low_TextChanged(object sender, EventArgs e)
        {
            Settings.Instance["Aux" + thisaux + "_low"] = TXT_low_value.Text;
        }

        private void TXT_pwm_high_TextChanged(object sender, EventArgs e)
        {
            Settings.Instance["Aux" + thisaux + "_high"] = TXT_highvalue.Text;
        }

        private void But_mid_Click(object sender, EventArgs e) { ExecuteProjectAux(1, Color.Orange); }

        private void txt_midvalue_TextChanged(object sender, EventArgs e)
        {
            Settings.Instance["Aux" + thisaux + "_mid"] = txt_midvalue.Text;
        }
    }
}