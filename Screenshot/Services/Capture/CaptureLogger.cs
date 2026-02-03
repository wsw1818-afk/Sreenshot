using System.Diagnostics;
using System.IO;
using System.Text;

namespace Screenshot.Services.Capture;

/// <summary>
/// 캡처 진단 및 디버깅을 위한 상세 로깅 시스템
/// </summary>
public static class CaptureLogger
{
    private static readonly object LockObj = new();
    private static readonly StringBuilder Buffer = new();
    private static LogLevel _minLevel = LogLevel.Debug;
    private static string? _logFilePath;
    private static bool _isEnabled = true;

    public enum LogLevel
    {
        Verbose = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4
    }

    /// <summary>
    /// 로깅 활성화 여부
    /// </summary>
    public static bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>
    /// 최소 로그 레벨 설정
    /// </summary>
    public static LogLevel MinimumLevel
    {
        get => _minLevel;
        set => _minLevel = value;
    }

    /// <summary>
    /// 로그 파일 경로
    /// </summary>
    public static string LogFilePath
    {
        get
        {
            if (_logFilePath == null)
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logDir = Path.Combine(appData, "SmartCapture", "Logs");
                Directory.CreateDirectory(logDir);
                _logFilePath = Path.Combine(logDir, $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            }
            return _logFilePath;
        }
        set => _logFilePath = value;
    }

    /// <summary>
    /// 로그 작성
    /// </summary>
    public static void Log(LogLevel level, string category, string message)
    {
        if (!_isEnabled || level < _minLevel) return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var levelStr = level.ToString().ToUpperInvariant();
        var logLine = $"[{timestamp}] [{levelStr}] [{category}] {message}";

        lock (LockObj)
        {
            Buffer.AppendLine(logLine);
            Debug.WriteLine(logLine);

            // 10줄마다 파일에 플러시 또는 Error 레벨은 즉시 플러시
            if (Buffer.Length > 0 && (level >= LogLevel.Error || Buffer.ToString().Split('\n').Length >= 10))
            {
                FlushToFile();
            }
        }
    }

    /// <summary>
    /// 상세 로그 (Verbose)
    /// </summary>
    public static void Verbose(string category, string message) => Log(LogLevel.Verbose, category, message);

    /// <summary>
    /// 디버그 로그
    /// </summary>
    public static void DebugLog(string category, string message) => Log(LogLevel.Debug, category, message);

    /// <summary>
    /// 정보 로그
    /// </summary>
    public static void Info(string category, string message) => Log(LogLevel.Info, category, message);

    /// <summary>
    /// 경고 로그
    /// </summary>
    public static void Warn(string category, string message) => Log(LogLevel.Warning, category, message);

    /// <summary>
    /// 에러 로그
    /// </summary>
    public static void Error(string category, string message, Exception? ex = null)
    {
        var fullMessage = ex != null ? $"{message} - Exception: {ex.GetType().Name}: {ex.Message}" : message;
        Log(LogLevel.Error, category, fullMessage);
    }

    /// <summary>
    /// 캡처 진단 정보 로깅
    /// </summary>
    public static void LogDiagnostics()
    {
        Info("Diagnostics", "=== 시스템 진단 정보 ===");
        
        try
        {
            // OS 정보
            Info("Diagnostics", $"OS: {Environment.OSVersion}");
            Info("Diagnostics", $"64-bit OS: {Environment.Is64BitOperatingSystem}");
            Info("Diagnostics", $"64-bit Process: {Environment.Is64BitProcess}");
            
            // .NET 정보
            Info("Diagnostics", $".NET Version: {Environment.Version}");
            
            // 디스플레이 정보
            var screens = System.Windows.Forms.Screen.AllScreens;
            Info("Diagnostics", $"Monitor Count: {screens.Length}");
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                Info("Diagnostics", $"  Monitor {i}: {s.Bounds.Width}x{s.Bounds.Height} @ ({s.Bounds.X},{s.Bounds.Y}) Primary={s.Primary}");
            }
            
            // DWM 정보
            var dwmEnabled = NativeMethods.DwmIsCompositionEnabled();
            Info("Diagnostics", $"DWM Enabled: {dwmEnabled}");
            
            // 메모리 정보
            var proc = Process.GetCurrentProcess();
            Info("Diagnostics", $"Working Set: {proc.WorkingSet64 / 1024 / 1024} MB");
            Info("Diagnostics", $"GC Total Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
        }
        catch (Exception ex)
        {
            Error("Diagnostics", "진단 정보 수집 실패", ex);
        }
        
        Info("Diagnostics", "========================");
    }

