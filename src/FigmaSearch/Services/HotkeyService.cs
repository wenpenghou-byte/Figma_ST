using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace FigmaSearch.Services;

public class HotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int VK_LMENU       = 0xA4;

    private IntPtr _hookId = IntPtr.Zero;
    private DateTime _lastAltPress = DateTime.MinValue;
    private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(300);
    private LowLevelKeyboardProc? _proc;
    private readonly Dispatcher _dispatcher;

    public event EventHandler? DoubleAltPressed;

    public HotkeyService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = HookCallback;
        Install();
    }

    private void Install()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module  = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc!, GetModuleHandle(module.ModuleName!), 0);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            bool isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            if (vk == VK_LMENU && isDown)
            {
                var now = DateTime.Now;
                if ((now - _lastAltPress) < _interval)
                {
                    _lastAltPress = DateTime.MinValue;
                    _dispatcher.BeginInvoke(() => DoubleAltPressed?.Invoke(this, EventArgs.Empty));
                }
                else
                {
                    _lastAltPress = now;
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc proc, IntPtr mod, uint threadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
