using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TimeSpan = System.TimeSpan;

namespace MissionPlanner.Controls
{
    public partial class Status : UserControl
    {
        private System.Threading.Timer _hidetimer;
        private volatile bool timerStopped;
        private double _percent = 50;
        public double Percent
        {
            get { return _percent;}
            set
            {
                if (value < 0 || value > 100 || IsStopping)
                    return;

                _percent = value;
                QueueVisibility(true);
                var timer = _hidetimer;
                if (timer == null) return;
                try
                {
                    timer.Change(TimeSpan.FromSeconds(10), TimeSpan.Zero);
                }
                catch (ObjectDisposedException) when (IsStopping)
                {
                    // Dispose 可能刚好发生在读取计时器引用之后，只忽略关闭中的这一次更新。
                    return;
                }
                if (!IsStopping) this.Invalidate();
            }
        }
        public Status()
        {
            InitializeComponent();

            CreateHandle();

            _hidetimer = new System.Threading.Timer(HideAfterDelay, null, 1, -1);
        }

        private bool IsStopping => timerStopped || IsDisposed || Disposing;

        private void HideAfterDelay(object state)
        {
            QueueVisibility(false);
        }

        private void QueueVisibility(bool visible)
        {
            if (IsStopping || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() => ApplyVisibilityIfAlive(visible)));
            }
            catch (InvalidOperationException) when (IsStopping || !IsHandleCreated)
            {
                // 检查后窗口仍可能正好销毁；其他状态的投递异常继续抛出。
            }
        }

        private void ApplyVisibilityIfAlive(bool visible)
        {
            // 已经排入 UI 队列的委托也可能晚于 Dispose 到达，不能重新创建控件句柄。
            if (IsStopping || !IsHandleCreated) return;
            Visible = visible;
        }

        private void StopHideTimer()
        {
            // 先标记终止，再释放计时器；已进入线程池的回调仍由两层检查阻止。
            timerStopped = true;
            Interlocked.Exchange(ref _hidetimer, null)?.Dispose();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_percent > 100)
                _percent = 50;

            try
            {
                e.Graphics.FillRectangle(Brushes.Green, 0, 0, (float) (Width * (_percent / 100.0)), Height);
            }
            catch (OverflowException)
            {

            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            
        }
    }
}