    /// <summary>
    /// 캡처 엔진 초기화 로깅
    /// </summary>
    public static void LogEngineInit(string engineName, bool isAvailable, string? details = null)
    {
        var status = isAvailable ? "AVAILABLE" : "UNAVAILABLE";
        var detailStr = details != null ? $" ({details})" : "";
        Info("Engine", $"[{engineName}] 초기화 완료 - 상태: {status}{detailStr}");
    }

    /// <summary>
    /// 캡처 시도 로깅
    /// </summary>
    public static void LogCaptureAttempt(string engineName, string captureMode, System.Drawing.Rectangle? region = null)
    {
        var regionStr = region.HasValue ? $", Region={region.Value}" : "";
        Info("Capture", $"[{engineName}] 캡처 시도 - Mode={captureMode}{regionStr}");
    }

    /// <summary>
    /// 캡처 결과 로깅
    /// </summary>
    public static void LogCaptureResult(string engineName, bool success, System.Drawing.Size? imageSize = null, string? error = null, bool? isBlackImage = null)
    {
        if (success)
        {
            var sizeStr = imageSize.HasValue ? $", Size={imageSize.Value.Width}x{imageSize.Value.Height}" : "";
            var blackStr = isBlackImage.HasValue ? $", IsBlack={isBlackImage.Value}" : "";
            Info("Capture", $"[{engineName}] 캡처 성공{sizeStr}{blackStr}");
        }
        else
        {
            Error("Capture", $"[{engineName}] 캡처 실패 - {error ?? "Unknown error"}");
        }
    }

    /// <summary>
    /// DXGI 특화 로깅
    /// </summary>
    public static void LogDxgi(string message, Exception? ex = null)
    {
        if (ex != null)
            Error("DXGI", message, ex);
        else
            DebugLog("DXGI", message);
    }

    /// <summary>
    /// WinRT 특화 로깅
    /// </summary>
    public static void LogWinRt(string message, Exception? ex = null)
    {
        if (ex != null)
            Error("WinRT", message, ex);
        else
            DebugLog("WinRT", message);
    }

    /// <summary>
    /// GDI 특화 로깅
    /// </summary>
    public static void LogGdi(string message, Exception? ex = null)
    {
        if (ex != null)
            Error("GDI", message, ex);
        else
            DebugLog("GDI", message);
    }

    /// <summary>
    /// 검은 화면 감지 로깅
    /// </summary>
    public static void LogBlackImageDetection(string engineName, int sampleCount, int blackCount, double ratio)
    {
        Warn("BlackImage", $"[{engineName}] 검은 화면 감지 - 샘플={sampleCount}, 검은픽셀={blackCount}, 비율={ratio:P1}");
    }

    /// <summary>
    /// Fallback 체인 로깅
    /// </summary>
    public static void LogFallback(string fromEngine, string toEngine, string reason)
    {
        Warn("Fallback", $"[{fromEngine}] → [{toEngine}] - 사유: {reason}");
    }

    /// <summary>
    /// 파일에 로그 플러시
    /// </summary>
    public static void FlushToFile()
    {
        lock (LockObj)
        {
            if (Buffer.Length == 0) return;

            try
            {
                File.AppendAllText(LogFilePath, Buffer.ToString());
                Buffer.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"로그 파일 쓰기 실패: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 모든 로그 가져오기
    /// </summary>
    public static string GetAllLogs()
    {
        lock (LockObj)
        {
            FlushToFile();
            try
            {
                return File.Exists(LogFilePath) ? File.ReadAllText(LogFilePath) : Buffer.ToString();
            }
            catch
            {
                return Buffer.ToString();
            }
        }
    }

    /// <summary>
    /// 로그 파일 경로 열기
    /// </summary>
    public static void OpenLogFolder()
    {
        try
        {
            FlushToFile();
            var folder = Path.GetDirectoryName(LogFilePath);
            if (folder != null && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"로그 폴더 열기 실패: {ex.Message}");
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        public static bool DwmIsCompositionEnabled()
        {
            try
            {
                return DwmIsCompositionEnabled(out var enabled) == 0 && enabled;
            }
            catch
            {
                return false;
            }
        }
    }
}
