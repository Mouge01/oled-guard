# Caffeine For OLED · 0.2.1 alpha

[English](README.md) · [İndir](https://github.com/Mouge01/oled-guard/releases)

Windows 11, Debian, Fedora ve Arch Linux için elle başlatılan OLED ekran koruyucu. **Türkçe / English** dil seçimi anında uygulanır ve kaydedilir.

## Kurulum

- **Windows 11:** Windows ZIP'ini çıkarın. Çalışan 0.1 sürümünden tepsi menüsündeki **Çıkış** ile çıkın. `CaffeineForOLED.exe` dosyasını açın; `.exe.config` yanında kalsın.
- **Debian:** `sudo apt install ./caffeine-for-oled_0.2.1.alpha-1_all.deb`
- **Fedora:** `sudo dnf install ./caffeine-for-oled-0.2.1-0.1.alpha.noarch.rpm`
- **Arch:** `sudo pacman -U ./caffeine-for-oled-0.2.1.alpha-1-any.pkg.tar.zst`

Linux'ta uygulama menüsünden **Caffeine For OLED** veya terminalden `caffeine-for-oled` ile açılır. Bağımlılıklar dağıtımın paket yöneticisi tarafından kurulur. Grafik arayüzü normal kullanıcıyla çalıştırın. Otomatik başlangıç, sistem hizmeti veya ayrıcalık kazandıran bir yardımcı kurulmaz.

Uygulamanın önceki adı OLED Guard idi. Eski ayar ve parlaklık kurtarma dizinleri korunur. Linux'ta `oled-guard` komutu da çalışmaya devam eder; yeni paketler eski `oled-guard` paketinin yerini alır.

## Kullanım

**Pitch black**, **Old style** veya **Parlaklık %0** seçin ve başlatın. Windows ve X11'de **SPACE** geri döndürür, **ESC** yedek çıkıştır, **Ctrl+Alt+O** başlatır/durdurur. Windows'ta **Ctrl+Alt+Backspace** de kullanılabilir.

Dil seçicisinden İngilizce veya Türkçe seçebilirsiniz. Linux'ta koruma durmuşken `caffeine-for-oled --language en` veya `caffeine-for-oled --language tr` de kullanılabilir.

SPACE sonrası Windows tepside kalabilir; ayarlar için simgeye çift tıklayın. Linux'ta ayarları açmak için uygulamayı menüden yeniden başlatın. `caffeine-for-oled --stop` komutu kullanıcı tanımlı masaüstü kısayoluna atanabilir.

**USES için:** Örtüsüz donanım parlaklığını seçen USES profilini kullanın. Chrome'u ve ders sekmesini açık bırakın. Windows / X11'de ders penceresine dönüp Ctrl+Alt+O kullanabilirsiniz. Örtü, tarayıcının arka plan algısını etkileyebilir.

**%0 parlaklık her panelde tamamen siyah değildir.** Donanım modu yalnızca desteklenen dahili aygıtları denetler; harici ekranları otomatik korumaz. Siyah örtü RGB sıfır çizer, fakat fiziksel panelin tamamen sönmesi ve kalıcı yanmayı kesin önleme garantisi verilmez.

## Linux: X11 ve Wayland

X11'de örtüler odağı almaz ve seçili ekranları örter. Global SPACE/ESC yalnızca koruma sırasında yakalanır.

Wayland'de örtüsüz parlaklık modu, masaüstünün **GlobalShortcuts portalı** üzerinden onaylanan çıkış kısayolu olmadan başlamaz. SPACE önerilir; masaüstü farklı bir kısayol atarsa izin penceresinde gösterilen tuşu kullanın. Durdurunca portal oturumu kapatılır ve SPACE normal kullanımda yakalanmaz.

Wayland örtüsü odağı alabilir. Çoklu ekran örtüsü, GTK layer-shell destekleyen bir masaüstü gerektirir. Destek yoksa tek ekran seçin veya donanım parlaklığını kullanın; uygulama eksik kapsama ile başlamaz. Linux'ta ekran yakalamadan hariç tutma özelliği yoktur.

Uyku ve ekran/boşta kalma engellemesi masaüstü inhibitörleriyle yapılır. Bazı masaüstleri ekran kapanması ile otomatik kilidi birlikte ele alır. Reddedilen istek hata olarak gösterilir. Win+L benzeri elle kilitleme ve zorunlu kurum kuralları aşılmaz. Linux sürümü sahte klavye/fare girdisi göndermez.

## Kurtarma ve sınırlar

Parlaklık değişmeden önce eski değerler kaydedilir. Ayrı yardımcı süreç, ana uygulama kapanırsa geri yüklemeyi dener. Hata oluşursa kayıt silinmez; sonraki açılışta yeniden denenir. Windows kayıtları `%LocalAppData%\OLEDGuard`, Linux kayıtları `~/.local/state/oled-guard` veya `$XDG_STATE_HOME/oled-guard` altındadır.

Gerçek Linux OLED donanımı, Wayland masaüstü ve USES ders geçişi uçtan uca henüz doğrulanmadı. Yerel testler, sahte parlaklık aygıtları ve izole X11 görüntüsü kullanır. Ayrıntılar [VALIDATION.md](VALIDATION.md) dosyasındadır.

Geliştirme komutları, özellik tablosu ve teknik kaynaklar [İngilizce belgede](README.md) yer alır. GNU GPLv3 lisansı geçerlidir; [LICENSE](LICENSE) dosyasını inceleyebilirsiniz.
