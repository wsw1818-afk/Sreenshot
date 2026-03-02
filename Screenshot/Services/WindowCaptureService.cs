using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Screenshot.Services;

/// <summary>
/// 특정 창 캡처 서비스
/// </summary>
public class WindowCaptureService
{
    #region Win32 API

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const uint SRCCOPY = 0x00CC0020;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    #endregion

    /// <summary>
    /// 열려있는 창 정보
    /// </summary>
    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = "";
        public Rectangle Bounds { get; set; }
    }

    /// <summary>
    /// 모든 보이는 창 목록 가져오기
    /// </summary>
    public List<WindowInfo> GetVisibleWindows()
    {
        var windows = new List<WindowInfo>();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var titleLength = GetWindowTextLength(hWnd);
            if (titleLength == 0) return true;

            var titleBuilder = new StringBuilder(titleLength + 1);
            GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            var title = titleBuilder.ToString();

            // 시스템 창 제외
            if (string.IsNullOrWhiteSpace(title)) return true;
            if (title == "Program Manager") return true;

            if (!GetWindowRect(hWnd, out RECT rect)) return true;

            var bounds = new Rectangle(rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top);

            // 너무 작은 창 제외
            if (bounds.Width < 100 || bounds.Height < 100) return true;

            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                Bounds = bounds
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <summary>
    /// 현재 활성 창 가져오기
    /// </summary>
    public WindowInfo? GetForegroundWindowInfo()
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return null;

        var titleLength = GetWindowTextLength(hWnd);
        var titleBuilder = new StringBuilder(titleLength + 1);
        GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);

        // DWM으로 실제 창 크기 가져오기 (그림자 제외)
        Rectangle bounds;
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT dwmRect, Marshal.SizeOf<RECT>()) == 0)
        {
            bounds = new Rectangle(dwmRect.Left, dwmRect.Top,
                dwmRect.Right - dwmRect.Left, dwmRect.Bottom - dwmRect.Top);
        }
        else if (GetWindowRect(hWnd, out RECT rect))
        {
            bounds = new Rectangle(rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        else
        {
            return null;
        }

        return new WindowInfo
        {
            Handle = hWnd,
            Title = titleBuilder.ToString(),
            Bounds = bounds
        };
    }

    /// <summary>
    /// 핸들로 창 정보 가져오기
    /// </summary>
    public WindowInfo? GetWindowInfoByHandle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return null;

        var titleLength = GetWindowTextLength(hWnd);
        var titleBuilder = new StringBuilder(titleLength + 1);
        GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);

        Rectangle bounds;
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT dwmRect, Marshal.SizeOf<RECT>()) == 0)
        {
            bounds = new Rectangle(dwmRect.Left, dwmRect.Top,
                dwmRect.Right - dwmRect.Left, dwmRect.Bottom - dwmRect.Top);
        }
        else if (GetWindowRect(hWnd, out RECT rect))
        {
            bounds = new Rectangle(rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        else
        {
            return null;
        }

        return new WindowInfo
        {
            Handle = hWnd,
            Title = titleBuilder.ToString(),
            Bounds = bounds
        };
    }

    /// <summary>
    /// 특정 창 캡처
    /// </summary>
    public Bitmap? CaptureWindow(IntPtr hWnd)
    {
        // DWM으로 실제 창 크기 가져오기
        Rectangle bounds;
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT dwmRect, Marshal.SizeOf<RECT>()) == 0)
        {
            bounds = new Rectangle(dwmRect.Left, dwmRect.Top,
                dwmRect.Right - dwmRect.Left, dwmRect.Bottom - dwmRect.Top);
        }
        else if (GetWindowRect(hWnd, out RECT rect))
        {
            bounds = new Rectangle(rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        else
        {
            return null;
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowCapture] 잘못된 bounds: {bounds}");
            return null;
        }

        Screenshot.Services.Capture.CaptureLogger.Info("WindowCapture", $"hWnd=0x{hWnd:X}, bounds={bounds}");

        // 1. PrintWindow 방식 시도 (여러 플래그로 시도, 내부에서 IsBlackImage 체크)
        var bitmap = TryPrintWindow(hWnd, bounds);
        if (bitmap != null)
        {
            Screenshot.Services.Capture.CaptureLogger.Info("WindowCapture", $"PrintWindow 성공: {bitmap.Width}x{bitmap.Height}");
            return bitmap;
        }
        Screenshot.Services.Capture.CaptureLogger.Info("WindowCapture", "PrintWindow 실패 (모든 플래그)");

        // 2. BitBlt(WindowDC) 방식 시도
        bitmap = TryBitBltCapture(hWnd, bounds);
        if (bitmap != null && !IsBlackImage(bitmap))
        {
            Screenshot.Services.Capture.CaptureLogger.Info("WindowCapture", $"BitBlt(WindowDC) 성공: {bitmap.Width}x{bitmap.Height}");
            return bitmap;
        }
        bitmap?.Dispose();
        Screenshot.Services.Capture.CaptureLogger.Info("WindowCapture", "BitBlt(WindowDC) 실패");

        // 3. 최종: 대상 창을 전면에 올린 후 화면 DC에서 해당 영역 크롭
        Screenshot.Services.Capture.CaptureLogger.Info("WindowCapture", "DesktopCrop 시도...");
        bitmap = TryDesktopCropCapture(hWnd, bounds);
        Screenshot.Services.Capture.CaptureLogger.Info("WindowCapture", $"DesktopCrop 결과: {(bitmap != null ? $"{bitmap.Width}x{bitmap.Height}" : "null (검은 화면)")}");
        return bitmap;
    }

    /// <summary>
    /// 활성 창 캡처
    /// </summary>
    public Bitmap? CaptureActiveWindow()
    {
        var windowInfo = GetForegroundWindowInfo();
        if (windowInfo == null) return null;

        return CaptureWindow(windowInfo.Handle);
    }

    private Bitmap? TryPrintWindow(IntPtr hWnd, Rectangle bounds)
    {
        // 여러 플래그로 시도: PW_RENDERFULLCONTENT(2) → 0(기본) → PW_CLIENTONLY(1)
        uint[] flags = { PW_RENDERFULLCONTENT, 0, 1 };

        foreach (var flag in flags)
        {
            try
            {
                var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    var hdc = g.GetHdc();
                    try
                    {
                        bool ok = PrintWindow(hWnd, hdc, flag);
                        System.Diagnostics.Debug.WriteLine($"[WindowCapture] PrintWindow(flag={flag}): {ok}");
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                if (!IsBlackImage(bitmap))
                {
                    System.Diagnostics.Debug.WriteLine($"[WindowCapture] PrintWindow(flag={flag}): 성공");
                    return bitmap;
                }

                System.Diagnostics.Debug.WriteLine($"[WindowCapture] PrintWindow(flag={flag}): 검은 화면");
                bitmap.Dispose();
            }
            catch
            {
                // 다음 플래그 시도
            }
        }

        return null;
    }

    private Bitmap? TryBitBltCapture(IntPtr hWnd, Rectangle bounds)
    {
        IntPtr hdcWindow = IntPtr.Zero;
        IntPtr hdcMem = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            hdcWindow = GetWindowDC(hWnd);
            if (hdcWindow == IntPtr.Zero) return null;

            hdcMem = CreateCompatibleDC(hdcWindow);
            hBitmap = CreateCompatibleBitmap(hdcWindow, bounds.Width, bounds.Height);
            hOld = SelectObject(hdcMem, hBitmap);

            BitBlt(hdcMem, 0, 0, bounds.Width, bounds.Height, hdcWindow, 0, 0, SRCCOPY);

            SelectObject(hdcMem, hOld);
            hOld = IntPtr.Zero; // finally에서 이중 호출 방지
            return Image.FromHbitmap(hBitmap);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero)
                SelectObject(hdcMem, hOld);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hdcMem != IntPtr.Zero)
                DeleteDC(hdcMem);
            if (hdcWindow != IntPtr.Zero)
                ReleaseDC(hWnd, hdcWindow);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    private const uint CAPTUREBLT = 0x40000000;

    /// <summary>
    /// 대상 창을 전면에 올린 후 화면 DC에서 해당 영역을 BitBlt 크롭
    /// PrintWindow/WindowDC BitBlt이 모두 검은 화면인 경우의 최종 폴백
    /// </summary>
    private Bitmap? TryDesktopCropCapture(IntPtr hWnd, Rectangle bounds)
    {
        try
        {
            // 최소화된 창이면 복원
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
                Thread.Sleep(500);
            }

            // 대상 창을 전면에 (SmartCapture는 이미 Hide() 상태)
            SetForegroundWindow(hWnd);

            // 전면에 올라올 때까지 대기 (최대 1초)
            for (int wait = 0; wait < 10; wait++)
            {
                Thread.Sleep(100);
                if (GetForegroundWindow() == hWnd) break;
            }

            // 추가 대기: DWM이 창을 완전히 렌더링할 시간
            Thread.Sleep(200);

            // DWM으로 최신 bounds 재확인
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT dwmRect, Marshal.SizeOf<RECT>()) == 0)
            {
                bounds = new Rectangle(dwmRect.Left, dwmRect.Top,
                    dwmRect.Right - dwmRect.Left, dwmRect.Bottom - dwmRect.Top);
            }

            System.Diagnostics.Debug.WriteLine($"[WindowCapture] DesktopCrop: foreground=0x{GetForegroundWindow():X}, target=0x{hWnd:X}, bounds={bounds}");

            // 최대 3회 시도 (검은 화면이면 대기 후 재시도)
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var bmp = CaptureScreenRegion(bounds);

                if (bmp != null && !IsBlackImage(bmp))
                {
                    System.Diagnostics.Debug.WriteLine($"[WindowCapture] DesktopCrop 시도 {attempt + 1}: 성공");
                    return bmp;
                }

                System.Diagnostics.Debug.WriteLine($"[WindowCapture] DesktopCrop 시도 {attempt + 1}: {(bmp == null ? "null" : "검은 화면")}");
                bmp?.Dispose();

                if (attempt < 2)
                    Thread.Sleep(300 * (attempt + 1));
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 화면 DC(GetDC(null))에서 지정 영역을 BitBlt로 캡처
    /// GetWindowDC(GetDesktopWindow())와 달리 전체 가상 화면을 커버합니다.
    /// </summary>
    private Bitmap? CaptureScreenRegion(Rectangle bounds)
    {
        IntPtr hdcSrc = IntPtr.Zero;
        IntPtr hdcDest = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            // GetDC(null) = 전체 화면 DC (모든 모니터 포함)
            hdcSrc = GetDC(IntPtr.Zero);
            if (hdcSrc == IntPtr.Zero) return null;

            hdcDest = CreateCompatibleDC(hdcSrc);
            hBitmap = CreateCompatibleBitmap(hdcSrc, bounds.Width, bounds.Height);
            hOld = SelectObject(hdcDest, hBitmap);

            BitBlt(hdcDest, 0, 0, bounds.Width, bounds.Height,
                hdcSrc, bounds.X, bounds.Y, SRCCOPY | CAPTUREBLT);

            SelectObject(hdcDest, hOld);
            hOld = IntPtr.Zero;
            return Image.FromHbitmap(hBitmap);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hOld != IntPtr.Zero && hdcDest != IntPtr.Zero)
                SelectObject(hdcDest, hOld);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero)
                DeleteDC(hdcDest);
            if (hdcSrc != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, hdcSrc);
        }
    }

    private bool IsBlackImage(Bitmap bitmap)
    {
        // 샘플링으로 검은색 확인
        int blackPixels = 0;
        int sampleCount = 100;

        for (int i = 0; i < sampleCount; i++)
        {
            int x = (bitmap.Width * i) / sampleCount;
            int y = (bitmap.Height * i) / sampleCount;

            if (x < bitmap.Width && y < bitmap.Height)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R < 10 && pixel.G < 10 && pixel.B < 10)
                {
                    blackPixels++;
                }
            }
        }

        return blackPixels > sampleCount * 0.9;
    }
}
