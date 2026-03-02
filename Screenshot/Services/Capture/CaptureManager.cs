using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
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
        CaptureLogger.CleanupOldLogs();
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

    /// <summary>
    /// 이벤트 없이 전체 가상 화면 캡처 (영역 선택용)
    /// 다중 모니터 환경에서 VirtualScreen 전체를 캡처합니다.
    /// GDI BitBlt을 우선 시도 (안정적), DXGI는 폴백으로 사용합니다.
    /// </summary>
    public async Task<CaptureResult> CaptureFullScreenRawAsync()
    {
        CaptureLogger.Info("Capture", "=== 전체 가상 화면 캡처 (Raw, 영역선택용) ===");
        var virtualBounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        CaptureLogger.Info("Capture", $"VirtualScreen: {virtualBounds}");
        return await Task.Run(() => ExecuteCaptureRawWithGdiFirst(virtualBounds));
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

    #region DXGI 창 캡처 폴백용 Win32 API

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int SW_RESTORE = 9;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    #endregion

    /// <summary>
    /// 특정 창 캡처 (PrintWindow → BitBlt(WindowDC) → 화면DC 크롭 → DXGI 폴백)
    /// Win11 24H2에서 GDI 기반 방법이 모두 실패할 수 있으므로
    /// DXGI로 전체 화면 캡처 후 창 영역 크롭하는 최종 폴백을 포함합니다.
    /// </summary>
    public async Task<CaptureResult> CaptureWindowAsync(IntPtr hWnd)
    {
        CaptureLogger.Info("Capture", $"=== 창 캡처: hWnd=0x{hWnd:X} ===");

        return await Task.Run(() =>
        {
            var windowService = new WindowCaptureService();

            try
            {
                var bitmap = windowService.CaptureWindow(hWnd);

                if (bitmap != null)
                {
                    CaptureLogger.Info("Capture", $"WindowCaptureService 캡처 완료: {bitmap.Width}x{bitmap.Height}");
                    var result = new CaptureResult
                    {
                        Success = true,
                        Image = bitmap,
                        EngineName = "WindowCapture",
                        CapturedAt = DateTime.Now
                    };
                    ProcessCaptureResult(result);
                    return result;
                }
                else
                {
                    CaptureLogger.Warn("Capture", "WindowCaptureService null 반환 — DXGI 폴백 시도");
                }
            }
            catch (Exception ex)
            {
                CaptureLogger.Error("Capture", "WindowCaptureService 예외", ex);
            }

            // ── DXGI 폴백: 창을 전면에 올린 후 DXGI 전체 캡처 → 창 영역 크롭 ──
            try
            {
                var dxgiResult = CaptureWindowViaDxgi(hWnd);
                if (dxgiResult != null)
                {
                    CaptureLogger.Info("Capture", $"DXGI 폴백 캡처 성공: {dxgiResult.Image!.Width}x{dxgiResult.Image.Height}");
                    ProcessCaptureResult(dxgiResult);
                    return dxgiResult;
                }
            }
            catch (Exception ex)
            {
                CaptureLogger.Error("Capture", "DXGI 폴백 예외", ex);
            }

            return new CaptureResult
            {
                Success = false,
                EngineName = "WindowCapture",
                ErrorMessage = "창 캡처 실패 (GDI + DXGI 모두 실패)"
            };
        });
    }

    /// <summary>
    /// DXGI 기반 창 캡처: 대상 창을 전면에 올린 후 DXGI로 전체 화면 캡처 → 창 영역 크롭.
    /// Win11 24H2에서 PrintWindow/BitBlt/GetDC가 모두 검은 화면을 반환할 때의 최종 폴백.
    /// </summary>
    private CaptureResult? CaptureWindowViaDxgi(IntPtr hWnd)
    {
        var dxgiEngine = _engines.FirstOrDefault(e => e is DxgiCapture && e.IsAvailable) as DxgiCapture;
        if (dxgiEngine == null)
        {
            CaptureLogger.Warn("Capture", "DXGI 엔진 사용 불가 — DXGI 폴백 불가");
            return null;
        }

        // 1. 최소화된 창 복원
        if (IsIconic(hWnd))
        {
            ShowWindow(hWnd, SW_RESTORE);
            Thread.Sleep(500);
        }

        // 2. 대상 창을 전면에 올리기
        SetForegroundWindow(hWnd);
        for (int wait = 0; wait < 15; wait++)
        {
            Thread.Sleep(100);
            if (GetForegroundWindow() == hWnd) break;
        }
        // DWM 렌더링 대기
        Thread.Sleep(300);

        // 3. DWM으로 최신 창 경계 가져오기
        Rectangle windowBounds;
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT dwmRect, Marshal.SizeOf<RECT>()) == 0)
        {
            windowBounds = new Rectangle(dwmRect.Left, dwmRect.Top,
                dwmRect.Right - dwmRect.Left, dwmRect.Bottom - dwmRect.Top);
        }
        else
        {
            CaptureLogger.Warn("Capture", "DWM 창 경계 조회 실패");
            return null;
        }

        CaptureLogger.Info("Capture", $"DXGI 폴백: 원본 bounds={windowBounds}, foreground=0x{GetForegroundWindow():X}");

        // 4. 창 경계를 VirtualScreen 범위로 클리핑
        //    최대화된 창은 화면 밖으로 삐져나오는 경우가 많음 (X=-7, Y=-7 등)
        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        windowBounds = Rectangle.Intersect(windowBounds, virtualScreen);

        CaptureLogger.Info("Capture", $"DXGI 폴백: 클리핑 후 bounds={windowBounds}, virtualScreen={virtualScreen}");

        if (windowBounds.Width <= 0 || windowBounds.Height <= 0)
        {
            CaptureLogger.Warn("Capture", "DXGI 폴백: 클리핑 후 잘못된 창 경계");
            return null;
        }

        // 5. DXGI로 영역 캡처 (CaptureRegion은 VirtualScreen 캡처 → 크롭)
        // 최대 3회 시도
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var regionResult = dxgiEngine.CaptureRegion(windowBounds);

                if (regionResult.Success && regionResult.Image != null && !IsBlackImage(regionResult.Image))
                {
                    CaptureLogger.Info("Capture", $"DXGI 폴백 시도 {attempt + 1}: 성공 {regionResult.Image.Width}x{regionResult.Image.Height}");
                    return new CaptureResult
                    {
                        Success = true,
                        Image = regionResult.Image,
                        EngineName = "WindowCapture (DXGI)",
                        CapturedAt = DateTime.Now
                    };
                }

                regionResult.Image?.Dispose();
                CaptureLogger.Warn("Capture", $"DXGI 폴백 시도 {attempt + 1}: 검은 화면 또는 실패");

                if (attempt < 2)
                    Thread.Sleep(300 * (attempt + 1));
            }
            catch (Exception ex)
            {
                CaptureLogger.Error("Capture", $"DXGI 폴백 시도 {attempt + 1} 예외", ex);
                if (attempt < 2) Thread.Sleep(300);
            }
        }

        CaptureLogger.Warn("Capture", "DXGI 폴백: 3회 시도 모두 실패");
        return null;
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

    /// <summary>
    /// 이벤트 없이 캡처 실행 (영역 선택용 - ProcessCaptureResult 호출 안함)
    /// </summary>
    private CaptureResult ExecuteCaptureRaw(Func<ICaptureEngine, CaptureResult> captureFunc, string mode)
    {
        var sw = Stopwatch.StartNew();

        foreach (var engine in _engines.Where(e => e.IsAvailable))
        {
            CaptureLogger.LogCaptureAttempt(engine.Name, mode + " (Raw)", null);

            try
            {
                var result = captureFunc(engine);

                if (result?.Success == true && result.Image != null && !IsBlackImage(result.Image))
                {
                    sw.Stop();
                    CaptureLogger.Info("Capture", $"Raw 성공 ({engine.Name}, {sw.ElapsedMilliseconds}ms)");
                    // ProcessCaptureResult 호출하지 않음 - 이벤트 발생 안함
                    return result;
                }

                result?.Image?.Dispose();
            }
            catch (Exception ex)
            {
                CaptureLogger.Error("Capture", $"[{engine.Name}] Raw 예외", ex);
            }
        }

        sw.Stop();
        return new CaptureResult
        {
            Success = false,
            EngineName = "None",
            ErrorMessage = "모든 캡처 방법 실패 (Raw)"
        };
    }

    /// <summary>
    /// 영역 선택용 VirtualScreen 캡처: GDI BitBlt 우선, DXGI 폴백
    /// DXGI CaptureVirtualScreen은 세션 재초기화 비용이 크고 첫 프레임이 검은 화면일 수 있으므로
    /// GDI BitBlt(데스크톱 DC)을 먼저 시도합니다.
    /// </summary>
    private CaptureResult ExecuteCaptureRawWithGdiFirst(System.Drawing.Rectangle virtualBounds)
    {
        var sw = Stopwatch.StartNew();

        // 1. GDI BitBlt 우선 시도 (안정적, 빠름)
        var gdiEngine = _engines.FirstOrDefault(e => e is GdiCapture && e.IsAvailable);
        if (gdiEngine != null)
        {
            CaptureLogger.LogCaptureAttempt(gdiEngine.Name, "VirtualScreen (Raw, GDI우선)", null);
            try
            {
                var result = gdiEngine.CaptureRegion(virtualBounds);
                if (result?.Success == true && result.Image != null && !IsBlackImage(result.Image))
                {
                    sw.Stop();
                    CaptureLogger.Info("Capture", $"Raw GDI 성공 ({sw.ElapsedMilliseconds}ms)");
                    return result;
                }
                result?.Image?.Dispose();
                CaptureLogger.Warn("Capture", "GDI BitBlt 검은 화면, DXGI 폴백 시도");
            }
            catch (Exception ex)
            {
                CaptureLogger.Error("Capture", $"[GDI] Raw 예외", ex);
            }
        }

        // 2. DXGI 폴백
        var dxgiEngine = _engines.FirstOrDefault(e => e is DxgiCapture && e.IsAvailable);
        if (dxgiEngine != null)
        {
            CaptureLogger.LogCaptureAttempt(dxgiEngine.Name, "VirtualScreen (Raw, DXGI폴백)", null);
            try
            {
                var result = dxgiEngine.CaptureRegion(virtualBounds);
                if (result?.Success == true && result.Image != null && !IsBlackImage(result.Image))
                {
                    sw.Stop();
                    CaptureLogger.Info("Capture", $"Raw DXGI 성공 ({sw.ElapsedMilliseconds}ms)");
                    return result;
                }
                result?.Image?.Dispose();
            }
            catch (Exception ex)
            {
                CaptureLogger.Error("Capture", $"[DXGI] Raw 예외", ex);
            }
        }

        sw.Stop();
        return new CaptureResult
        {
            Success = false,
            EngineName = "None",
            ErrorMessage = "영역 캡처 실패: GDI/DXGI 모두 검은 화면"
        };
    }

    private static bool IsBlackImage(Bitmap bitmap) => CaptureEngineBase.IsBlackImage(bitmap);

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
        CaptureLogger.DebugLog("Save", $"기본 저장 폭더: {saveFolder}");
        
        if (_settings.OrganizeByDate)
        {
            saveFolder = Path.Combine(saveFolder, DateTime.Now.ToString("yyyy-MM-dd"));
            CaptureLogger.DebugLog("Save", $"날짜별 폭더: {saveFolder}");
        }

        if (!Directory.Exists(saveFolder))
        {
            CaptureLogger.DebugLog("Save", $"폭더 생성: {saveFolder}");
            Directory.CreateDirectory(saveFolder);
        }

        var fileName = GenerateFileName();
        // .NET System.Drawing은 webp 미지원 → PNG로 대체 저장
        var actualFormat = _settings.ImageFormat.ToLower();
        if (actualFormat == "webp")
        {
            CaptureLogger.Warn("Save", "webp 미지원, PNG로 대체 저장");
            actualFormat = "png";
        }
        var extension = actualFormat switch
        {
            "jpg" or "jpeg" => ".jpg",
            "bmp" => ".bmp",
            _ => ".png"
        };

        var filePath = Path.Combine(saveFolder, fileName + extension);
        CaptureLogger.Info("Save", $"파일 저장: {filePath}");

        int counter = 1;
        while (File.Exists(filePath))
        {
            filePath = Path.Combine(saveFolder, $"{fileName}_{counter}{extension}");
            counter++;
        }

        var format = actualFormat switch
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
        
        CaptureLogger.Info("Save", $"저장 완료: {filePath}");
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
