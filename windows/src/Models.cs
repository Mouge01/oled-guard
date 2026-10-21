using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace OledGuard
{
    public enum SaverMode { PitchBlack, OldStyle, BrightnessZero }
    [DataContract]
    public sealed class Settings
    {
        [DataMember] public string Language = "";
        [DataMember] public SaverMode Mode = SaverMode.PitchBlack;
        [DataMember] public bool PreventSleep = true;
        [DataMember] public bool PreventDisplayOff = true;
        [DataMember] public bool PreventIdleLock = false;
        [DataMember] public bool CaptureCompatible = true;
        [DataMember] public string ScreenName = "";
        public static Settings UsesProfile()
        { return new Settings { Mode = SaverMode.BrightnessZero }; }
    }
    [DataContract]
    public sealed class BrightnessSnapshot
    {
        [DataMember] public string InstanceName;
        [DataMember] public byte Brightness;
    }
    public static class Storage
    {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OLEDGuard");
        public static T Read<T>(string path)
        { using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }
        public static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static Settings LoadSettings()
        {
            try
            {
                Settings s = Read<Settings>(Path.Combine(Root, "settings.json"));
                if (!Enum.IsDefined(typeof(SaverMode), s.Mode)) return new Settings();
                return s;
            }
            catch { return new Settings(); }
        }
        public static void SaveSettings(Settings s) { Write(Path.Combine(Root, "settings.json"), s); }
        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Root); string p = Path.Combine(Root, "events.log");
                if (File.Exists(p) && new FileInfo(p).Length > 262144) File.Delete(p);
                File.AppendAllText(p, DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }
    public interface IBrightnessBackend
    { List<BrightnessSnapshot> Capture(); void Set(string instanceName, byte brightness); }
    public interface IRecoveryGuard : IDisposable { void Start(string journal); }
    // Journal and watchdog must both be ready BEFORE changing any display.
    public sealed class BrightnessTransaction
    {
        private readonly IBrightnessBackend backend;
        private readonly IRecoveryGuard guard;
        private readonly string journal;
        private List<BrightnessSnapshot> original;
        public BrightnessTransaction(IBrightnessBackend backend, IRecoveryGuard guard, string journal)
        { this.backend = backend; this.guard = guard; this.journal = journal; }
        public void Begin()
        {
            original = backend.Capture();
            if (original == null || original.Count == 0)
                throw new InvalidOperationException(I18n.T("Donanım parlaklığı desteklenmiyor. Pitch black modunu seçin."));
            Storage.Write(journal, original);
            try { guard.Start(journal); foreach (var m in original) backend.Set(m.InstanceName, 0); }
            catch { try { Restore(); } catch { } throw; }
        }
        public void Restore()
        {
            if (original == null) return;
            var errors = new List<Exception>();
            foreach (var m in original)
            { try { backend.Set(m.InstanceName, m.Brightness); } catch (Exception e) { errors.Add(e); } }
            if (errors.Count != 0) throw new AggregateException(I18n.T("Parlaklık geri yüklenemedi; kurtarma kaydı saklandı."), errors);
            if (File.Exists(journal)) File.Delete(journal);
            original = null; guard.Dispose();
        }
        public static bool Recover(IBrightnessBackend backend, string journal)
        {
            if (!File.Exists(journal)) return true;
            var originals = Storage.Read<List<BrightnessSnapshot>>(journal);
            bool success = true;
            foreach (var m in originals)
            { try { backend.Set(m.InstanceName, m.Brightness); } catch (Exception e) { success = false; Storage.Log("Recovery: " + e.Message); } }
            if (success) File.Delete(journal);
            return success;
        }
    }
}
