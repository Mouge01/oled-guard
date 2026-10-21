using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace OledGuard
{
    public static class Native
    {
        public const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 1, ES_DISPLAY_REQUIRED = 2;
        [DllImport("kernel32.dll", SetLastError = true)] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint id, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        public delegate IntPtr HookProc(int code, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wparam, IntPtr lparam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION data; }
        [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mouse; [FieldOffset(0)] public KEYBDINPUT keyboard; }
        [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT
        { public int dx, dy; public uint mouseData, flags, time; public UIntPtr extraInfo; }
        [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT
        { public ushort vk, scan; public uint flags, time; public UIntPtr extraInfo; }
        public static uint PowerFlags(Settings s)
        { return ES_CONTINUOUS | (s.PreventSleep ? ES_SYSTEM_REQUIRED : 0) | (s.PreventDisplayOff ? ES_DISPLAY_REQUIRED : 0); }
        public static void ApplyPower(Settings s)
        {
            if (SetThreadExecutionState(PowerFlags(s)) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), I18n.T("Windows güç isteğini kabul etmedi."));
        }
        public static bool PulseIdle()
        {
            var input = new INPUT { type = 0, data = new INPUTUNION { mouse = new MOUSEINPUT { flags = 1 } } };
            return SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1;
        }
    }
    // Separate pump: emergency exit never waits for a brightness driver call.
    public sealed class ExitKeyHook : IDisposable
    {
        private readonly Thread thread;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private readonly Action stop;
        private Native.HookProc callback;
        private IntPtr hook;
        private uint threadId;
        private Exception failure;
        private volatile bool armed;
        private int consumedKey;
        public bool Armed { get { return armed; } set { armed = value; } }
        public ExitKeyHook(Action stop)
        {
            this.stop = stop;
            thread = new Thread(Run) { IsBackground = true, Name = "OLED Guard exit key" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            if (!ready.WaitOne(4000)) throw new InvalidOperationException(I18n.T("SPACE dinleyicisi başlatılamadı."));
            if (failure != null) throw new InvalidOperationException(I18n.T("SPACE dinleyicisi kurulamadı."), failure);
        }
        private void Run()
        {
            threadId = Native.GetCurrentThreadId(); callback = OnKey;
            hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) { failure = new Win32Exception(); ready.Set(); return; }
            using (var queue = new Control())
            { var handle = queue.Handle; ready.Set(); Application.Run(); }
            Native.UnhookWindowsHookEx(hook);
        }
        private IntPtr OnKey(int code, IntPtr wparam, IntPtr lparam)
        {
            if (code >= 0)
            {
                int key = Marshal.ReadInt32(lparam);
                bool down = wparam.ToInt32() == 0x100 || wparam.ToInt32() == 0x104;
                bool up = wparam.ToInt32() == 0x101 || wparam.ToInt32() == 0x105;
                if (consumedKey == key) { if (up) consumedKey = 0; return new IntPtr(1); }
                if (armed && down && (key == 0x20 || key == 0x1B))
                {
                    consumedKey = key; armed = false;
                    try { stop(); } catch { }
                    return new IntPtr(1);
                }
            }
            return Native.CallNextHookEx(hook, code, wparam, lparam);
        }
        public void Dispose()
        { armed = false; Native.PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero); thread.Join(2000); ready.Dispose(); }
    }
}
