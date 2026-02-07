using System.Drawing;

namespace Screenshot.Models;

/// <summary>
/// 캡처 결과를 담는 모델
/// </summary>
public class CaptureResult : IDisposable
{
    private bool _disposed;
    /// <summary>
    /// 캡처 성공 여부
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 캡처된 이미지 (Bitmap)
    /// </summary>
    public Bitmap? Image { get; set; }

    /// <summary>
    /// 캡처 시간
    /// </summary>
    public DateTime CapturedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 캡처 영역 (화면 좌표)
    /// </summary>
    public Rectangle CaptureArea { get; set; }

    /// <summary>
    /// 사용된 캡처 엔진 이름
    /// </summary>
    public string EngineName { get; set; } = string.Empty;

    /// <summary>
    /// 에러 메시지 (실패 시)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 저장된 파일 경로
    /// </summary>
    public string? SavedFilePath { get; set; }

    /// <summary>
    /// 모니터 인덱스
    /// </summary>
    public int MonitorIndex { get; set; }

    /// <summary>
    /// 리소스 해제
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Image?.Dispose();
        Image = null;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 캡처 모드
/// </summary>
public enum CaptureMode
{
    /// <summary>전체 화면 (모든 모니터)</summary>
    FullScreen,

    /// <summary>특정 모니터</summary>
    SingleMonitor,

    /// <summary>영역 선택</summary>
    Region,

    /// <summary>활성 창</summary>
    ActiveWindow,

    /// <summary>지연 캡처</summary>
    Delayed,

    /// <summary>스크롤 캡처</summary>
    Scroll
}
