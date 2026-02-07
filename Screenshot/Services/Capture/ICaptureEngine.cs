using System.Drawing;
using Screenshot.Models;

namespace Screenshot.Services.Capture;

/// <summary>
/// 캡처 엔진 인터페이스
/// </summary>
public interface ICaptureEngine : IDisposable
{
    /// <summary>
    /// 엔진 이름
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 엔진 우선순위 (낮을수록 먼저 시도)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 엔진 사용 가능 여부
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 전체 화면 캡처 (모든 모니터)
    /// </summary>
    CaptureResult CaptureFullScreen();

    /// <summary>
    /// 특정 모니터 캡처
    /// </summary>
    CaptureResult CaptureMonitor(int monitorIndex);

    /// <summary>
    /// 특정 영역 캡처
    /// </summary>
    CaptureResult CaptureRegion(Rectangle region);

    /// <summary>
    /// 활성 창 캡처
    /// </summary>
    CaptureResult CaptureActiveWindow();
}

/// <summary>
/// 캡처 엔진 기본 구현
/// </summary>
public abstract class CaptureEngineBase : ICaptureEngine
{
    public abstract string Name { get; }
    public abstract int Priority { get; }
    public virtual bool IsAvailable => true;

    public abstract CaptureResult CaptureFullScreen();
    public abstract CaptureResult CaptureMonitor(int monitorIndex);
    public abstract CaptureResult CaptureRegion(Rectangle region);

    /// <summary>
    /// 활성 창 캡처 (기본 구현 - 전체 화면으로 대체)
    /// </summary>
    public virtual CaptureResult CaptureActiveWindow()
    {
        // Win32 API 없이 전체 화면 캡처로 대체
        return CaptureFullScreen();
    }

    #region 공통 유틸리티

    // 검은 화면 체크용 랜덤 (스레드 안전)
    private static readonly Random SharedRandom = new();
    private static readonly object RandomLock = new();

    /// <summary>
    /// 이미지가 검은 화면인지 확인 (공통 구현, static으로 외부에서도 호출 가능)
    /// </summary>
    public static bool IsBlackImage(Bitmap bitmap, int threshold = 15, double blackRatio = 0.85)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return true;

        int sampleCount = Math.Min(20, Math.Max(5, bitmap.Width * bitmap.Height / 100));
        int blackCount = 0;

        lock (RandomLock)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                int x = SharedRandom.Next(bitmap.Width);
                int y = SharedRandom.Next(bitmap.Height);

                try
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.R < threshold && pixel.G < threshold && pixel.B < threshold)
                    {
                        blackCount++;
                    }
                }
                catch
                {
                    // 픽셀 읽기 실패 시 무시
                }
            }
        }

        return blackCount >= sampleCount * blackRatio;
    }

    #endregion

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
