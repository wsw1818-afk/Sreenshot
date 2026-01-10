using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using NHotkey;
using NHotkey.Wpf;
using Screenshot.Models;
using Screenshot.Services;
using Screenshot.Services.Capture;
using Screenshot.Views;

namespace Screenshot;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly CaptureManager _captureManager;
    private readonly List<CaptureResult> _captureHistory = new();
    private CaptureResult? _selectedCapture;
    private bool _isExiting;
    private bool _isCapturing; // 캡처 중 플래그 (중복 캡처 방지)

    public MainWindow()
    {
        InitializeComponent();

        // 설정 로드
        _settings = AppSettings.Load();

        // 캡처 매니저 초기화
        _captureManager = new CaptureManager(_settings);
        _captureManager.CaptureCompleted += OnCaptureCompleted;
        _captureManager.StatusChanged += OnStatusChanged;

        // UI 초기화
        UpdateSavePathDisplay();
        InitializeMonitorButtons();

        // 전역 단축키 비활성화 - 보안 프로그램 차단
        // RegisterHotkeys();

        // 시작 시 최소화 옵션
        if (_settings.StartMinimized)
        {
            WindowState = WindowState.Minimized;
            if (_settings.MinimizeToTray)
            {
                Hide();
            }
        }
    }

    #region 전역 단축키 (비활성화 - 보안 프로그램 차단)

    /// <summary>
    /// 비동기 작업을 안전하게 실행하고 예외 처리
    /// </summary>
    private async void SafeExecuteAsync(Func<Task> asyncAction)
    {
        try
        {
            await asyncAction();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"캡처 작업 실패: {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"오류: {ex.Message}";
            });
        }
    }

    #endregion

    #region 캡처 기능

    private async Task CaptureFullScreenAsync()
    {
        // 중복 캡처 방지
        if (_isCapturing) return;
        _isCapturing = true;

        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SmartCapture_Log.txt");

        try
        {
            StatusText.Text = "전체 화면 캡처 중...";
            File.AppendAllLines(logPath, new[]
            {
                $"=== 전체 화면 캡처 시도: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==="
            });

            // 창을 잠시 숨김
            var wasVisible = IsVisible;
            if (wasVisible) Hide();
            await Task.Delay(300); // 창이 완전히 사라질 때까지 대기 (잔상 방지)

            var result = await _captureManager.CaptureFullScreenAsync();

            File.AppendAllLines(logPath, new[]
            {
                $"캡처 결과: Success={result.Success}, Engine={result.EngineName}",
                result.Image != null ? $"이미지 크기: {result.Image.Width}x{result.Image.Height}" : "이미지: null",
                result.ErrorMessage ?? "",
                ""
            });

            if (wasVisible)
            {
                Show();
                InvalidateVisual();
                UpdateLayout();
            }

            HandleCaptureResult(result);
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async Task CaptureRegionAsync()
    {
        // 중복 캡처 방지
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            // 창을 숨기고 화면 캡처
            Hide();
            await Task.Delay(300); // 창이 완전히 숨겨질 때까지 대기 (잔상 방지)

            // 화면 캡처 (오버레이 표시 전)
            var capturedScreen = CaptureOverlay.CaptureScreen();
            if (capturedScreen == null)
            {
                Show();
                StatusText.Text = "화면 캡처 실패";
                return;
            }

            // 캡처된 이미지로 오버레이 표시
            var overlay = new CaptureOverlay(capturedScreen);
            var dialogResult = overlay.ShowDialog();

            if (dialogResult == true && overlay.SelectedRegion != System.Drawing.Rectangle.Empty && overlay.CapturedScreen != null)
            {
                var region = overlay.SelectedRegion;
                var imageRegion = overlay.ImageRegion;  // 이미지 내 좌표 사용

                // 로그 파일에 기록
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SmartCapture_Log.txt");
                var logLines = new List<string>
                {
                    $"=== 캡처 시도: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===",
                    $"선택된 영역: X={region.X}, Y={region.Y}, W={region.Width}, H={region.Height}",
                    $"이미지 영역: X={imageRegion.X}, Y={imageRegion.Y}, W={imageRegion.Width}, H={imageRegion.Height}",
                    $"캡처 이미지 크기: {overlay.CapturedScreen.Width}x{overlay.CapturedScreen.Height}",
                    $"화면 정보: screenX={SystemInformation.VirtualScreen.X}, screenY={SystemInformation.VirtualScreen.Y}"
                };
                File.AppendAllLines(logPath, logLines);

                Debug.WriteLine($"선택된 영역: X={region.X}, Y={region.Y}, W={region.Width}, H={region.Height}");
                Debug.WriteLine($"이미지 영역: X={imageRegion.X}, Y={imageRegion.Y}, W={imageRegion.Width}, H={imageRegion.Height}");
                Debug.WriteLine($"이미지 크기: {overlay.CapturedScreen.Width}x{overlay.CapturedScreen.Height}");
                StatusText.Text = $"영역 캡처 중... ({region.Width}x{region.Height})";

                // 이미지 내 좌표로 직접 잘라내기
                var cropX = imageRegion.X;
                var cropY = imageRegion.Y;
                var cropWidth = imageRegion.Width;
                var cropHeight = imageRegion.Height;

                // 범위 검증
                if (cropX < 0) cropX = 0;
                if (cropY < 0) cropY = 0;
                if (cropX + cropWidth > overlay.CapturedScreen.Width)
                    cropWidth = overlay.CapturedScreen.Width - cropX;
                if (cropY + cropHeight > overlay.CapturedScreen.Height)
                    cropHeight = overlay.CapturedScreen.Height - cropY;

                var cropRect = new System.Drawing.Rectangle(cropX, cropY, cropWidth, cropHeight);

                // 잘라내기 전 로그
                File.AppendAllLines(logPath, new[]
                {
                    $"잘라내기 영역: X={cropRect.X}, Y={cropRect.Y}, W={cropRect.Width}, H={cropRect.Height}",
                    $"잘라내기 시작..."
                });

                Debug.WriteLine($"잘라내기 영역: X={cropRect.X}, Y={cropRect.Y}, W={cropRect.Width}, H={cropRect.Height}");

                try
                {
                    var croppedImage = overlay.CapturedScreen.Clone(cropRect, overlay.CapturedScreen.PixelFormat);
                    File.AppendAllLines(logPath, new[] { $"잘라내기 성공: {croppedImage.Width}x{croppedImage.Height}", "" });

                    var result = new CaptureResult
                    {
                        Success = true,
                        Image = croppedImage,
                        EngineName = "OverlayCapture"
                    };
                    HandleCaptureResult(result);
                }
                catch (Exception ex)
                {
                    File.AppendAllLines(logPath, new[] { $"잘라내기 실패: {ex.Message}", "" });
                    throw;
                }

                // 원본 이미지 해제
                overlay.CapturedScreen.Dispose();
            }
            else
            {
                overlay.CapturedScreen?.Dispose();
            }

            // 캡처 후 창 다시 표시
            Show();
            InvalidateVisual();
            UpdateLayout();
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async Task CaptureDelayedAsync()
    {
        // 중복 캡처 방지
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            // 창을 숨기고 카운트다운
            Hide();

            for (int i = _settings.DelaySeconds; i > 0; i--)
            {
                // TODO: 화면에 카운트다운 표시
                StatusText.Text = $"{i}초 후 캡처...";
                await Task.Delay(1000);
            }

            var result = await _captureManager.CaptureFullScreenAsync();
            HandleCaptureResult(result);

            // 캡처 후 창 다시 표시
            Show();
            InvalidateVisual();
            UpdateLayout();
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private const int MaxHistoryCount = 10; // 최대 히스토리 개수

    private void HandleCaptureResult(CaptureResult result)
    {
        Dispatcher.Invoke(() =>
        {
            if (result.Success && result.Image != null)
            {
                // 히스토리가 최대 개수를 초과하면 가장 오래된 것 제거
                while (_captureHistory.Count >= MaxHistoryCount)
                {
                    var oldest = _captureHistory[0];
                    _captureHistory.RemoveAt(0);
                    oldest.Dispose(); // 메모리 해제

                    // 썸네일도 제거
                    if (ThumbnailPanel.Children.Count > 0)
                    {
                        ThumbnailPanel.Children.RemoveAt(0);
                    }
                }

                // 히스토리에 추가
                _captureHistory.Add(result);
                _selectedCapture = result;

                // 썸네일 추가
                AddThumbnail(result);

                // 빈 상태 숨기고 스크롤 표시
                EmptyState.Visibility = Visibility.Collapsed;
                ThumbnailScrollViewer.Visibility = Visibility.Visible;

                // 정보 업데이트
                EngineText.Text = result.EngineName;
                SizeText.Text = $"{result.Image.Width} x {result.Image.Height}";

                if (!string.IsNullOrEmpty(result.SavedFilePath))
                {
                    SavePathText.Text = result.SavedFilePath;
                }

                StatusText.Text = $"캡처 완료 - {result.EngineName}";

                // 스크롤을 맨 오른쪽(최신)으로 이동
                ThumbnailScrollViewer.ScrollToRightEnd();

                // 캡처 사운드
                if (_settings.PlaySound)
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }
            }
            else
            {
                StatusText.Text = $"캡처 실패: {result.ErrorMessage}";
                System.Windows.MessageBox.Show(result.ErrorMessage, "캡처 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private void AddThumbnail(CaptureResult result)
    {
        var bitmapSource = ConvertToBitmapSource(result.Image!);

        // 썸네일 컨테이너 (Border + Image)
        var border = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 37)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(5),
            Padding = new Thickness(5),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = $"클릭: 클립보드 복사\n{result.Image!.Width} x {result.Image.Height}\n{result.EngineName}",
            Tag = result,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 120
        };

        var image = new System.Windows.Controls.Image
        {
            Source = bitmapSource,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Height = 100,
            MaxWidth = 180
        };

        border.Child = image;
        border.MouseLeftButtonDown += Thumbnail_Click;
        border.MouseEnter += Thumbnail_MouseEnter;
        border.MouseLeave += Thumbnail_MouseLeave;

        ThumbnailPanel.Children.Add(border);

        // 선택 상태 표시
        UpdateThumbnailSelection(border, true);
    }

    private void Thumbnail_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border)
        {
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));
        }
    }

    private void Thumbnail_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border)
        {
            var isSelected = border.Tag == _selectedCapture;
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(
                isSelected ? System.Windows.Media.Color.FromRgb(0, 120, 212) : System.Windows.Media.Color.FromRgb(51, 51, 51));
        }
    }

    private async void Thumbnail_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is CaptureResult result)
        {
            _selectedCapture = result;

            // 모든 썸네일 선택 상태 업데이트
            foreach (var child in ThumbnailPanel.Children)
            {
                if (child is System.Windows.Controls.Border b)
                {
                    UpdateThumbnailSelection(b, b.Tag == _selectedCapture);
                }
            }

            // 정보 업데이트
            if (result.Image != null)
            {
                EngineText.Text = result.EngineName;
                SizeText.Text = $"{result.Image.Width} x {result.Image.Height}";
                if (!string.IsNullOrEmpty(result.SavedFilePath))
                {
                    SavePathText.Text = result.SavedFilePath;
                }

                // 클립보드에 복사
                try
                {
                    System.Windows.Clipboard.SetImage(ConvertToBitmapSource(result.Image));
                    CopyOverlay.Visibility = Visibility.Visible;
                    StatusText.Text = "클립보드에 복사됨";

                    await Task.Delay(800);
                    CopyOverlay.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"복사 실패: {ex.Message}";
                }
            }
        }
    }

    private void UpdateThumbnailSelection(System.Windows.Controls.Border border, bool isSelected)
    {
        border.BorderBrush = new System.Windows.Media.SolidColorBrush(
            isSelected ? System.Windows.Media.Color.FromRgb(0, 120, 212) : System.Windows.Media.Color.FromRgb(51, 51, 51));
        border.BorderThickness = new Thickness(isSelected ? 3 : 2);
    }

    private BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            var source = BitmapSource.Create(
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

    #endregion

    #region 이벤트 핸들러

    private void OnCaptureCompleted(object? sender, CaptureResult result)
    {
        // 캡처 완료 후 추가 처리
    }

    private void OnStatusChanged(object? sender, string status)
    {
        Dispatcher.Invoke(() => StatusText.Text = status);
    }

    private void CaptureFullScreen_Click(object sender, RoutedEventArgs e)
    {
        SafeExecuteAsync(CaptureFullScreenAsync);
    }

    private void CaptureRegion_Click(object sender, RoutedEventArgs e)
    {
        SafeExecuteAsync(CaptureRegionAsync);
    }

    private void MonitorSelectButton_Click(object sender, RoutedEventArgs e)
    {
        // 모니터 컨텍스트 메뉴 생성 및 표시
        var contextMenu = new System.Windows.Controls.ContextMenu();
        var monitors = DpiHelper.GetAllMonitors();

        foreach (var monitor in monitors)
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                Header = $"모니터 {monitor.Index + 1}" + (monitor.IsPrimary ? " (주 모니터)" : "") + $" - {monitor.Bounds.Width}x{monitor.Bounds.Height}",
                Tag = monitor.Index
            };
            menuItem.Click += MonitorMenuItem_Click;
            contextMenu.Items.Add(menuItem);
        }

        contextMenu.PlacementTarget = sender as System.Windows.Controls.Button;
        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        contextMenu.IsOpen = true;
    }

    private void CaptureDelayed_Click(object sender, RoutedEventArgs e)
    {
        SafeExecuteAsync(CaptureDelayedAsync);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settings);
        settingsWindow.Owner = this;
        if (settingsWindow.ShowDialog() == true)
        {
            _settings.Save();
            UpdateSavePathDisplay();

            // 단축키 비활성화
            // RegisterHotkeys();
        }
    }

    private void SavePathText_Click(object sender, MouseButtonEventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectedCapture?.SavedFilePath))
        {
            var folder = Path.GetDirectoryName(_selectedCapture.SavedFilePath);
            if (Directory.Exists(folder))
            {
                Process.Start("explorer.exe", $"/select,\"{_selectedCapture.SavedFilePath}\"");
            }
        }
        else if (Directory.Exists(_settings.SaveFolder))
        {
            Process.Start("explorer.exe", _settings.SaveFolder);
        }
    }

    private void UpdateSavePathDisplay()
    {
        SavePathText.Text = _settings.SaveFolder;
    }

    private void InitializeMonitorButtons()
    {
        var monitors = DpiHelper.GetAllMonitors();

        // 메인 버튼 힌트 텍스트 업데이트
        MonitorSelectHint.Text = $"{monitors.Count}개 모니터";

        // 트레이 메뉴에 서브메뉴 추가
        TrayMonitorMenu.Items.Clear();
        foreach (var monitor in monitors)
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                Header = $"모니터 {monitor.Index + 1}" + (monitor.IsPrimary ? " (주)" : "") + $" - {monitor.Bounds.Width}x{monitor.Bounds.Height}",
                Tag = monitor.Index
            };
            menuItem.Click += MonitorMenuItem_Click;
            TrayMonitorMenu.Items.Add(menuItem);
        }
    }

    private void MonitorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is int monitorIndex)
        {
            SafeExecuteAsync(() => CaptureMonitorAsync(monitorIndex));
        }
    }

    private async Task CaptureMonitorAsync(int monitorIndex)
    {
        StatusText.Text = $"모니터 {monitorIndex + 1} 캡처 중...";

        // 창을 잠시 숨김
        var wasVisible = IsVisible;
        if (wasVisible) Hide();
        await Task.Delay(300); // 창이 완전히 숨겨질 때까지 대기 (잔상 방지)

        var result = await _captureManager.CaptureMonitorAsync(monitorIndex);

        if (wasVisible)
        {
            Show();
            InvalidateVisual();
            UpdateLayout();
        }

        HandleCaptureResult(result);
    }

    #endregion

    #region 시스템 트레이

    private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowAndActivate();
    }

    private void TrayMenu_FullScreen_Click(object sender, RoutedEventArgs e)
    {
        SafeExecuteAsync(CaptureFullScreenAsync);
    }

    private void TrayMenu_Region_Click(object sender, RoutedEventArgs e)
    {
        SafeExecuteAsync(CaptureRegionAsync);
    }


    private void TrayMenu_Show_Click(object sender, RoutedEventArgs e)
    {
        ShowAndActivate();
    }

    private void TrayMenu_Settings_Click(object sender, RoutedEventArgs e)
    {
        ShowAndActivate();
        SettingsButton_Click(sender, e);
    }

    private void TrayMenu_Exit_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();

        // WPF 렌더링 강제 갱신 (흰색 화면 방지)
        InvalidateVisual();
        UpdateLayout();
    }

    #endregion

    #region 창 이벤트

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
        {
            Hide();
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // 리소스 정리
        _captureManager.Dispose();
        foreach (var capture in _captureHistory)
        {
            capture.Dispose();
        }
        _captureHistory.Clear();
        TrayIcon.Dispose();
    }

    #endregion
}
