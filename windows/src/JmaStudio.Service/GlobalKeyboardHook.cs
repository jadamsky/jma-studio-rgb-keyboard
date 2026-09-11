// A minimal WH_KEYBOARD_LL global keyboard hook -- same implementation
// proven in JmaStudio.HardwareTest during interactive testing this
// session, now the real production version backing InputListener.cs
// (the C# analogue of Python's daemon/input_listener.py). Same narrow-
// scope principle as the Python original: this only ever produces a
// key NAME on keydown, nothing about content, and nothing persisted.

using System.Runtime.InteropServices;

namespace JmaStudio.Service;

public sealed class GlobalKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdllHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX, PtY;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const uint WmQuit = 0x0012;

    // Kept as a field so the delegate passed to SetWindowsHookEx isn't
    // garbage-collected while the unmanaged hook still holds a pointer
    // to it -- a real and easy-to-hit bug with P/Invoke callbacks.
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookHandle;
    private Thread? _messageLoopThread;
    private uint _messageLoopThreadId;

    /// <summary>Fired on the hook's own message-loop thread, not the
    /// caller's -- keep handlers fast and thread-aware.</summary>
    public event Action<string>? KeyDown;

    public GlobalKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        var ready = new ManualResetEventSlim();
        _messageLoopThread = new Thread(() =>
        {
            _messageLoopThreadId = GetCurrentThreadId();
            _hookHandle = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
            if (_hookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                ready.Set();
                throw new InvalidOperationException($"SetWindowsHookEx failed, Win32 error {error}.");
            }
            ready.Set();

            // A low-level keyboard hook only actually receives events
            // while its installing thread runs a message pump.
            while (GetMessage(out Msg msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        })
        {
            IsBackground = true,
        };
        _messageLoopThread.SetApartmentState(ApartmentState.STA);
        _messageLoopThread.Start();
        ready.Wait();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WmKeydown || wParam == WmSyskeydown))
        {
            var data = Marshal.PtrToStructure<KbdllHookStruct>(lParam);
            string? name = WindowsKeyMap.Resolve((int)data.VkCode, data.Flags);
            if (name != null)
            {
                KeyDown?.Invoke(name);
            }
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        if (_messageLoopThreadId != 0)
        {
            PostThreadMessage(_messageLoopThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
