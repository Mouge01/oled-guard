using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OledGuard
{
    public sealed class MainForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(231, 238, 244);
        private static readonly Color Muted = Color.FromArgb(153, 168, 181);
        private static readonly Color Accent = Color.FromArgb(116, 234, 197);
        private static readonly Color Card = Color.FromArgb(25, 33, 42);
        private readonly ComboBox mode = new ComboBox(), screen = new ComboBox();
        private readonly ComboBox language = new ComboBox();
        private readonly CheckBox sleep = new CheckBox(), display = new CheckBox(), idleLock = new CheckBox(), capture = new CheckBox();
        private readonly Label modeNote = new Label(), status = new Label(), hardware = new Label();
        private readonly Button start = new Button(), uses = new Button();
        private readonly TableLayoutPanel layout = new TableLayoutPanel();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 500 };
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private ExitKeyHook keyHook;
        private NotifyIcon tray;
        private OverlayGroup overlays;
        private BrightnessTransaction brightness;
        private CancellationTokenSource cancellation;
        private IntPtr previousWindow;
        private bool busy, active, exitRequested, allowClose, preview, disposed;
        private bool initialized;
        private bool previousRecoveryComplete = true;
        private readonly List<string> screenNames = new List<string>();
        private DateTime lastPulse;
        private bool idlePulseFailed;

        public MainForm(bool previewOnly)
        {
            preview = previewOnly;
            var savedSettings = Storage.LoadSettings();
            if (savedSettings.Language == "en" || savedSettings.Language == "tr") I18n.Language = savedSettings.Language;
            previousWindow = Native.GetForegroundWindow();
            Text = "Caffeine For OLED · 0.2.1 alpha"; Icon = SystemIcons.Application;
            BackColor = Color.FromArgb(14, 20, 27); ForeColor = Ink;
            AutoScaleMode = AutoScaleMode.None; Font = new Font("Segoe UI", 10);
            ClientSize = new Size(760, 775); MinimumSize = new Size(660, 640);
            StartPosition = FormStartPosition.CenterScreen;
            var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(26, 18, 26, 12) };
            Controls.Add(scroller);
            layout.Dock = DockStyle.Top; layout.AutoSize = true; layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); scroller.Controls.Add(layout);
            var brand = new Label { Text = "CAFFEINE FOR OLED", Font = new Font("Segoe UI", 23, FontStyle.Bold), ForeColor = Ink, Dock = DockStyle.Fill };
            Add(brand, 52);
            var languageRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            languageRow.Controls.Add(LabelOf(I18n.T("Dil / Language"), Muted), 0, 0);
            StyleCombo(language); language.DropDownStyle = ComboBoxStyle.DropDownList;
            language.Items.AddRange(new object[] { "Türkçe", "English" }); language.SelectedIndex = I18n.Language == "tr" ? 0 : 1;
            languageRow.Controls.Add(language, 1, 0); Add(languageRow, 40);
            Add(LabelOf(I18n.T("Ekranın dinlensin. Arka plandaki işin devam etsin."), Muted), 30);
            var banner = new Panel { BackColor = Card, Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0, 6, 0, 12) };
            var key = LabelOf(I18n.T("SPACE  ile geri dön     ·     ESC  yedek çıkış"), Accent); key.Font = new Font("Segoe UI", 11, FontStyle.Bold); banner.Controls.Add(key); Add(banner, 58);

            Add(LabelOf(I18n.T("KORUMA MODU"), Muted), 25);
            mode.DropDownStyle = ComboBoxStyle.DropDownList;
            mode.Items.AddRange(new object[] { I18n.T("Pitch black  /  Tam siyah"), I18n.T("Old style  /  XP esintili hareketli çizgiler"), I18n.T("Parlaklık %0  /  Örtü olmadan") });
            StyleCombo(mode); Add(mode, 36);
            modeNote.ForeColor = Muted; modeNote.Dock = DockStyle.Fill; modeNote.Margin = new Padding(0, 7, 0, 0); Add(modeNote, 59);

            var displayRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0), RowCount = 1 };
            displayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78)); displayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            displayRow.Controls.Add(LabelOf(I18n.T("Ekran"), Ink), 0, 0); StyleCombo(screen); screen.DropDownStyle = ComboBoxStyle.DropDownList;
            RefreshScreenChoices("");
            displayRow.Controls.Add(screen, 1, 0); Add(displayRow, 38);
            hardware.ForeColor = Muted; hardware.Dock = DockStyle.Fill; hardware.Font = new Font("Segoe UI", 9); hardware.Text = I18n.T("Donanım parlaklığı desteği kontrol ediliyor…"); Add(hardware, 30);

            Add(LabelOf(I18n.T("KORUMA SÜRESİNCE"), Muted), 27);
            ConfigureCheck(sleep, I18n.T("Uykuya geçmeyi engelle  /  Prevent sleep")); Add(sleep, 30);
            ConfigureCheck(display, I18n.T("Ekran kapanmasını engelle  /  Prevent screen off")); Add(display, 30);
            ConfigureCheck(idleLock, I18n.T("Otomatik kilidi engellemeyi dene  /  Prevent lockscreen")); Add(idleLock, 30);
            var lockNote = LabelOf(I18n.T("İsteğe bağlıdır. Win+L ve kurumun zorunlu kilit kuralları geçerlidir."), Muted);
            lockNote.Font = new Font("Segoe UI", 9); Add(lockNote, 24);
            ConfigureCheck(capture, I18n.T("Siyah örtüyü desteklenen ekran yakalamalarından hariç tut")); Add(capture, 34);

            uses.Text = I18n.T("USES profili: örtüsüz parlaklık %0"); StyleButton(uses, Card, Accent); Add(uses, 40);
            Add(LabelOf(I18n.T("Ders sekmesini açık ve Chrome’u küçültmeden bırakın. Gerçek USES akışı ayrıca sınanmalıdır."), Muted), 43);
            start.Text = I18n.T("Korumayı başlat"); StyleButton(start, Accent, Color.FromArgb(12, 30, 29));
            start.Font = new Font("Segoe UI", 12, FontStyle.Bold); Add(start, 48);
            status.ForeColor = Muted; status.Dock = DockStyle.Fill; status.Padding = new Padding(0, 7, 0, 0); Add(status, 44);
            var footer = LabelOf(I18n.T("0.2.1 ALPHA  ·  Windows 11  ·  Ctrl+Alt+O: başlat / durdur"), Muted);
            footer.Font = new Font("Segoe UI", 9); Add(footer, 27);

            ApplySettings(savedSettings);
            language.SelectedIndexChanged += delegate
            {
                I18n.Language = language.SelectedIndex == 0 ? "tr" : "en";
                RefreshLanguage();
                if (!preview) try { Storage.SaveSettings(ReadSettings()); } catch (Exception e) { SetStatus(I18n.T("Ayarlar kaydedilemedi: ") + e.Message); }
            };
            mode.SelectedIndexChanged += delegate { UpdateModeNote(); };
            uses.Click += delegate { ApplySettings(Settings.UsesProfile()); SetStatus(I18n.T("USES profili seçildi. Başlatın veya ders penceresindeyken Ctrl+Alt+O kullanın.")); };
            start.Click += async delegate { await StartProtection(); };
            FormClosing += OnClosing;
            Shown += async delegate { if (!preview) await Initialize(); };
            UpdateModeNote();
            if (preview) { hardware.Text = I18n.T("Donanım parlaklığı · Desteklenen dahili ekranlarda kullanılabilir"); SetStatus(I18n.T("Hazır. Koruma yalnızca siz başlattığınızda çalışır.")); }
            using (var graphics = CreateGraphics())
            { float factor = graphics.DpiX / 96f; Scale(new SizeF(factor, factor)); }
        }
        private static Label LabelOf(string text, Color color)
        { return new Label { Text = text, ForeColor = color, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false, Margin = new Padding(0) }; }
        private void Add(Control control, int height)
        {
            int row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            if (control.Margin == new Padding(3)) control.Margin = new Padding(0, 0, 0, 3);
            layout.Controls.Add(control, 0, row);
        }
        private static void StyleCombo(ComboBox combo)
        { combo.Dock = DockStyle.Fill; combo.BackColor = Card; combo.ForeColor = Ink; combo.FlatStyle = FlatStyle.Flat; }
        private static void ConfigureCheck(CheckBox c, string text)
        { c.Text = text; c.Dock = DockStyle.Fill; c.ForeColor = Ink; c.AutoSize = false; c.Margin = new Padding(0); }
        private static void StyleButton(Button b, Color background, Color foreground)
        { b.BackColor = background; b.ForeColor = foreground; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.Dock = DockStyle.Fill; b.Cursor = Cursors.Hand; b.Margin = new Padding(0, 0, 0, 5); }
        private void SetStatus(string message) { status.Text = message; }
        private Settings ReadSettings()
        {
            return new Settings { Language = I18n.Language, Mode = (SaverMode)mode.SelectedIndex, PreventSleep = sleep.Checked,
                PreventDisplayOff = display.Checked, PreventIdleLock = idleLock.Checked, CaptureCompatible = capture.Checked,
                ScreenName = screen.SelectedIndex <= 0 ? "" : screenNames[screen.SelectedIndex - 1] };
        }
        public void SelectLanguage(string selectedLanguage)
        { language.SelectedIndex = selectedLanguage == "tr" ? 0 : 1; RefreshLanguage(); }
        private void RefreshLanguage()
        {
            TranslateControls(this);
            if (tray != null)
            {
                tray.Text = I18n.T(tray.Text);
                foreach (ToolStripItem item in tray.ContextMenuStrip.Items) item.Text = I18n.T(item.Text);
            }
            UpdateModeNote();
        }
        private static void TranslateControls(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is ComboBox)
                {
                    var combo = (ComboBox)child;
                    if (combo.Items.Contains("English") && combo.Items.Contains("Türkçe")) continue;
                    int selected = combo.SelectedIndex;
                    for (int i = 0; i < combo.Items.Count; i++) combo.Items[i] = I18n.T(combo.Items[i].ToString());
                    combo.SelectedIndex = selected;
                }
                else child.Text = I18n.T(child.Text);
                TranslateControls(child);
            }
        }
        private void ApplySettings(Settings s)
        {
            mode.SelectedIndex = (int)s.Mode; sleep.Checked = s.PreventSleep; display.Checked = s.PreventDisplayOff;
            idleLock.Checked = s.PreventIdleLock; capture.Checked = s.CaptureCompatible;
            screen.SelectedIndex = 0;
            for (int i = 0; i < screenNames.Count; i++) if (screenNames[i] == s.ScreenName) screen.SelectedIndex = i + 1;
            UpdateModeNote();
        }
        private void RefreshScreenChoices(string selected)
        {
            screen.Items.Clear(); screenNames.Clear(); screen.Items.Add(I18n.T("Tüm ekranlar"));
            foreach (var s in Screen.AllScreens)
            { screenNames.Add(s.DeviceName); screen.Items.Add(s.DeviceName + (s.Primary ? I18n.T(" · Ana ekran") : "")); }
            screen.SelectedIndex = 0;
            for (int i = 0; i < screenNames.Count; i++) if (screenNames[i] == selected) screen.SelectedIndex = i + 1;
        }
        private void UpdateModeNote()
        {
            bool dim = mode.SelectedIndex == (int)SaverMode.BrightnessZero;
            screen.Enabled = !dim; capture.Visible = !dim;
            modeNote.Text = dim ? I18n.T("Görüntüyü ve pencere odağını değiştirmez. Yalnızca desteklenen dahili ekranlar kararır; %0 her panelde tam siyah değildir. Harici ekranlar korunmaz.") :
                mode.SelectedIndex == (int)SaverMode.OldStyle ? I18n.T("Siyah zeminde yavaş hareket eden loş çizgiler. XP esintili; sabit görselleri azaltır. Örtü, Chrome’un arka plan algısını etkileyebilir.") :
                I18n.T("Seçilen ekranlara saf siyah (#000000) örtü. Özel tam ekran moduna geçmez. Örtü, Chrome’un arka plan algısını etkileyebilir.");
        }
        private async Task Initialize()
        {
            start.Enabled = false;
            tray = new NotifyIcon { Text = I18n.T("Caffeine For OLED · Hazır"), Icon = SystemIcons.Application, Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add(I18n.T("Korumayı başlat"), null, async delegate { await StartProtection(); });
            menu.Items.Add(I18n.T("Korumayı durdur"), null, async delegate { await StopProtection(I18n.T("Koruma durduruldu."), false); });
            menu.Items.Add(I18n.T("Ayarları göster"), null, delegate { if (!active && !busy) { Show(); Activate(); } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(I18n.T("Çıkış"), null, async delegate { await ExitApplication(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += async delegate { if (active || busy) await StopProtection(I18n.T("Koruma durduruldu."), true); else { Show(); Activate(); } };
            keyHook = new ExitKeyHook(delegate { if (!IsDisposed) BeginInvoke(new Action(async delegate { await StopProtection(I18n.T("Koruma durduruldu; önceki parlaklık geri yüklendi."), false); })); });
            bool toggle = Native.RegisterHotKey(Handle, 1, 0x4003, 0x4F);
            bool fallback = Native.RegisterHotKey(Handle, 2, 0x4003, 0x08);
            timer.Tick += OnTimer; timer.Start();
            SystemEvents.SessionSwitch += OnSessionSwitch; SystemEvents.PowerModeChanged += OnPowerChanged; SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            bool recovered = await Task.Run(() => RecoveryGuard.RecoverPreviousSessions());
            previousRecoveryComplete = recovered;
            try
            {
                int count = await Task.Run(() => new WmiBrightness().Capture().Count);
                hardware.Text = count > 0 ? I18n.T("Donanım parlaklığı · ") + count + I18n.T(" desteklenen dahili ekran bulundu") : I18n.T("Donanım parlaklığı bulunamadı · Pitch black ve Old style kullanılabilir");
            }
            catch { hardware.Text = I18n.T("Donanım parlaklığı okunamadı · Siyah örtü modları kullanılabilir"); }
            SetStatus(!recovered ? I18n.T("Önceki parlaklık kurtarılamadı. Günlük: %LocalAppData%\\OLEDGuard") :
                !toggle || !fallback ? I18n.T("Bir global kısayol başka uygulamada kullanımda. Başlat düğmesi ve SPACE kullanılabilir.") : I18n.T("Hazır. Koruma yalnızca siz başlattığınızda çalışır."));
            initialized = true; start.Enabled = true;
        }
        private void OnTimer(object sender, EventArgs e)
        {
            if (!active && !busy)
            {
                IntPtr fg = Native.GetForegroundWindow(); uint pid;
                Native.GetWindowThreadProcessId(fg, out pid);
                if (pid != Process.GetCurrentProcess().Id && fg != IntPtr.Zero) previousWindow = fg;
            }
            if (!active) return;
            if (overlays != null) overlays.Raise();
            if (idleLock.Checked && (DateTime.UtcNow - lastPulse).TotalSeconds >= 20)
            {
                lastPulse = DateTime.UtcNow;
                if (!Native.PulseIdle() && !idlePulseFailed)
                { idlePulseFailed = true; Storage.Log("Idle reset failed."); tray.ShowBalloonTip(4000, "Caffeine For OLED", I18n.T("Otomatik kilidi önleme isteği reddedildi."), ToolTipIcon.Warning); }
            }
        }
        public async Task StartProtection()
        {
            if (preview || !initialized || busy || active || exitRequested || keyHook == null) return;
            if (brightness != null) { SetStatus(I18n.T("Önceki parlaklık henüz kurtarılamadı. Tepsi menüsünden Korumayı durdur seçeneğini yeniden deneyin.")); return; }
            Settings settings = ReadSettings();
            if (settings.Mode == SaverMode.BrightnessZero && !previousRecoveryComplete)
            { SetStatus(I18n.T("Önceki oturumun parlaklığı kurtarılamadı. Ekranları yeniden bağlayıp uygulamayı yeniden açın veya siyah örtü modunu kullanın.")); return; }
            try { Storage.SaveSettings(settings); }
            catch (Exception e) { SetStatus(I18n.T("Ayarlar kaydedilemedi: ") + e.Message); return; }
            busy = true; start.Enabled = false;
            if (cancellation != null) cancellation.Dispose();
            cancellation = new CancellationTokenSource(); keyHook.Armed = true;
            await gate.WaitAsync();
            try
            {
                Exception startupError = null;
                try
                {
                Hide();
                if (previousWindow != IntPtr.Zero && Native.IsWindow(previousWindow)) Native.SetForegroundWindow(previousWindow);
                await Task.Delay(200, cancellation.Token);
                Native.ApplyPower(settings);
                if (settings.Mode == SaverMode.BrightnessZero)
                {
                    string journal = Path.Combine(Storage.Root, "recovery-" + Guid.NewGuid().ToString("N") + ".json");
                    brightness = new BrightnessTransaction(new WmiBrightness(), new RecoveryGuard(), journal);
                    await Task.Run(() => brightness.Begin());
                }
                else
                {
                    overlays = new OverlayGroup(); overlays.Show(settings);
                    if (settings.CaptureCompatible && !overlays.CaptureCompatible)
                        throw new InvalidOperationException(I18n.T("Örtüyü ekran yakalamasından hariç tutma başarısız. Örtüsüz parlaklık modunu deneyin."));
                }
                cancellation.Token.ThrowIfCancellationRequested();
                active = true; lastPulse = DateTime.UtcNow; idlePulseFailed = false;
                tray.Text = I18n.T("Caffeine For OLED · Koruma etkin · SPACE ile dön");
                Storage.Log("Protection started: " + settings.Mode);
                }
                catch (Exception e) { startupError = e; }
                if (startupError != null)
                {
                    string restoreError = await Cleanup();
                    Show();
                    SetStatus((startupError is OperationCanceledException ? I18n.T("Başlatma iptal edildi.") : I18n.T("Başlatılamadı: ") + startupError.Message) + restoreError);
                    Storage.Log("Start: " + startupError.Message);
                }
            }
            finally { busy = false; start.Enabled = true; gate.Release(); }
        }
        public async Task StopProtection(string reason, bool showSettings)
        {
            if (cancellation != null) cancellation.Cancel();
            await gate.WaitAsync();
            try
            {
                if (!active && brightness == null && overlays == null) return;
                busy = true;
                string error = await Cleanup();
                SetStatus(reason + error);
                Storage.Log("Protection stopped" + error);
                if (showSettings || error.Length > 0) { Show(); if (showSettings) Activate(); }
                else if (!exitRequested) ShowWithoutStealingFocus();
            }
            finally { busy = false; gate.Release(); }
        }
        private void ShowWithoutStealingFocus()
        {
            // Stay in the tray after SPACE so Chrome keeps its foreground window.
            if (tray != null) tray.Text = I18n.T("Caffeine For OLED · Hazır · Çift tıkla: ayarlar");
        }
        private async Task<string> Cleanup()
        {
            active = false; if (keyHook != null) keyHook.Armed = false;
            string error = "";
            try { if (overlays != null) overlays.Dispose(); } catch (Exception e) { error = I18n.T(" Örtü: ") + e.Message; }
            overlays = null;
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            if (brightness != null)
            {
                try { await Task.Run(() => brightness.Restore()); brightness = null; }
                catch (Exception e) { error += I18n.T(" Parlaklık kurtarılamadı; yeniden durdurmayı veya uygulamadan çıkmayı deneyin. ") + e.Message; }
            }
            if (tray != null) tray.Text = I18n.T("Caffeine For OLED · Hazır");
            return error;
        }
        private async Task ExitApplication()
        {
            if (exitRequested) return; exitRequested = true;
            await StopProtection(I18n.T("Kapatılıyor."), false); allowClose = true; Close();
        }
        private async void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (preview || allowClose || e.CloseReason == CloseReason.WindowsShutDown) return;
            e.Cancel = true; await ExitApplication();
        }
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        { if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff) RequestStop(I18n.T("Oturum kilitlendi; koruma durduruldu.")); }
        private void OnPowerChanged(object sender, PowerModeChangedEventArgs e)
        { if (e.Mode == PowerModes.Suspend) RequestStop(I18n.T("Sistem askıya alındı; koruma durduruldu.")); }
        private void OnDisplayChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(async delegate
            {
                string selected = screen.SelectedIndex <= 0 ? "" : screenNames[screen.SelectedIndex - 1];
                await StopProtection(I18n.T("Ekran düzeni değişti; korumayı yeniden başlatın."), false);
                RefreshScreenChoices(selected); SetStatus(I18n.T("Ekran listesi yenilendi; korumayı yeniden başlatabilirsiniz."));
            }));
        }
        private void RequestStop(string reason)
        { if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(async delegate { await StopProtection(reason, false); })); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x8001) { if (!active && !busy) { Show(); Activate(); } return; }
            if (m.Msg == 0x312)
            {
                int id = m.WParam.ToInt32();
                BeginInvoke(new Action(async delegate
                { if (id == 2 || active || busy) await StopProtection(I18n.T("Kısayol ile durduruldu."), false); else await StartProtection(); })); return;
            }
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true; timer.Dispose();
                SystemEvents.SessionSwitch -= OnSessionSwitch; SystemEvents.PowerModeChanged -= OnPowerChanged; SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
                Native.UnregisterHotKey(Handle, 1); Native.UnregisterHotKey(Handle, 2);
                Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
                if (overlays != null) overlays.Dispose();
                if (keyHook != null) keyHook.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (cancellation != null) cancellation.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
