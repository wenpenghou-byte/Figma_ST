using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FigmaSearch.Services;

/// <summary>
/// Global hotkey service supporting two modes:
/// 1. RegisterHotKey — for Modifier+Key combos (e.g. Ctrl+Space, Alt+Shift+F)
/// 2. Low-level keyboard hook — for double-tap modifier keys (e.g. DoubleAlt)
/// </summary>
public class HotkeyService : IDisposable
{
    // ── Win32 constants ──
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int WM_HOTKEY      = 0x0312;
    private const int VK_LMENU       = 0xA4;
    private const int HOTKEY_ID      = 0x4653; // "FS"

    // ── State ──
    private readonly Dispatcher _dispatcher;
    private string _currentConfig = "DoubleAlt";

    // Low-level hook state (for DoubleAlt)
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private DateTime _lastAltPress = DateTime.MinValue;
    private readonly TimeSpan _doubleTapInterval = TimeSpan.FromMilliseconds(300);

    // RegisterHotKey state (for modifier+key combos)
    private HwndSource? _hwndSource;
    private bool _hotkeyRegistered;

    public event EventHandler? HotkeyPressed;

    public string CurrentConfig => _currentConfig;

    public HotkeyService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Apply a hotkey configuration. Call on startup and when user changes settings.
    /// Format: "DoubleAlt" | "Ctrl+Space" | "Alt+Shift+F" etc.
    /// </summary>
    public void ApplyConfig(string config)
    {
        // Tear down previous
        UninstallAll();

        _currentConfig = config;

        if (string.Equals(config, "DoubleAlt", StringComparison.OrdinalIgnoreCase))
        {
            InstallLowLevelHook();
        }
        else
        {
            var parsed = ParseHotkey(config);
            if (parsed.HasValue)
                InstallRegisteredHotkey(parsed.Value.modifiers, parsed.Value.vk);
            else
                InstallLowLevelHook(); // fallback to DoubleAlt if parse fails
        }
    }

    // ── RegisterHotKey mode ─────────────────────────────────────

    private void InstallRegisteredHotkey(uint modifiers, uint vk)
    {
        // Need a hidden window for WndProc
        var helper = new WindowInteropHelper(Application.Current.MainWindow ?? new Window());
        if (helper.Handle == IntPtr.Zero)
            helper.EnsureHandle();

        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        if (_hwndSource == null)
        {
            // Fallback: create a message-only window
            var msgWin = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false };
            msgWin.Show();
            msgWin.Hide();
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(msgWin).Handle);
        }

        _hwndSource?.AddHook(WndProc);
        _hotkeyRegistered = RegisterHotKey(_hwndSource!.Handle, HOTKEY_ID, modifiers, vk);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ── Low-level hook mode (DoubleAlt) ─────────────────────────

    private void InstallLowLevelHook()
    {
        _proc = HookCallback;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module  = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName!), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            bool isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            if (vk == VK_LMENU && isDown)
            {
                var now = DateTime.Now;
                if ((now - _lastAltPress) < _doubleTapInterval)
                {
                    _lastAltPress = DateTime.MinValue;
                    _dispatcher.BeginInvoke(() => HotkeyPressed?.Invoke(this, EventArgs.Empty));
                }
                else
                {
                    _lastAltPress = now;
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // ── Cleanup ─────────────────────────────────────────────────

    private void UninstallAll()
    {
        // Unregister global hotkey
        if (_hotkeyRegistered && _hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            _hotkeyRegistered = false;
        }
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        // Unhook low-level keyboard hook
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _proc = null;
    }

    public void Dispose() => UninstallAll();

    // ── Hotkey string parsing ───────────────────────────────────

    /// <summary>
    /// Parse "Ctrl+Space", "Alt+Shift+F", etc. into Win32 modifiers + virtual key code.
    /// Returns null if the string can't be parsed.
    /// </summary>
    public static (uint modifiers, uint vk)? ParseHotkey(string config)
    {
        if (string.IsNullOrWhiteSpace(config)) return null;

        var parts = config.Split('+').Select(p => p.Trim()).ToArray();
        if (parts.Length == 0) return null;

        uint modifiers = 0;
        string? keyPart = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= 0x0002; // MOD_CONTROL
                    break;
                case "alt":
                    modifiers |= 0x0001; // MOD_ALT
                    break;
                case "shift":
                    modifiers |= 0x0004; // MOD_SHIFT
                    break;
                case "win":
                    modifiers |= 0x0008; // MOD_WIN
                    break;
                default:
                    keyPart = part;
                    break;
            }
        }

        if (keyPart == null) return null; // modifier-only combo like "Ctrl" alone
        modifiers |= 0x4000; // MOD_NOREPEAT

        // Convert key name to VK code
        if (Enum.TryParse<Key>(keyPart, ignoreCase: true, out var key))
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0) return (modifiers, (uint)vk);
        }

        // Try common names that don't map directly
        return keyPart.ToLowerInvariant() switch
        {
            "space"     => (modifiers, 0x20u),
            "enter"     => (modifiers, 0x0Du),
            "tab"       => (modifiers, 0x09u),
            "esc"       => (modifiers, 0x1Bu),
            "backspace" => (modifiers, 0x08u),
            _           => null
        };
    }

    /// <summary>
    /// Format a Key + ModifierKeys combination into a display/storage string.
    /// </summary>
    public static string FormatHotkey(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))     parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))   parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        var keyName = key switch
        {
            Key.Space     => "Space",
            Key.Return    => "Enter",
            Key.Tab       => "Tab",
            Key.Escape    => "Esc",
            Key.Back      => "Backspace",
            _             => key.ToString()
        };
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    // ── P/Invoke ────────────────────────────────────────────────

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc proc, IntPtr mod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
