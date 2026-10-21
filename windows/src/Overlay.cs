using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace OledGuard
{
    public sealed class SaverOverlay : Form
    {
        private readonly bool retro;
        private readonly bool capture;
        private readonly Timer animation;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<PointF[]> trail = new List<PointF[]>();
        private readonly Font logoFont = new Font("Segoe UI", 16, FontStyle.Bold);
        private int frame;
        public bool CaptureExclusionApplied { get; private set; }
        public SaverOverlay(Rectangle bounds, SaverMode mode, bool captureCompatible)
        {
            retro = mode == SaverMode.OldStyle; capture = captureCompatible;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; Bounds = bounds;
            BackColor = Color.Black; TopMost = true; DoubleBuffered = true;
            Text = "OLED Guard — " + (retro ? "Old style" : "Pitch black");
            animation = new Timer { Interval = 50 };
            animation.Tick += delegate { frame++; Invalidate(); };
            if (retro) animation.Start();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (capture) CaptureExclusionApplied = Native.SetWindowDisplayAffinity(Handle, 0x11);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (m.Msg == 0x20) { Native.SetCursor(IntPtr.Zero); m.Result = new IntPtr(1); return; }
            base.WndProc(ref m);
        }
        public void RaiseWithoutFocus()
        { Native.SetWindowPos(Handle, new IntPtr(-1), Left, Top, Width, Height, 0x0010 | 0x0040); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Black);
            if (!retro) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            double t = clock.Elapsed.TotalSeconds;
            var points = new PointF[4];
            for (int i = 0; i < points.Length; i++)
                points[i] = new PointF((float)(Width * (.5 + .34 * Math.Sin(t * .31 + i * 1.7))),
                    (float)(Height * (.5 + .32 * Math.Sin(t * .23 + i * 2.2))));
            // Bounded history and low brightness: the classic XP ribbon, without static text.
            if (frame % 2 == 0) { trail.Add(points); if (trail.Count > 18) trail.RemoveAt(0); }
            for (int i = 0; i < trail.Count; i++)
            {
                int b = 18 + i * 3;
                using (var pen = new Pen(Color.FromArgb(b / 3, b, Math.Min(100, b + 18)), 1.5f))
                    e.Graphics.DrawClosedCurve(pen, trail[i]);
            }
            SizeF textSize = e.Graphics.MeasureString("OLED GUARD", logoFont);
            float x = Bounce((float)(t * 47), Math.Max(1, Width - textSize.Width - 16));
            float y = Bounce((float)(t * 29), Math.Max(1, Height - textSize.Height - 16));
            using (var brush = new SolidBrush(Color.FromArgb(45, 68, 78)))
                e.Graphics.DrawString("OLED GUARD", logoFont, brush, x + 8, y + 8);
        }
        private static float Bounce(float value, float limit)
        { float cycle = value % (limit * 2); return cycle < limit ? cycle : limit * 2 - cycle; }
        protected override void Dispose(bool disposing)
        { if (disposing) { animation.Dispose(); logoFont.Dispose(); } base.Dispose(disposing); }
    }
    public sealed class OverlayGroup : IDisposable
    {
        private readonly List<SaverOverlay> windows = new List<SaverOverlay>();
        public bool CaptureCompatible { get { return windows.All(w => w.CaptureExclusionApplied); } }
        public void Show(Settings settings)
        {
            var selected = Screen.AllScreens.Where(s => string.IsNullOrEmpty(settings.ScreenName) || s.DeviceName == settings.ScreenName).ToArray();
            if (selected.Length == 0) throw new InvalidOperationException(I18n.T("Seçilen ekran bağlı değil. Ekran seçimini yenileyin."));
            foreach (var screen in selected)
            {
                var window = new SaverOverlay(screen.Bounds, settings.Mode, settings.CaptureCompatible);
                windows.Add(window); window.Show(); window.RaiseWithoutFocus();
            }
        }
        public void Raise() { foreach (var w in windows) w.RaiseWithoutFocus(); }
        public void Dispose() { foreach (var w in windows) w.Dispose(); windows.Clear(); }
    }
}
