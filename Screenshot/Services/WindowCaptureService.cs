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

        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        // PrintWindow 방식 시도
        var bitmap = TryPrintWindow(hWnd, bounds);
        if (bitmap != null && !IsBlackImage(bitmap))
        {
            return bitmap;
        }
        bitmap?.Dispose();

        // BitBlt 방식 시도
        return TryBitBltCapture(hWnd, bounds);
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
        try
        {
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                var hdc = g.GetHdc();
                try
                {
                    PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
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
