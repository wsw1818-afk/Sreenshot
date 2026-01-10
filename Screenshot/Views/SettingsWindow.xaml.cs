using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Screenshot.Models;

namespace Screenshot.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    // 임시 단축키 설정 (저장 전까지 보관)
    private HotkeySettings _tempFullScreenHotkey = null!;
    private HotkeySettings _tempRegionHotkey = null!;
    private HotkeySettings _tempWindowHotkey = null!;
    private HotkeySettings _tempDelayedHotkey = null!;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadSettings();
    }

    private void LoadSettings()
    {
        // 저장 설정
        SaveFolderTextBox.Text = _settings.SaveFolder;

        // 파일 형식
        foreach (ComboBoxItem item in FormatComboBox.Items)
        {
            if (item.Tag?.ToString() == _settings.ImageFormat.ToLower())
            {
                FormatComboBox.SelectedItem = item;
                break;
            }
        }
        if (FormatComboBox.SelectedItem == null)
            FormatComboBox.SelectedIndex = 0;

        // JPG 품질
        foreach (ComboBoxItem item in QualityComboBox.Items)
        {
            if (item.Tag?.ToString() == _settings.JpegQuality.ToString())
            {
                QualityComboBox.SelectedItem = item;
                break;
            }
        }
        if (QualityComboBox.SelectedItem == null)
            QualityComboBox.SelectedIndex = 1;

        // 지연 시간
        foreach (ComboBoxItem item in DelayComboBox.Items)
        {
            if (item.Tag?.ToString() == _settings.DelaySeconds.ToString())
            {
                DelayComboBox.SelectedItem = item;
                break;
            }
        }
        if (DelayComboBox.SelectedItem == null)
            DelayComboBox.SelectedIndex = 0;

        // 체크박스들
        OrganizeByDateCheckBox.IsChecked = _settings.OrganizeByDate;
        AutoSaveCheckBox.IsChecked = _settings.AutoSave;
        CopyToClipboardCheckBox.IsChecked = _settings.CopyToClipboard;
        PlaySoundCheckBox.IsChecked = _settings.PlaySound;
        OpenEditorCheckBox.IsChecked = _settings.OpenEditorAfterCapture;

        // X 버튼 동작 (RadioButton)
        CloseToTrayRadio.IsChecked = _settings.MinimizeToTray;
        CloseToExitRadio.IsChecked = !_settings.MinimizeToTray;

        StartMinimizedCheckBox.IsChecked = _settings.StartMinimized;
        RunOnStartupCheckBox.IsChecked = _settings.RunOnStartup;

        // 스마트 기능
        AutoPrivacyMaskingCheckBox.IsChecked = _settings.AutoPrivacyMasking;
        EnableWatermarkCheckBox.IsChecked = _settings.EnableWatermark;
        EnableAuditLogCheckBox.IsChecked = _settings.EnableAuditLog;

        // 단축키 로드
        _tempFullScreenHotkey = CloneHotkey(_settings.FullScreenHotkey);
        _tempRegionHotkey = CloneHotkey(_settings.RegionHotkey);
        _tempWindowHotkey = CloneHotkey(_settings.ActiveWindowHotkey);
        _tempDelayedHotkey = CloneHotkey(_settings.DelayedHotkey);

        FullScreenHotkeyBox.Text = _tempFullScreenHotkey.ToString();
        RegionHotkeyBox.Text = _tempRegionHotkey.ToString();
        WindowHotkeyBox.Text = _tempWindowHotkey.ToString();
        DelayedHotkeyBox.Text = _tempDelayedHotkey.ToString();
    }

    private static HotkeySettings CloneHotkey(HotkeySettings source)
    {
        return new HotkeySettings(source.Key, source.Ctrl, source.Shift, source.Alt);
    }

    private void SaveSettings()
    {
        // 저장 설정
        _settings.SaveFolder = SaveFolderTextBox.Text;

        if (FormatComboBox.SelectedItem is ComboBoxItem formatItem)
            _settings.ImageFormat = formatItem.Tag?.ToString() ?? "png";

        if (QualityComboBox.SelectedItem is ComboBoxItem qualityItem)
            _settings.JpegQuality = int.Parse(qualityItem.Tag?.ToString() ?? "95");

        if (DelayComboBox.SelectedItem is ComboBoxItem delayItem)
            _settings.DelaySeconds = int.Parse(delayItem.Tag?.ToString() ?? "3");

        // 체크박스들
        _settings.OrganizeByDate = OrganizeByDateCheckBox.IsChecked ?? true;
        _settings.AutoSave = AutoSaveCheckBox.IsChecked ?? true;
        _settings.CopyToClipboard = CopyToClipboardCheckBox.IsChecked ?? true;
        _settings.PlaySound = PlaySoundCheckBox.IsChecked ?? true;
        _settings.OpenEditorAfterCapture = OpenEditorCheckBox.IsChecked ?? false;

        // X 버튼 동작 (RadioButton)
        _settings.MinimizeToTray = CloseToTrayRadio.IsChecked ?? true;

        _settings.StartMinimized = StartMinimizedCheckBox.IsChecked ?? false;
        _settings.RunOnStartup = RunOnStartupCheckBox.IsChecked ?? false;

        // 스마트 기능
        _settings.AutoPrivacyMasking = AutoPrivacyMaskingCheckBox.IsChecked ?? false;
        _settings.EnableWatermark = EnableWatermarkCheckBox.IsChecked ?? false;
        _settings.EnableAuditLog = EnableAuditLogCheckBox.IsChecked ?? true;

        // 단축키 저장
        _settings.FullScreenHotkey = _tempFullScreenHotkey;
        _settings.RegionHotkey = _tempRegionHotkey;
        _settings.ActiveWindowHotkey = _tempWindowHotkey;
        _settings.DelayedHotkey = _tempDelayedHotkey;

        // 시작 프로그램 등록/해제
        UpdateStartupRegistry();
    }

    private void UpdateStartupRegistry()
    {
        const string appName = "SmartCapture";

        // 단일 파일 앱에서는 Assembly.Location이 빈 문자열을 반환하므로 AppContext.BaseDirectory 사용
        var baseDir = AppContext.BaseDirectory;
        var exePath = Path.Combine(baseDir, "SmartCapture.exe");

        // 실행 파일이 없으면 현재 프로세스 경로 사용
        if (!File.Exists(exePath))
        {
            exePath = Environment.ProcessPath ?? exePath;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null) return;

            if (_settings.RunOnStartup)
            {
                key.SetValue(appName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"시작 프로그램 등록 실패: {ex.Message}");
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "스크린샷 저장 폴더를 선택하세요",
            SelectedPath = SaveFolderTextBox.Text,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SaveFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    #region 단축키 설정

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            textBox.Text = "키를 입력하세요...";
            textBox.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(60, 60, 60));
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox) return;

        e.Handled = true;

        // 수정자 키만 누른 경우 무시
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftShift || e.Key == Key.RightShift ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
            e.Key == Key.LWin || e.Key == Key.RWin ||
            e.Key == Key.System)
        {
            return;
        }

        // 실제 키 가져오기 (Alt 조합 시 SystemKey 사용)
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // ESC는 취소
        if (key == Key.Escape)
        {
            // 원래 값 복원
            RestoreHotkeyDisplay(textBox);
            Keyboard.ClearFocus();
            return;
        }

        // 수정자 키 확인
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        // PrintScreen은 수정자 없이 허용
        if (key != Key.PrintScreen && key != Key.Snapshot)
        {
            // 다른 키는 최소 하나의 수정자 필요
            if (!ctrl && !shift && !alt)
            {
                textBox.Text = "수정자 키 필요 (Ctrl/Shift/Alt)";
                return;
            }
        }

        // 키 이름 변환
        string keyName = ConvertKeyToString(key);

        // 단축키 설정 생성
        var newHotkey = new HotkeySettings(keyName, ctrl, shift, alt);

        // 해당 TextBox에 맞는 임시 변수에 저장
        var tag = textBox.Tag?.ToString();
        switch (tag)
        {
            case "FullScreen":
                _tempFullScreenHotkey = newHotkey;
                break;
            case "Region":
                _tempRegionHotkey = newHotkey;
                break;
            case "Window":
                _tempWindowHotkey = newHotkey;
                break;
            case "Delayed":
                _tempDelayedHotkey = newHotkey;
                break;
        }

        textBox.Text = newHotkey.ToString();
        textBox.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(51, 51, 51));
        Keyboard.ClearFocus();
    }

    private void RestoreHotkeyDisplay(System.Windows.Controls.TextBox textBox)
    {
        var tag = textBox.Tag?.ToString();
        textBox.Text = tag switch
        {
            "FullScreen" => _tempFullScreenHotkey.ToString(),
            "Region" => _tempRegionHotkey.ToString(),
            "Window" => _tempWindowHotkey.ToString(),
            "Delayed" => _tempDelayedHotkey.ToString(),
            _ => ""
        };
        textBox.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(51, 51, 51));
    }

    private static string ConvertKeyToString(Key key)
    {
        return key switch
        {
            Key.PrintScreen or Key.Snapshot => "PrintScreen",
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            Key.NumPad0 => "NumPad0",
            Key.NumPad1 => "NumPad1",
            Key.NumPad2 => "NumPad2",
            Key.NumPad3 => "NumPad3",
            Key.NumPad4 => "NumPad4",
            Key.NumPad5 => "NumPad5",
            Key.NumPad6 => "NumPad6",
            Key.NumPad7 => "NumPad7",
            Key.NumPad8 => "NumPad8",
            Key.NumPad9 => "NumPad9",
            Key.OemTilde => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            _ => key.ToString()
        };
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        // 기본값으로 초기화
        _tempFullScreenHotkey = new HotkeySettings("PrintScreen", false, false, false);
        _tempRegionHotkey = new HotkeySettings("S", true, true, false);
        _tempWindowHotkey = new HotkeySettings("PrintScreen", false, false, true);
        _tempDelayedHotkey = new HotkeySettings("D", true, true, false);

        FullScreenHotkeyBox.Text = _tempFullScreenHotkey.ToString();
        RegionHotkeyBox.Text = _tempRegionHotkey.ToString();
        WindowHotkeyBox.Text = _tempWindowHotkey.ToString();
        DelayedHotkeyBox.Text = _tempDelayedHotkey.ToString();
    }

    #endregion
}
