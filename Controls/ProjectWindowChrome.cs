using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    // 使用系统原生标题栏；只调整支持该属性的 Windows 窗框外观。
    internal static class ProjectWindowChrome
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        internal static void Apply(Form window)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || !window.IsHandleCreated) return;
            try
            {
                var dark = 1;
                DwmSetWindowAttribute(window.Handle, 20, ref dark, sizeof(int));
                var caption = ColorTranslator.ToWin32(ProjectWorkspaceStyle.Surface);
                var text = ColorTranslator.ToWin32(ProjectWorkspaceStyle.Text);
                var border = ColorTranslator.ToWin32(ProjectWorkspaceStyle.Edge);
                // 不支持的 Windows 版本会返回失败码，继续使用原生窗口按钮和拖动行为。
                DwmSetWindowAttribute(window.Handle, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(window.Handle, 36, ref text, sizeof(int));
                DwmSetWindowAttribute(window.Handle, 34, ref border, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        internal static System.Drawing.Icon CreateWorkspaceIcon()
        {
            using (var bitmap = new Bitmap(64, 64))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                using (var pen = new Pen(ProjectWorkspaceStyle.Cyan, 4))
                {
                    graphics.Clear(ProjectWorkspaceStyle.Surface);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.DrawEllipse(pen, 14, 14, 36, 36);
                    graphics.DrawLine(pen, 32, 5, 32, 59);
                    graphics.DrawLine(pen, 5, 32, 59, 32);
                }
                var handle = bitmap.GetHicon();
                try
                {
                    using (var temporary = System.Drawing.Icon.FromHandle(handle)) return (System.Drawing.Icon)temporary.Clone();
                }
                finally { DestroyIcon(handle); }
            }
        }
    }
}

