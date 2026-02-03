using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Screenshot.Models;

namespace Screenshot.Services.Capture;

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
        CaptureLogger.Info("Init", "=== CaptureManager 초기화 ===");
        CaptureLogger.LogDiagnostics();

        _engines = new List<ICaptureEngine>();
        
        // 1. DXGI 캡처 (최우선)
        try
        {
            var dxgiCapture = new DxgiCapture();
            CaptureLogger.LogEngineInit("DXGI", dxgiCapture.IsAvailable);
            
            if (dxgiCapture.IsAvailable)
            {
                _engines.Add(dxgiCapture);
            }
        }
        catch (Exception ex)
        {
            CaptureLogger.Error("Init", "DXGI 초기화 실패", ex);
        }
        
        // 2. GDI 캡처 (Fallback)
        _engines.Add(new GdiCapture());
        CaptureLogger.LogEngineInit("GDI", true, "Fallback");
        
        // WinRT는 COM 마샬링 문제로 일단 제외
        CaptureLogger.Info("Init", "WinRT 비활성화 (COM 마샬링 문제)");
        
        CaptureLogger.Info("Init", $"엔진: {string.Join(", ", _engines.Select(e => $"{e.Name}(P{e.Priority})"))}");
        CaptureLogger.Info("Init", "=== 초기화 완료 ===");
    }

    public IReadOnlyList<ICaptureEngine> AvailableEngines =>
        _engines.Where(e => e.IsAvailable).ToList().AsReadOnly();

    public ICaptureEngine? LastSuccessfulEngine => _lastSuccessfulEngine;

    public async Task<CaptureResult> CaptureFullScreenAsync()
    {
        CaptureLogger.Info("Capture", "=== 전체 화면 캡처 ===");
        return await Task.Run(() => ExecuteCapture(e => e.CaptureFullScreen(), "FullScreen"));
    }

    public async Task<CaptureResult> CaptureMonitorAsync(int monitorIndex)
    {
        CaptureLogger.Info("Capture", $"=== 모니터 {monitorIndex} ===");
        return await Task.Run(() => ExecuteCapture(e => e.CaptureMonitor(monitorIndex), $"Monitor({monitorIndex})"));
    }

    public async Task<CaptureResult> CaptureRegionAsync(Rectangle region)
    {
        CaptureLogger.Info("Capture", $"=== 영역 캡처: {region} ===");
        return await Task.Run(() => ExecuteCapture(e => e.CaptureRegion(region), "Region", region));
    }

    public async Task<CaptureResult> CaptureActiveWindowAsync()
    {
        CaptureLogger.Info("Capture", "=== 활성 창 캡처 ===");
        return await Task.Run(() => ExecuteCapture(e => e.CaptureActiveWindow(), "ActiveWindow"));
    }

    /// <summary>
    /// 특정 창 캡처 (WindowCaptureService 통합)
    /// </summary>
    public async Task<CaptureResult> CaptureWindowAsync(IntPtr hWnd)
    {
        CaptureLogger.Info("Capture", $"=== 창 캡처: {hWnd} ===");
        
        return await Task.Run(() =>
        {
            // 1. 먼저 엔진들로 시도 (DXGI/GDI)
            var result = ExecuteCapture(e => e.CaptureActiveWindow(), "Window");
            if (result.Success && result.Image != null)
            {
                return result;
            }

            // 2. 엔진 실패시 WindowCaptureService 사용 (PrintWindow)
            try
            {
                var windowService = new WindowCaptureService();
                var bitmap = windowService.CaptureWindow(hWnd);
                
                if (bitmap != null && !IsBlackImage(bitmap))
                {
                    CaptureLogger.Info("Capture", "WindowCaptureService로 캡처 성공");
                    return new CaptureResult
                    {
                        Success = true,
                        Image = bitmap,
                        EngineName = "PrintWindow",
                        CapturedAt = DateTime.Now
                    };
                }
                
                bitmap?.Dispose();
            }
            catch (Exception ex)
            {
                CaptureLogger.Error("Capture", "WindowCaptureService 실패", ex);
            }

            return new CaptureResult
            {
                Success = false,
                EngineName = "None",
                ErrorMessage = "모든 창 캡처 방법 실패"
            };
        });
    }

    private CaptureResult ExecuteCapture(Func<ICaptureEngine, CaptureResult> captureFunc, string mode, Rectangle? region = null)
    {
        var sw = Stopwatch.StartNew();
        var errors = new List<string>();

        // 마지막 성공 엔진 먼저 시도
        if (_lastSuccessfulEngine != null)
        {
            CaptureLogger.Info("Capture", $"마지막 성공 엔진: {_lastSuccessfulEngine.Name}, IsAvailable={_lastSuccessfulEngine.IsAvailable}");
            
            if (_lastSuccessfulEngine.IsAvailable)
            {
                StatusChanged?.Invoke(this, $"{_lastSuccessfulEngine.Name}으로 캡처 중...");
                CaptureLogger.LogCaptureAttempt(_lastSuccessfulEngine.Name, mode, region);
                
                try
                {
                    var result = captureFunc(_lastSuccessfulEngine);

                    if (result.Success && result.Image != null && !IsBlackImage(result.Image))
                    {
                        sw.Stop();
                        CaptureLogger.Info("Capture", $"성공 ({_lastSuccessfulEngine.Name}, {sw.ElapsedMilliseconds}ms)");
                        ProcessCaptureResult(result);
                        return result;
                    }
                    
                    if (result.Image != null)
                    {
                        result.Image.Dispose();
                        errors.Add($"{_lastSuccessfulEngine.Name}: 검은 화면");
                    }
                    else
                    {
                        errors.Add($"{_lastSuccessfulEngine.Name}: {result.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{_lastSuccessfulEngine.Name}: {ex.Message}");
                    CaptureLogger.Error("Capture", $"[{_lastSuccessfulEngine.Name}] 예외", ex);
                    // 예외 발생시 해당 엔진 초기화
                    _lastSuccessfulEngine = null;
                }
            }
            else
            {
                CaptureLogger.Warn("Capture", $"마지막 성공 엔진 {_lastSuccessfulEngine.Name}을 사용할 수 없음, 순회 모드로 전환");
                _lastSuccessfulEngine = null; // 사용 불가하면 초기화
            }
        }
        else
        {
            CaptureLogger.Info("Capture", "마지막 성공 엔진 없음, 모든 엔진 순회");
        }

        // 모든 엔진 순회
        foreach (var engine in _engines.Where(e => e.IsAvailable && e != _lastSuccessfulEngine))
        {
            StatusChanged?.Invoke(this, $"{engine.Name}으로 캡처 중...");
            CaptureLogger.LogCaptureAttempt(engine.Name, mode, region);

            try
            {
                var result = captureFunc(engine);

                if (result.Success && result.Image != null && !IsBlackImage(result.Image))
                {
                    _lastSuccessfulEngine = engine;
                    sw.Stop();
                    CaptureLogger.Info("Capture", $"성공 ({engine.Name}, {sw.ElapsedMilliseconds}ms)");
                    ProcessCaptureResult(result);
                    return result;
                }

                if (result.Image != null)
                {
                    result.Image.Dispose();
                    errors.Add($"{engine.Name}: 검은 화면");
                }
                else
                {
                    errors.Add($"{engine.Name}: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{engine.Name}: {ex.Message}");
                CaptureLogger.Error("Capture", $"[{engine.Name}] 예외", ex);
            }
        }

        // 모든 엔진 실패
        sw.Stop();
        StatusChanged?.Invoke(this, "캡처 실패");
        var errorMsg = "모든 캡처 방법 실패:\n" + string.Join("\n", errors);
        CaptureLogger.Error("Capture", $"실패 ({sw.ElapsedMilliseconds}ms): {errorMsg}");
        
        return new CaptureResult
        {
            Success = false,
            EngineName = "None",
            ErrorMessage = errorMsg
        };
    }

    private bool IsBlackImage(Bitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return true;

        int sampleCount = Math.Min(20, Math.Max(5, bitmap.Width * bitmap.Height / 100));
        int blackCount = 0;
        var random = new Random();

        for (int i = 0; i < sampleCount; i++)
        {
            int x = random.Next(bitmap.Width);
            int y = random.Next(bitmap.Height);

            try
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R < 15 && pixel.G < 15 && pixel.B < 15)
                    blackCount++;
            }
            catch { }
        }

        var ratio = (double)blackCount / sampleCount;
        var isBlack = ratio >= 0.85;
        
        if (isBlack)
        {
            CaptureLogger.LogBlackImageDetection("Validator", sampleCount, blackCount, ratio);
        }

        return isBlack;
    }

    private void ProcessCaptureResult(CaptureResult result)
    {
        if (!result.Success || result.Image == null) return;

        try
        {
            if (_settings.CopyToClipboard)
            {
                CopyToClipboard(result.Image);
            }

            if (_settings.AutoSave)
            {
                result.SavedFilePath = SaveToFile(result.Image);
            }

            CaptureCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            CaptureLogger.Error("PostProcess", "후처리 실패", ex);
        }
    }

    private void CopyToClipboard(Bitmap image)
    {
        try
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                System.Windows.Clipboard.SetImage(ConvertToBitmapSource(image));
            });
        }
        catch (Exception ex)
        {
            CaptureLogger.Error("Clipboard", "복사 실패", ex);
        }
    }

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

            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private string SaveToFile(Bitmap image)
    {
        var saveFolder = _settings.SaveFolder;
        if (_settings.OrganizeByDate)
        {
            saveFolder = Path.Combine(saveFolder, DateTime.Now.ToString("yyyy-MM-dd"));
        }

        if (!Directory.Exists(saveFolder))
        {
            Directory.CreateDirectory(saveFolder);
        }

        var fileName = GenerateFileName();
        var extension = _settings.ImageFormat.ToLower() switch
        {
            "jpg" or "jpeg" => ".jpg",
            "bmp" => ".bmp",
            "webp" => ".webp",
            _ => ".png"
        };

        var filePath = Path.Combine(saveFolder, fileName + extension);

        int counter = 1;
        while (File.Exists(filePath))
        {
            filePath = Path.Combine(saveFolder, $"{fileName}_{counter}{extension}");
            counter++;
        }

        var format = _settings.ImageFormat.ToLower() switch
        {
            "jpg" or "jpeg" => ImageFormat.Jpeg,
            "bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Png
        };

        if (format == ImageFormat.Jpeg)
        {
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

    private string GenerateFileName()
    {
        var now = DateTime.Now;
        return _settings.FileNamePattern
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{time}", now.ToString("HHmmss"))
            .Replace("{datetime}", now.ToString("yyyy-MM-dd_HHmmss"));
    }

    public void Dispose()
    {
        CaptureLogger.Info("Dispose", "리소스 정리");
        foreach (var engine in _engines)
        {
            engine.Dispose();
        }
        _engines.Clear();
        CaptureLogger.FlushToFile();
        GC.SuppressFinalize(this);
    }
}
