using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Screenshot.Models;

namespace Screenshot.Services;

/// <summary>
/// Win32 API를 사용한 전역 단축키 서비스
/// </summary>
public class HotkeyService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // 수정자 키
    private const uint MOD_NONE = 0x0000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    // 단축키 ID
    public const int HOTKEY_FULLSCREEN = 1;
    public const int HOTKEY_REGION = 2;
    public const int HOTKEY_DELAYED = 3;
    public const int HOTKEY_WINDOW = 4;

    private IntPtr _windowHandle;
    private HwndSource? _source;
    private readonly Dictionary<int, Action> _hotkeyActions = new();
    private bool _isRegistered;

    public event Action? FullScreenCapture;
    public event Action? RegionCapture;
    public event Action? DelayedCapture;
    public event Action? WindowCapture;

    public bool IsRegistered => _isRegistered;

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;

        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(HwndHook);
    }

    /// <summary>
    /// 기본 단축키로 등록 (하위 호환)
    /// </summary>
    public bool RegisterHotkeys()
    {
        return RegisterHotkeys(null);
    }

    /// <summary>
    /// AppSettings의 사용자 설정 단축키로 등록
    /// </summary>
    public bool RegisterHotkeys(AppSettings? settings)
    {
        if (_windowHandle == IntPtr.Zero) return false;

        // 기존 등록 해제
        UnregisterHotkeys();

        var registeredKeys = new List<int>();

        try
        {
            // 전체 화면 캡처
            var (fsMod, fsVk) = ResolveHotkey(settings?.FullScreenHotkey, MOD_NONE, 0x2C /*VK_SNAPSHOT*/);
            if (RegisterHotKey(_windowHandle, HOTKEY_FULLSCREEN, fsMod, fsVk))
                registeredKeys.Add(HOTKEY_FULLSCREEN);

            // 영역 선택 캡처
            var (rMod, rVk) = ResolveHotkey(settings?.RegionHotkey, MOD_CONTROL | MOD_SHIFT, 0x53 /*VK_S*/);
            if (RegisterHotKey(_windowHandle, HOTKEY_REGION, rMod, rVk))
                registeredKeys.Add(HOTKEY_REGION);

            // 지연 캡처
            var (dMod, dVk) = ResolveHotkey(settings?.DelayedHotkey, MOD_CONTROL | MOD_SHIFT, 0x44 /*VK_D*/);
            if (RegisterHotKey(_windowHandle, HOTKEY_DELAYED, dMod, dVk))
                registeredKeys.Add(HOTKEY_DELAYED);

            // 창 캡처
            var (wMod, wVk) = ResolveHotkey(settings?.ActiveWindowHotkey, MOD_CONTROL | MOD_SHIFT, 0x57 /*VK_W*/);
            if (RegisterHotKey(_windowHandle, HOTKEY_WINDOW, wMod, wVk))
                registeredKeys.Add(HOTKEY_WINDOW);

            _isRegistered = registeredKeys.Count == 4;
            return _isRegistered;
        }
        catch
        {
            foreach (var keyId in registeredKeys)
            {
                UnregisterHotKey(_windowHandle, keyId);
            }
            _isRegistered = false;
            return false;
        }
    }

    /// <summary>
    /// HotkeySettings → Win32 수정자+가상키 변환. settings가 null이면 기본값 사용
    /// </summary>
    private static (uint modifiers, uint vk) ResolveHotkey(HotkeySettings? hs, uint defaultMod, uint defaultVk)
    {
        if (hs == null || string.IsNullOrWhiteSpace(hs.Key))
            return (defaultMod, defaultVk);

        uint mod = MOD_NONE;
        if (hs.Ctrl) mod |= MOD_CONTROL;
        if (hs.Shift) mod |= MOD_SHIFT;
        if (hs.Alt) mod |= MOD_ALT;

        uint vk = KeyNameToVk(hs.Key);
        if (vk == 0) return (defaultMod, defaultVk); // 변환 실패 시 기본값

        return (mod, vk);
    }

    /// <summary>
    /// 키 이름을 Win32 가상 키 코드로 변환
    /// </summary>
    private static uint KeyNameToVk(string keyName)
    {
        return keyName.ToUpperInvariant() switch
        {
            "PRINTSCREEN" or "SNAPSHOT" => 0x2C,
            "A" => 0x41, "B" => 0x42, "C" => 0x43, "D" => 0x44,
            "E" => 0x45, "F" => 0x46, "G" => 0x47, "H" => 0x48,
            "I" => 0x49, "J" => 0x4A, "K" => 0x4B, "L" => 0x4C,
            "M" => 0x4D, "N" => 0x4E, "O" => 0x4F, "P" => 0x50,
            "Q" => 0x51, "R" => 0x52, "S" => 0x53, "T" => 0x54,
            "U" => 0x55, "V" => 0x56, "W" => 0x57, "X" => 0x58,
            "Y" => 0x59, "Z" => 0x5A,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "0" => 0x30, "1" => 0x31, "2" => 0x32, "3" => 0x33,
            "4" => 0x34, "5" => 0x35, "6" => 0x36, "7" => 0x37,
            "8" => 0x38, "9" => 0x39,
            _ => 0
        };
    }

    public void UnregisterHotkeys()
    {
        if (_windowHandle == IntPtr.Zero) return;

        UnregisterHotKey(_windowHandle, HOTKEY_FULLSCREEN);
        UnregisterHotKey(_windowHandle, HOTKEY_REGION);
        UnregisterHotKey(_windowHandle, HOTKEY_DELAYED);
        UnregisterHotKey(_windowHandle, HOTKEY_WINDOW);

        _isRegistered = false;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();

            switch (id)
            {
                case HOTKEY_FULLSCREEN:
                    FullScreenCapture?.Invoke();
                    handled = true;
                    break;
                case HOTKEY_REGION:
                    RegionCapture?.Invoke();
                    handled = true;
                    break;
                case HOTKEY_DELAYED:
                    DelayedCapture?.Invoke();
                    handled = true;
                    break;
                case HOTKEY_WINDOW:
                    WindowCapture?.Invoke();
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotkeys();
        _source?.RemoveHook(HwndHook);
        _source?.Dispose();
    }
}
