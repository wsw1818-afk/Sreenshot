using System.Drawing;
using System.Windows.Forms;

namespace Screenshot.Services;

/// <summary>
/// DPI 처리 및 다중 모니터 좌표 보정 헬퍼
/// Win32 API 최소화 - System.Windows.Forms.Screen 사용
/// </summary>
public static class DpiHelper
{
    /// <summary>
    /// 모든 모니터 정보 가져오기
    /// </summary>
    public static List<MonitorInfo> GetAllMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var screens = Screen.AllScreens;

        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            monitors.Add(new MonitorInfo
            {
                Index = i,
                DeviceName = screen.DeviceName,
                Bounds = screen.Bounds,
                WorkArea = screen.WorkingArea,
                DpiScale = 1.0, // 기본값
                IsPrimary = screen.Primary
            });
        }

        return monitors;
    }

    /// <summary>
    /// 가상 화면 전체 영역 (모든 모니터 합침)
    /// </summary>
    public static Rectangle GetVirtualScreenBounds()
    {
        return SystemInformation.VirtualScreen;
    }

    /// <summary>
    /// 기본 모니터 정보 가져오기
    /// </summary>
    public static MonitorInfo? GetPrimaryMonitor()
    {
        return GetAllMonitors().FirstOrDefault(m => m.IsPrimary);
    }
}

/// <summary>
/// 모니터 정보
/// </summary>
public class MonitorInfo
{
    public int Index { get; set; }
    public IntPtr Handle { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public Rectangle Bounds { get; set; }
    public Rectangle WorkArea { get; set; }
    public double DpiScale { get; set; } = 1.0;
    public bool IsPrimary { get; set; }

    public override string ToString()
    {
        var primary = IsPrimary ? " (Primary)" : "";
        return $"Monitor {Index + 1}{primary}: {Bounds.Width}x{Bounds.Height}";
    }
}
