using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Screenshot.Models;

/// <summary>
/// 앱 설정 모델
/// </summary>
public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SmartCapture",
        "settings.json"
    );

    /// <summary>
    /// 저장 폴더 경로
    /// </summary>
    public string SaveFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "Screenshots"
    );

    /// <summary>
    /// 파일 형식 (png, jpg, webp, bmp)
    /// </summary>
    public string ImageFormat { get; set; } = "png";

    /// <summary>
    /// JPG 품질 (1-100)
    /// </summary>
    public int JpegQuality { get; set; } = 95;

    /// <summary>
    /// 파일명 패턴 (기본: Screenshot_{date}_{time})
    /// </summary>
    public string FileNamePattern { get; set; } = "Screenshot_{date}_{time}";

    /// <summary>
    /// 날짜별 폴더 정리
    /// </summary>
    public bool OrganizeByDate { get; set; } = true;

    /// <summary>
    /// 캡처 후 클립보드에 복사
    /// </summary>
    public bool CopyToClipboard { get; set; } = true;

    /// <summary>
    /// 캡처 후 자동 저장
    /// </summary>
    public bool AutoSave { get; set; } = true;

    /// <summary>
    /// 캡처 후 편집 창 열기
    /// </summary>
    public bool OpenEditorAfterCapture { get; set; } = false;

    /// <summary>
    /// 캡처 사운드 재생
    /// </summary>
    public bool PlaySound { get; set; } = true;

    /// <summary>
    /// 시작 시 최소화
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// Windows 시작 시 자동 실행
    /// </summary>
    public bool RunOnStartup { get; set; } = false;

    /// <summary>
    /// 시스템 트레이로 최소화
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// 지연 캡처 시간 (초)
    /// </summary>
    public int DelaySeconds { get; set; } = 3;

    #region 단축키 설정

    /// <summary>전체 화면 캡처 단축키</summary>
    public HotkeySettings FullScreenHotkey { get; set; } = new("PrintScreen", false, false, false);

    /// <summary>영역 선택 캡처 단축키</summary>
    public HotkeySettings RegionHotkey { get; set; } = new("S", true, true, false);

    /// <summary>활성 창 캡처 단축키</summary>
    public HotkeySettings ActiveWindowHotkey { get; set; } = new("PrintScreen", false, false, true);

    /// <summary>지연 캡처 단축키</summary>
    public HotkeySettings DelayedHotkey { get; set; } = new("D", true, true, false);

    #endregion

    #region 스마트 기능 설정

    /// <summary>
    /// 자동 개인정보 마스킹 활성화
    /// </summary>
    public bool AutoPrivacyMasking { get; set; } = false;

    /// <summary>
    /// 워터마크 삽입 활성화
    /// </summary>
    public bool EnableWatermark { get; set; } = false;

    /// <summary>
    /// 워터마크 텍스트
    /// </summary>
    public string WatermarkText { get; set; } = "Captured by {user} at {time}";

    /// <summary>
    /// 감사 로그 활성화
    /// </summary>
    public bool EnableAuditLog { get; set; } = true;

    #endregion

    #region 저장/불러오기

    /// <summary>
    /// 설정 저장
    /// </summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"설정 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 설정 불러오기
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"설정 불러오기 실패: {ex.Message}");
        }

        return new AppSettings();
    }

    #endregion
}

/// <summary>
/// 단축키 설정
/// </summary>
public class HotkeySettings
{
    public string Key { get; set; } = string.Empty;
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }

    public HotkeySettings() { }

    public HotkeySettings(string key, bool ctrl, bool shift, bool alt)
    {
        Key = key;
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}
