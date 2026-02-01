using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Screenshot.Models;

namespace Screenshot.Services.Capture;

/// <summary>
/// 캡처 엔진 관리자
/// 여러 캡처 엔진을 우선순위에 따라 시도하고 Fallback 처리
/// </summary>
public class CaptureManager : IDisposable
{
    private readonly List<ICaptureEngine> _engines;
    private readonly AppSettings _settings;
    private ICaptureEngine? _lastSuccessfulEngine;

    public event EventHandler<CaptureResult>? CaptureCompleted;
    public event EventHandler<string>? StatusChanged;

    public CaptureManager(AppSettings settings)
    {
        _settings = settings;

        // 캡처 엔진 우선순위: WinRT > DXGI > GDI (Fallback)
        // WinRT: Windows 10 1903+ 하드웨어 가속, DLP 후킹 우회, 테두리 없음
        // DXGI: 하드웨어 레벨 직접 접근, DRM 우회 가능
        // GDI: 레거시 Fallback (모든 Windows 지원)
        _engines = new List<ICaptureEngine>();
        
        // 1. WinRT 캡처 (최신 Windows, 최우선)
        try
        {
            var winRtCapture = new WinRtCapture();
            if (winRtCapture.IsAvailable)
            {
                _engines.Add(winRtCapture);
                System.Diagnostics.Debug.WriteLine("WinRT 캡처 엔진 등록됨");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WinRT 캡처 초기화 실패: {ex.Message}");
        }
        
        // 2. DXGI 캡처 (하드웨어 레벨)
        try
        {
            var dxgiCapture = new DxgiCapture();
            if (dxgiCapture.IsAvailable)
            {
                _engines.Add(dxgiCapture);
                System.Diagnostics.Debug.WriteLine("DXGI 캡처 엔진 등록됨");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DXGI 캡처 초기화 실패: {ex.Message}");
        }
        
        // 3. GDI 캡처 (Fallback, 항상 추가)
        _engines.Add(new GdiCapture());
        System.Diagnostics.Debug.WriteLine("GDI 캡처 엔진 등록됨");
        
        // 우선순위에 따라 정렬 (낮은 값이 높은 우선순위)
        _engines = _engines.OrderBy(e => e.Priority).ToList();
        
        System.Diagnostics.Debug.WriteLine($"등록된 캡처 엔진: {string.Join(", ", _engines.Select(e => $"{e.Name}(P{e.Priority})"))}");
    }

    /// <summary>
    /// 사용 가능한 엔진 목록
    /// </summary>
    public IReadOnlyList<ICaptureEngine> AvailableEngines =>
        _engines.Where(e => e.IsAvailable).ToList().AsReadOnly();

    /// <summary>
    /// 마지막으로 성공한 엔진
    /// </summary>
    public ICaptureEngine? LastSuccessfulEngine => _lastSuccessfulEngine;

    /// <summary>
    /// 전체 화면 캡처
    /// </summary>
    public async Task<CaptureResult> CaptureFullScreenAsync()
    {
        return await Task.Run(() => ExecuteWithFallback(e => e.CaptureFullScreen()));
    }

    /// <summary>
    /// 특정 모니터 캡처
    /// </summary>
    public async Task<CaptureResult> CaptureMonitorAsync(int monitorIndex)
    {
        return await Task.Run(() => ExecuteWithFallback(e => e.CaptureMonitor(monitorIndex)));
    }

    /// <summary>
    /// 영역 캡처
    /// </summary>
    public async Task<CaptureResult> CaptureRegionAsync(Rectangle region)
    {
        return await Task.Run(() => ExecuteWithFallback(e => e.CaptureRegion(region)));
    }

    /// <summary>
    /// 활성 창 캡처
    /// </summary>
    public async Task<CaptureResult> CaptureActiveWindowAsync()
    {
        return await Task.Run(() => ExecuteWithFallback(e => e.CaptureActiveWindow()));
    }

    /// <summary>
    /// Fallback 로직으로 캡처 실행
    /// </summary>
    private CaptureResult ExecuteWithFallback(Func<ICaptureEngine, CaptureResult> captureFunc)
    {
        // 마지막 성공 엔진이 있으면 먼저 시도
        if (_lastSuccessfulEngine != null && _lastSuccessfulEngine.IsAvailable)
        {
            StatusChanged?.Invoke(this, $"{_lastSuccessfulEngine.Name}으로 캡처 시도 중...");
            var result = captureFunc(_lastSuccessfulEngine);
            if (result.Success)
            {
                ProcessCaptureResult(result);
                return result;
            }
        }

        // 모든 엔진을 우선순위 순으로 시도
        var errors = new List<string>();

        foreach (var engine in _engines.Where(e => e.IsAvailable && e != _lastSuccessfulEngine))
        {
            StatusChanged?.Invoke(this, $"{engine.Name}으로 캡처 시도 중...");

            try
            {
                var result = captureFunc(engine);

                if (result.Success)
                {
                    _lastSuccessfulEngine = engine;
                    ProcessCaptureResult(result);
                    StatusChanged?.Invoke(this, $"캡처 성공 ({engine.Name})");
                    return result;
                }

                errors.Add($"{engine.Name}: {result.ErrorMessage}");
            }
            catch (Exception ex)
            {
                errors.Add($"{engine.Name}: {ex.Message}");
            }
        }

        // 모든 엔진 실패
        StatusChanged?.Invoke(this, "모든 캡처 방법 실패");
        return new CaptureResult
        {
            Success = false,
            EngineName = "None",
            ErrorMessage = $"모든 캡처 방법 실패:\n{string.Join("\n", errors)}"
        };
    }

    /// <summary>
    /// 캡처 결과 처리 (저장, 클립보드 등)
    /// </summary>
    private void ProcessCaptureResult(CaptureResult result)
    {
        if (!result.Success || result.Image == null) return;

        try
        {
            // 클립보드에 복사
            if (_settings.CopyToClipboard)
            {
                CopyToClipboard(result.Image);
            }

            // 파일로 저장
            if (_settings.AutoSave)
            {
                result.SavedFilePath = SaveToFile(result.Image);
            }

            // 캡처 완료 이벤트 발생
            CaptureCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"캡처 후처리 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 클립보드에 이미지 복사
    /// </summary>
    private void CopyToClipboard(Bitmap image)
    {
        try
        {
            // WPF 애플리케이션의 경우 STA 스레드에서 실행해야 함
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Clipboard.SetImage(ConvertToBitmapSource(image));
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"클립보드 복사 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// Bitmap을 BitmapSource로 변환
    /// </summary>
    private System.Windows.Media.Imaging.BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var source = System.Windows.Media.Imaging.BitmapSource.Create(
                bitmap.Width, bitmap.Height,
                96, 96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                bitmapData.Scan0,
                bitmapData.Stride * bitmap.Height,
                bitmapData.Stride);

            source.Freeze(); // 스레드 안전
            return source;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    /// <summary>
    /// 파일로 저장
    /// </summary>
    private string SaveToFile(Bitmap image)
    {
        // 저장 폴더 확인/생성
        var saveFolder = _settings.SaveFolder;
        if (_settings.OrganizeByDate)
        {
            saveFolder = Path.Combine(saveFolder, DateTime.Now.ToString("yyyy-MM-dd"));
        }

        if (!Directory.Exists(saveFolder))
        {
            Directory.CreateDirectory(saveFolder);
        }

        // 파일명 생성
        var fileName = GenerateFileName();
        var extension = _settings.ImageFormat.ToLower() switch
        {
            "jpg" or "jpeg" => ".jpg",
            "bmp" => ".bmp",
            "webp" => ".webp",
            _ => ".png"
        };

        var filePath = Path.Combine(saveFolder, fileName + extension);

        // 중복 파일명 처리
        int counter = 1;
        while (File.Exists(filePath))
        {
            filePath = Path.Combine(saveFolder, $"{fileName}_{counter}{extension}");
            counter++;
        }

        // 저장
        var format = _settings.ImageFormat.ToLower() switch
        {
            "jpg" or "jpeg" => ImageFormat.Jpeg,
            "bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Png
        };

        if (format == ImageFormat.Jpeg)
        {
            // JPEG 품질 설정
            var encoder = ImageCodecInfo.GetImageEncoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality, (long)_settings.JpegQuality);
            image.Save(filePath, encoder, encoderParams);
        }
        else
        {
            image.Save(filePath, format);
        }

        return filePath;
    }

    /// <summary>
    /// 파일명 생성
    /// </summary>
    private string GenerateFileName()
    {
        var now = DateTime.Now;
        return _settings.FileNamePattern
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{time}", now.ToString("HHmmss"))
            .Replace("{datetime}", now.ToString("yyyy-MM-dd_HHmmss"));
    }

    /// <summary>
    /// 특정 엔진으로 직접 캡처 (테스트용)
    /// </summary>
    public CaptureResult CaptureWithEngine(string engineName, CaptureMode mode, Rectangle? region = null)
    {
        var engine = _engines.FirstOrDefault(e =>
            e.Name.Equals(engineName, StringComparison.OrdinalIgnoreCase));

        if (engine == null)
        {
            return new CaptureResult
            {
                Success = false,
                ErrorMessage = $"엔진을 찾을 수 없습니다: {engineName}"
            };
        }

        return mode switch
        {
            CaptureMode.FullScreen => engine.CaptureFullScreen(),
            CaptureMode.Region when region.HasValue => engine.CaptureRegion(region.Value),
            CaptureMode.ActiveWindow => engine.CaptureActiveWindow(),
            _ => engine.CaptureFullScreen()
        };
    }

    public void Dispose()
    {
        foreach (var engine in _engines)
        {
            engine.Dispose();
        }
        _engines.Clear();
        GC.SuppressFinalize(this);
    }
}
