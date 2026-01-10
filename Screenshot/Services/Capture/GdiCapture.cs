using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Screenshot.Models;

namespace Screenshot.Services.Capture;

/// <summary>
/// 기본 GDI 캡처 엔진
/// CopyFromScreen만 사용 (Win32 API, SendKeys 제거)
/// </summary>
public class GdiCapture : CaptureEngineBase
{
    public override string Name => "GDI Capture";
    public override int Priority => 4;

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
                ErrorMessage = $"모니터 인덱스가 잘못되었습니다: {monitorIndex}"
            };
        }

        var monitor = monitors[monitorIndex];
        return CaptureRegion(monitor.Bounds);
    }

    public override CaptureResult CaptureRegion(Rectangle region)
    {
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

            // CopyFromScreen 사용 (순수 .NET API)
            var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(region.X, region.Y, 0, 0,
                    new Size(region.Width, region.Height),
                    CopyPixelOperation.SourceCopy);
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
        catch (Exception ex)
        {
            return new CaptureResult
            {
                Success = false,
                EngineName = Name,
                ErrorMessage = $"GDI 캡처 예외: {ex.Message}"
            };
        }
    }

    public override CaptureResult CaptureActiveWindow()
    {
        // 활성 창 캡처는 전체 화면 캡처로 대체
        return CaptureFullScreen();
    }
}
