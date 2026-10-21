using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("OLED Guard")]
[assembly: AssemblyDescription("Windows 11 OLED ekran koruyucu")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace OledGuard
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--watchdog") return RecoveryGuard.Run(args);
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && args[0] == "--render-ui")
            {
                using (var form = new MainForm(true))
                {
                    if (args.Length >= 3) form.SelectLanguage(args[2]);
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-30000, -30000); form.Show(); Application.DoEvents();
                    using (var graphics = form.CreateGraphics())
                        File.WriteAllText(args[1] + ".txt", "DPI=" + graphics.DpiX + " FontHeight=" + form.Font.Height + " Autoscale=" + form.AutoScaleDimensions + " Current=" + form.CurrentAutoScaleDimensions + " Size=" + form.Size);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(args[1]); }
                }
                return 0;
            }
            bool first;
            using (var mutex = new Mutex(true, "Local\\OLEDGuard.Alpha01", out first))
            {
                if (!first)
                {
                    IntPtr window = Native.FindWindow(null, "OLED Guard · 0.2 alpha");
                    if (window != IntPtr.Zero) Native.PostMessage(window, 0x8001, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Storage.Log("UI exception: " + e.Exception);
                    // Exit closes overlays; the separate process restores hardware brightness.
                    Application.Exit();
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                { Storage.Log("Fatal: " + e.ExceptionObject); };
                try { using (var form = new MainForm(false)) Application.Run(form); return 0; }
                catch (Exception e)
                { Storage.Log(e.ToString()); MessageBox.Show(e.Message, I18n.T("OLED Guard başlatılamadı"), MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
