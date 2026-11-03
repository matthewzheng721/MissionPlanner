using System;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Controls;

namespace MissionPlanner.Utilities
{
    public enum ProjectCheckResult { Unchecked, Confirmed, Skipped }

    // 检查仅记录操作员核对；不推断传感器通过，也不替代飞控预解锁。
    public sealed class ProjectPreflightChecklist
    {
        public static readonly string[] Titles = { "基础状态检查", "ECU 与缸温", "空速计", "舵机测试", "舵机极性", "EKF / VIBE", "暖机温度", "预解锁检查" };
        private readonly ProjectCheckResult[] results = new ProjectCheckResult[8];
        private object connection;
        private int system, component;
        public event EventHandler Changed;
        public bool Connected { get; private set; }
        public bool FbwaPolarityConfirmed { get; private set; }
        public bool ManualPolarityConfirmed { get; private set; }
        public bool CanArm => Connected && results.All(value => value == ProjectCheckResult.Confirmed);
        public ProjectCheckResult this[int index] => results[index];
        public string Missing => string.Join("、", Titles.Where((title, index) => results[index] != ProjectCheckResult.Confirmed));

        public void ObserveConnection(object identity, int systemId, int componentId, bool connected)
        {
            if (Connected != connected || !ReferenceEquals(connection, identity) || system != systemId || component != componentId)
            {
                Array.Clear(results, 0, results.Length);
                FbwaPolarityConfirmed = ManualPolarityConfirmed = false;
                connection = identity; system = systemId; component = componentId; Connected = connected;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SetPolarity(bool manual, bool confirmed)
        {
            if (confirmed && !Connected) return;
            if (manual) ManualPolarityConfirmed = confirmed; else FbwaPolarityConfirmed = confirmed;
            results[4] = FbwaPolarityConfirmed && ManualPolarityConfirmed ? ProjectCheckResult.Confirmed : ProjectCheckResult.Unchecked;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Set(int index, ProjectCheckResult result)
        {
            if (result == ProjectCheckResult.Confirmed && !Connected) return;
            if (index == 4)
            {
                if (result == ProjectCheckResult.Confirmed && !(FbwaPolarityConfirmed && ManualPolarityConfirmed)) return;
                if (result != ProjectCheckResult.Confirmed) FbwaPolarityConfirmed = ManualPolarityConfirmed = false;
            }
            results[index] = result;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public static class ProjectPreflight
    {
        public static ProjectPreflightChecklist Checklist { get; } = new ProjectPreflightChecklist();
        private static ProjectPreflightWizard window;
        public static void RefreshConnection()
        {
            var port = MainV2.comPort;
            Checklist.ObserveConnection(port?.BaseStream, port?.MAV.sysid ?? 0, port?.MAV.compid ?? 0,
                port != null && port.BaseStream.IsOpen && !port.logreadmode);
        }
        public static void Show(Control owner)
        {
            RefreshConnection();
            if (window == null || window.IsDisposed) window = new ProjectPreflightWizard();
            if (!window.Visible) window.Show(owner); else window.Activate();
        }
        public static bool ConfirmArm(IWin32Window owner, bool arm)
        {
            RefreshConnection();
            if (arm && !Checklist.CanArm)
            {
                MessageBox.Show(owner, "请完成人工核对：" + Checklist.Missing + "。\n平台与起飞条件两组均须完成；跳过不算完成。", "尚未完成检查", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (!ConfirmAction(owner, arm ? "解锁" : "锁定", "执行后仍以飞控反馈为准。")) return false;
            RefreshConnection();
            return !arm || Checklist.CanArm;
        }
        public static bool ConfirmAction(IWin32Window owner, string action, string detail)
        {
            return MissionPlanner.Controls.ProjectWorkspaceStyle.Confirm(owner, action, detail);
        }
    }
}
