using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using OledGuard;

internal static class Tests
{
    private static int passed, failed;
    private static bool headless;
    private static readonly string root = Path.Combine(Path.GetTempPath(), "OLEDGuard-tests-" + Guid.NewGuid().ToString("N"));
    private static string Journal() { return Path.Combine(root, Guid.NewGuid().ToString("N") + ".json"); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Check(string name, Action test)
    {
        if (headless && (name.StartsWith("Pitch black") || name.StartsWith("SPACE hook") || name.StartsWith("Old style")))
        { Console.WriteLine("SKIP (headless desktop): " + name); return; }
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
    }
    private static void Throws(Action operation)
    { bool thrown = false; try { operation(); } catch { thrown = true; } Assert(thrown, "Expected exception"); }
    private sealed class Brightness : IBrightnessBackend
    {
        public readonly Dictionary<string, byte> Levels = new Dictionary<string, byte> { { "A", 70 }, { "B", 35 } };
        public readonly List<string> Calls = new List<string>();
        public string JournalFile;
        public bool GuardStarted;
        public Func<string, byte, bool> Fail;
        public List<BrightnessSnapshot> Capture()
        { return Levels.Select(p => new BrightnessSnapshot { InstanceName = p.Key, Brightness = p.Value }).ToList(); }
        public void Set(string id, byte value)
        {
            Calls.Add(id + "=" + value);
            if (value == 0) Assert(File.Exists(JournalFile) && GuardStarted, "Dim before journal/watchdog");
            if (Fail != null && Fail(id, value)) throw new IOException("Fake driver failure");
            Levels[id] = value;
        }
    }
    private sealed class Guard : IRecoveryGuard
    {
        public Brightness Backend; public bool Fail; public bool Disposed;
        public void Start(string path) { if (Fail) throw new IOException("Watchdog failed"); Backend.GuardStarted = true; }
        public void Dispose() { Disposed = true; }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        headless = args.Contains("--headless");
        if (args.Length == 2 && args[0] == "--crash-parent")
        { Storage.Write(args[1], new List<BrightnessSnapshot>()); new RecoveryGuard().Start(args[1]); Environment.Exit(23); }
        Directory.CreateDirectory(root);
        Check("English and Turkish translations switch both ways", delegate
        {
            string original = "Korumayı başlat";
            I18n.Language = "en"; Assert(I18n.T(original) == "Start protection", "English missing");
            Assert(I18n.T("Donanım parlaklığı · 2 desteklenen dahili ekran bulundu").Contains("2 supported internal display(s)"), "Dynamic translation missing");
            I18n.Language = "tr"; Assert(I18n.T("Start protection") == original, "Turkish restoration missing");
            string bilingual = "Uykuya geçmeyi engelle  /  Prevent sleep";
            Assert(I18n.T(bilingual) == bilingual, "Turkish caption expanded recursively");
            I18n.Language = "en"; Assert(I18n.T(I18n.T(bilingual)) == "Prevent sleep", "English caption is not idempotent");
            I18n.Language = "tr";
        });
        Check("Journal + watchdog precede hardware writes; exact multi-display restore", delegate
        {
            string j = Journal(); var b = new Brightness { JournalFile = j }; var g = new Guard { Backend = b };
            var t = new BrightnessTransaction(b, g, j); t.Begin();
            Assert(b.Levels.Values.All(v => v == 0), "Not dimmed");
            var saved = Storage.Read<List<BrightnessSnapshot>>(j);
            Assert(saved[0].Brightness == 70 && saved[1].Brightness == 35, "Wrong snapshots");
            t.Restore(); Assert(b.Levels["A"] == 70 && b.Levels["B"] == 35 && !File.Exists(j) && g.Disposed, "Restore failure");
            int calls = b.Calls.Count; t.Restore(); Assert(b.Calls.Count == calls, "Non-idempotent restore");
        });
        Check("Partial dim failure rolls back every display", delegate
        {
            string j = Journal(); var b = new Brightness { JournalFile = j, Fail = (id, v) => id == "B" && v == 0 };
            var t = new BrightnessTransaction(b, new Guard { Backend = b }, j);
            Throws(t.Begin); Assert(b.Levels["A"] == 70 && b.Levels["B"] == 35 && !File.Exists(j), "Rollback failed");
        });
        Check("Watchdog startup failure never dims", delegate
        {
            string j = Journal(); var b = new Brightness { JournalFile = j };
            var t = new BrightnessTransaction(b, new Guard { Backend = b, Fail = true }, j);
            Throws(t.Begin); Assert(!b.Calls.Any(c => c.EndsWith("=0")) && !File.Exists(j), "Dimmed despite failed watchdog");
        });
        Check("Restore failure retains journal, restores other displays, supports retry", delegate
        {
            string j = Journal(); var b = new Brightness { JournalFile = j }; var g = new Guard { Backend = b };
            var t = new BrightnessTransaction(b, g, j); t.Begin(); b.Fail = (id, v) => id == "A" && v == 70;
            Throws(t.Restore); Assert(File.Exists(j) && !g.Disposed && b.Levels["B"] == 35, "Lost recovery state");
            b.Fail = null; t.Restore(); Assert(!File.Exists(j) && b.Levels["A"] == 70, "Retry failed");
        });
        Check("Already zero brightness returns to zero", delegate
        {
            string j = Journal(); var b = new Brightness { JournalFile = j }; b.Levels["A"] = 0;
            var t = new BrightnessTransaction(b, new Guard { Backend = b }, j); t.Begin(); t.Restore(); Assert(b.Levels["A"] == 0, "Changed baseline");
        });
        Check("Unsupported hardware has no writes or recovery file", delegate
        {
            string j = Journal(); var b = new Brightness { JournalFile = j }; b.Levels.Clear();
            Throws(new BrightnessTransaction(b, new Guard { Backend = b }, j).Begin);
            Assert(!File.Exists(j) && b.Calls.Count == 0, "Unexpected side effects");
        });
        Check("Crash recovery retains failed record then retries all original values", delegate
        {
            string j = Journal(); var b = new Brightness { JournalFile = j };
            Storage.Write(j, b.Capture()); b.Levels["A"] = b.Levels["B"] = 0;
            b.Fail = (id, v) => id == "A";
            Assert(!BrightnessTransaction.Recover(b, j) && File.Exists(j) && b.Levels["B"] == 35, "Partial recovery lost");
            b.Fail = null;
            Assert(BrightnessTransaction.Recover(b, j) && !File.Exists(j) && b.Levels["A"] == 70, "Recovery retry failed");
        });
        Check("Malformed journal is preserved", delegate
        { string j = Journal(); File.WriteAllText(j, "{broken"); Throws(() => BrightnessTransaction.Recover(new Brightness(), j)); Assert(File.Exists(j), "Deleted damaged record"); });
        Check("Missing journal is harmless", delegate { Assert(BrightnessTransaction.Recover(new Brightness(), Journal()), "Missing journal rejected"); });
        Check("Settings round trip keeps independent options", delegate
        {
            string j = Journal(); var s = new Settings { Mode = SaverMode.OldStyle, PreventSleep = false,
                PreventDisplayOff = true, PreventIdleLock = true, CaptureCompatible = false, ScreenName = "DISPLAY_TEST" };
            Storage.Write(j, s); var copy = Storage.Read<Settings>(j);
            Assert(copy.Mode == s.Mode && !copy.PreventSleep && copy.PreventDisplayOff && copy.PreventIdleLock && !copy.CaptureCompatible && copy.ScreenName == s.ScreenName, "Settings lost");
            s.Mode = SaverMode.PitchBlack; Storage.Write(j, s); Assert(Storage.Read<Settings>(j).Mode == s.Mode, "Atomic replacement failed");
        });
        Check("Power flags cover every independent combination", delegate
        {
            for (int i = 0; i < 4; i++)
            {
                var s = new Settings { PreventSleep = (i & 1) != 0, PreventDisplayOff = (i & 2) != 0, PreventIdleLock = true };
                Assert(Native.PowerFlags(s) == 0x80000000u + (uint)i, "Power flags coupled");
            }
        });
        Check("USES profile uses hardware without idle input simulation", delegate
        { var s = Settings.UsesProfile(); Assert(s.Mode == SaverMode.BrightnessZero && s.PreventSleep && s.PreventDisplayOff && !s.PreventIdleLock, "Wrong USES profile"); });
        Check("Input structure matches native x64/x86 ABI", delegate
        { Assert(Marshal.SizeOf(typeof(Native.INPUT)) == (IntPtr.Size == 8 ? 40 : 28), "Wrong SendInput size"); });
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); Application.EnableVisualStyles();
        Check("Pitch black is pure RGB zero; overlay does not activate", delegate
        {
            IntPtr before = Native.GetForegroundWindow();
            using (var w = new SaverOverlay(new Rectangle(-10000, -10000, 320, 180), SaverMode.PitchBlack, true))
            {
                w.Show(); w.RaiseWithoutFocus(); Application.DoEvents();
                Assert(Native.GetForegroundWindow() == before, "Overlay stole focus");
                Assert(w.Bounds.Width == 320 && w.Bounds.Height == 180, "Wrong physical bounds");
                uint affinity; Assert(Native.GetWindowDisplayAffinity(w.Handle, out affinity) && affinity == 0x11, "Capture affinity not applied");
                using (var bitmap = new Bitmap(w.Width, w.Height))
                {
                    w.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                        Assert(bitmap.GetPixel(x, y).ToArgb() == Color.Black.ToArgb(), "Non-black pixel");
                }
            }
        });
        Check("SPACE hook receives exit and swallows down/up", delegate
        {
            int observed = 0;
            Native.HookProc observer = delegate(int code, IntPtr wp, IntPtr lp)
            {
                if (code >= 0 && Marshal.ReadInt32(lp) == 0x20) observed++;
                return Native.CallNextHookEx(IntPtr.Zero, code, wp, lp);
            };
            IntPtr observerHandle = Native.SetWindowsHookEx(13, observer, Native.GetModuleHandle(null), 0);
            Assert(observerHandle != IntPtr.Zero, "Observer hook failed");
            try
            {
            using (var signal = new ManualResetEvent(false))
            using (var hook = new ExitKeyHook(() => signal.Set()))
            {
                hook.Armed = true;
                var input = new Native.INPUT { type = 1, data = new Native.INPUTUNION { keyboard = new Native.KEYBDINPUT { vk = 0x20 } } };
                var up = input; up.data.keyboard.flags = 2;
                Assert(Native.SendInput(2, new[] { input, up }, Marshal.SizeOf(typeof(Native.INPUT))) == 2, "SendInput blocked");
                Assert(signal.WaitOne(2000) && !hook.Armed, "SPACE hook failed");
                for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(20); }
                Assert(observed == 0, "SPACE leaked to downstream applications");
            }
            }
            finally { Native.UnhookWindowsHookEx(observerHandle); GC.KeepAlive(observer); }
        });
        Check("Old style visibly animates", delegate
        {
            using (var w = new SaverOverlay(new Rectangle(-10000, -10000, 640, 360), SaverMode.OldStyle, false))
            {
                w.Show(); Application.DoEvents();
                using (var first = new Bitmap(w.Width, w.Height))
                using (var second = new Bitmap(w.Width, w.Height))
                {
                    w.DrawToBitmap(first, new Rectangle(Point.Empty, first.Size));
                    for (int i = 0; i < 10; i++) { Thread.Sleep(50); Application.DoEvents(); }
                    w.DrawToBitmap(second, new Rectangle(Point.Empty, second.Size));
                    bool changed = false;
                    for (int y = 0; y < first.Height; y += 3) for (int x = 0; x < first.Width; x += 3)
                        if (first.GetPixel(x, y) != second.GetPixel(x, y)) changed = true;
                    Assert(changed, "No animation");
                }
            }
        });
        Check("Real Windows power request is applied then released on same thread", delegate
        {
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            try
            {
                Native.ApplyPower(new Settings());
                uint previous = Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
                Assert((previous & 3) == 3, "Power request not retained");
            }
            finally { Native.SetThreadExecutionState(Native.ES_CONTINUOUS); }
        });
        Check("Real watchdog restores an empty journal after parent process crash", delegate
        {
            // Empty list deliberately exercises lifecycle without writing any monitor.
            string journal = Path.Combine(Storage.Root, "recovery-test-" + Guid.NewGuid().ToString("N") + ".json");
            var info = new ProcessStartInfo(typeof(Tests).Assembly.Location, "--crash-parent \"" + journal + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            using (var p = Process.Start(info))
            { Assert(p.WaitForExit(10000) && p.ExitCode == 23, "Crash parent did not reach watchdog readiness"); }
            for (int i = 0; i < 30 && File.Exists(journal); i++) Thread.Sleep(100);
            Assert(!File.Exists(journal), "Watchdog did not recover journal");
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed. Hardware brightness was NOT changed.");
        // Keep artifacts on failure; delete only this verified, unique test folder on success.
        if (failed == 0 && Path.GetFileName(root).StartsWith("OLEDGuard-tests-")) Directory.Delete(root, true);
        return failed == 0 ? 0 : 1;
    }
}
