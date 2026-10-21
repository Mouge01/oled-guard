using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Reflection;
using System.Threading;

namespace OledGuard
{
    public sealed class WmiBrightness : IBrightnessBackend
    {
        private static ManagementObjectSearcher Query(string className)
        {
            return new ManagementObjectSearcher("root\\WMI", "SELECT * FROM " + className,
                new EnumerationOptions { Timeout = TimeSpan.FromSeconds(4), ReturnImmediately = false });
        }
        public List<BrightnessSnapshot> Capture()
        {
            var result = new List<BrightnessSnapshot>();
            using (var search = Query("WmiMonitorBrightness"))
            using (var rows = search.Get())
                foreach (ManagementObject row in rows)
                    using (row)
                        if (Convert.ToBoolean(row["Active"]))
                            result.Add(new BrightnessSnapshot { InstanceName = (string)row["InstanceName"],
                                Brightness = Convert.ToByte(row["CurrentBrightness"]) });
            return result;
        }
        public void Set(string instanceName, byte brightness)
        {
            if (brightness > 100) throw new ArgumentOutOfRangeException("brightness");
            using (var search = Query("WmiMonitorBrightnessMethods"))
            using (var rows = search.Get())
                foreach (ManagementObject row in rows)
                    using (row)
                    {
                        if (!string.Equals((string)row["InstanceName"], instanceName, StringComparison.OrdinalIgnoreCase)) continue;
                        using (var args = row.GetMethodParameters("WmiSetBrightness"))
                        {
                            args["Timeout"] = (uint)0; args["Brightness"] = brightness;
                            using (var output = row.InvokeMethod("WmiSetBrightness", args,
                                new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(4) }))
                                if (output == null || Convert.ToUInt32(output["ReturnValue"]) != 0)
                                    throw new InvalidOperationException(I18n.T("Ekran parlaklık değişikliğini reddetti."));
                        }
                        return;
                    }
            throw new InvalidOperationException(I18n.T("Parlaklığı denetlenen ekran artık bağlı değil."));
        }
    }
    public sealed class RecoveryGuard : IRecoveryGuard
    {
        private Process process;
        public void Start(string journal)
        {
            string eventName = "Local\\OLEDGuard.Ready." + Guid.NewGuid().ToString("N");
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
            using (var parent = Process.GetCurrentProcess())
            {
                var info = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                    "--watchdog " + parent.Id + " " + parent.StartTime.ToUniversalTime().Ticks + " \"" + journal + "\" \"" + eventName + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                process = Process.Start(info);
                if (process == null || !ready.WaitOne(6000))
                    throw new InvalidOperationException(I18n.T("Parlaklık kurtarma süreci başlatılamadı; ekran karartılmadı."));
            }
        }
        public void Dispose() { if (process != null) { process.Dispose(); process = null; } }
        public static int Run(string[] args)
        {
            try
            {
                if (args.Length != 5) return 2;
                int pid = int.Parse(args[1], CultureInfo.InvariantCulture);
                long ticks = long.Parse(args[2], CultureInfo.InvariantCulture);
                string journal = Path.GetFullPath(args[3]);
                if (!string.Equals(Path.GetDirectoryName(journal), Storage.Root, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(journal).StartsWith("recovery-", StringComparison.Ordinal)) return 3;
                Process parent = null;
                try { parent = Process.GetProcessById(pid); if (parent.StartTime.ToUniversalTime().Ticks != ticks) { parent.Dispose(); parent = null; } }
                catch (ArgumentException) { }
                using (var ready = EventWaitHandle.OpenExisting(args[4])) ready.Set();
                if (parent != null)
                    using (parent)
                        while (File.Exists(journal) && !parent.WaitForExit(500)) { }
                for (int i = 0; i < 3 && File.Exists(journal); i++)
                {
                    if (BrightnessTransaction.Recover(new WmiBrightness(), journal)) return 0;
                    Thread.Sleep(700);
                }
                return File.Exists(journal) ? 1 : 0;
            }
            catch (Exception e) { Storage.Log("Watchdog: " + e.Message); return 1; }
        }
        public static bool RecoverPreviousSessions()
        {
            if (!Directory.Exists(Storage.Root)) return true;
            bool success = true;
            foreach (string file in Directory.GetFiles(Storage.Root, "recovery-*.json"))
                try { if (!BrightnessTransaction.Recover(new WmiBrightness(), file)) success = false; }
                catch (Exception e) { success = false; Storage.Log("Startup recovery: " + e.Message); }
            return success;
        }
    }
}
