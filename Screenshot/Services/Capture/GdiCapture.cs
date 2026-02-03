using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Screenshot.Models;

namespace Screenshot.Services.Capture;

/// <summary>
/// GDI 캡처 엔진 - BitBlt 사용 (CopyFromScreen보다 안정적)
/// </summary>
public class GdiCapture : CaptureEngineBase
{
    // Win32 API
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    public override string Name => "GDI Capture";
    public override int Priority => 10; // DXGI 다음

    public override CaptureResult CaptureFullScreen()
    {
        var bounds = DpiHelper.GetVirtualScreenBounds();
        return CaptureRegion(bounds);
    }

    public override CaptureResult CaptureMonitor(int monitorIndex)
    {
        var monitors = DpiHelper.GetAllMonitors();
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
        {
            return new CaptureResult
            {
                Success = false,
                EngineName = Name,
                ErrorMessage = $"잘못된 모니터 인덱스: {monitorIndex}"
            };
        }

        return CaptureRegion(monitors[monitorIndex].Bounds);
    }

    public override CaptureResult CaptureRegion(Rectangle region)
    {
        CaptureLogger.LogGdi($"영역 캡처: {region}");
        
        try
        {
            if (region.Width <= 0 || region.Height <= 0)
            {
                return new CaptureResult
                {
                    Success = false,
                    EngineName = Name,
                    ErrorMessage = "캡처 영역이 유효하지 않습니다."
                };
            }

            // 방법 1: BitBlt (Win32 API) - 더 안정적
            var result = CaptureWithBitBlt(region);
            if (result.Success && result.Image != null && !IsBlackImage(result.Image))
            {
                CaptureLogger.LogGdi("BitBlt 캡처 성공");
                return result;
            }

            // BitBlt 실패시 이미지 정리
            result.Image?.Dispose();
            CaptureLogger.Warn("GDI", "BitBlt 실패, CopyFromScreen으로 폴백");

            // 방법 2: CopyFromScreen (Fallback)
            Thread.Sleep(100);
            result = CaptureWithCopyFromScreen(region);
            
            if (result.Success && result.Image != null && !IsBlackImage(result.Image))
            {
                return result;
            }

            // 재시도 한 번 더
            result.Image?.Dispose();
            CaptureLogger.Warn("GDI", "재시도 중...");
            Thread.Sleep(300);
            
            result = CaptureWithBitBlt(region);
            if (result.Success && result.Image != null && !IsBlackImage(result.Image))
            {
                return result;
            }

            // 모든 방법 실패
            result.Image?.Dispose();
            return new CaptureResult
            {
                Success = false,
                EngineName = Name,
                ErrorMessage = "GDI 캡처 결과가 검은 화면입니다. (BitBlt, CopyFromScreen 모두 실패)"
            };
        }
        catch (Exception ex)
        {
            CaptureLogger.LogGdi("캡처 예외", ex);
            return new CaptureResult
            {
                Success = false,
                EngineName = Name,
                ErrorMessage = $"GDI 캡처 예외: {ex.Message}"
            };
        }
    }

    private CaptureResult CaptureWithBitBlt(Rectangle region)
    {
        IntPtr hWndDesktop = IntPtr.Zero;
        IntPtr hdcSrc = IntPtr.Zero;
        IntPtr hdcDest = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;
        Bitmap? bitmap = null;

        try
        {
            hWndDesktop = GetDesktopWindow();
            hdcSrc = GetWindowDC(hWndDesktop);
            hdcDest = CreateCompatibleDC(hdcSrc);
            hBitmap = CreateCompatibleBitmap(hdcSrc, region.Width, region.Height);
            hOld = SelectObject(hdcDest, hBitmap);

            // BitBlt로 화면 캡처 (CAPTUREBLT 플래그로 레이어드 창도 포함)
            bool success = BitBlt(
                hdcDest, 0, 0, region.Width, region.Height,
                hdcSrc, region.X, region.Y, SRCCOPY | CAPTUREBLT);

            if (!success)
            {
                return new CaptureResult
                {
                    Success = false,
                    EngineName = Name,
                    ErrorMessage = "BitBlt 실패"
                };
            }

            // Bitmap으로 변환
            bitmap = Image.FromHbitmap(hBitmap);
            
            return new CaptureResult
            {
                Success = true,
                Image = bitmap,
                CaptureArea = region,
                EngineName = Name,
                CapturedAt = DateTime.Now
            };
        }
        finally
        {
            // 리소스 정리
            if (hOld != IntPtr.Zero) SelectObject(hdcDest, hOld);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero) DeleteDC(hdcDest);
            if (hdcSrc != IntPtr.Zero) ReleaseDC(hWndDesktop, hdcSrc);
        }
    }

    private CaptureResult CaptureWithCopyFromScreen(Rectangle region)
    {
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(
                region.X, region.Y, 
                0, 0, 
                new Size(region.Width, region.Height),
                CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
        }

        return new CaptureResult
        {
            Success = true,
            Image = bitmap,
            CaptureArea = region,
            EngineName = Name,
            CapturedAt = DateTime.Now
        };
    }

    public override CaptureResult CaptureActiveWindow()
    {
        // 활성 창 캡처는 전체 화면으로 대체
        return CaptureFullScreen();
    }
}
