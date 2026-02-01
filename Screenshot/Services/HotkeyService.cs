using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

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

    // 가상 키 코드
    private const uint VK_SNAPSHOT = 0x2C;  // PrintScreen
    private const uint VK_S = 0x53;
    private const uint VK_D = 0x44;
    private const uint VK_W = 0x57;

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

    public bool RegisterHotkeys()
    {
        if (_windowHandle == IntPtr.Zero) return false;

        var registeredKeys = new List<int>();

        try
        {
            // PrintScreen - 전체 화면 캡처
            if (RegisterHotKey(_windowHandle, HOTKEY_FULLSCREEN, MOD_NONE, VK_SNAPSHOT))
            {
                registeredKeys.Add(HOTKEY_FULLSCREEN);
            }

            // Ctrl+Shift+S - 영역 선택 캡처
            if (RegisterHotKey(_windowHandle, HOTKEY_REGION, MOD_CONTROL | MOD_SHIFT, VK_S))
            {
                registeredKeys.Add(HOTKEY_REGION);
            }

            // Ctrl+Shift+D - 지연 캡처
            if (RegisterHotKey(_windowHandle, HOTKEY_DELAYED, MOD_CONTROL | MOD_SHIFT, VK_D))
            {
                registeredKeys.Add(HOTKEY_DELAYED);
            }

            // Ctrl+Shift+W - 창 캡처
            if (RegisterHotKey(_windowHandle, HOTKEY_WINDOW, MOD_CONTROL | MOD_SHIFT, VK_W))
            {
                registeredKeys.Add(HOTKEY_WINDOW);
            }

            // 모든 핫키가 등록되었는지 확인
            _isRegistered = registeredKeys.Count == 4;
            return _isRegistered;
        }
        catch
        {
            // 예외 발생 시 이미 등록된 핫키 해제
            foreach (var keyId in registeredKeys)
            {
                UnregisterHotKey(_windowHandle, keyId);
            }
            _isRegistered = false;
            return false;
        }
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
