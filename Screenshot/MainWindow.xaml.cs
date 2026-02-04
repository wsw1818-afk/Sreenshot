using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using Screenshot.Models;
using Screenshot.Services;
using Screenshot.Services.Capture;
using Screenshot.Views;

namespace Screenshot;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly CaptureManager _captureManager;
    private readonly HotkeyService _hotkeyService;
    private readonly NotificationService _notificationService;
    private readonly WindowCaptureService _windowCaptureService;
    private readonly ScrollCaptureService _scrollCaptureService;
    private readonly ChromeCaptureService _chromeCaptureService;
    private readonly OcrService _ocrService;
    private readonly List<CaptureResult> _captureHistory = new();
    private CaptureResult? _selectedCapture;
    private bool _isExiting;
    private bool _isCapturing;

    public MainWindow()
    {
        InitializeComponent();

        // 설정 로드
        _settings = AppSettings.Load();

        // 서비스 초기화
        _captureManager = new CaptureManager(_settings);
        _captureManager.CaptureCompleted += OnCaptureCompleted;
        _captureManager.StatusChanged += OnStatusChanged;

        _hotkeyService = new HotkeyService();
        _notificationService = new NotificationService();
        _windowCaptureService = new WindowCaptureService();
        _scrollCaptureService = new ScrollCaptureService();
        _chromeCaptureService = new ChromeCaptureService();
        _ocrService = new OcrService();

        // UI 초기화
        UpdateSavePathDisplay();
        InitializeMonitorButtons();

        // 창 로드 후 단축키 등록
        Loaded += MainWindow_Loaded;

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

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 전역 단축키 초기화
        if (_settings.EnableGlobalHotkeys)
        {
            _hotkeyService.Initialize(this);
            _hotkeyService.FullScreenCapture += () => SafeExecuteAsync(CaptureFullScreenAsync);
            _hotkeyService.RegionCapture += () => SafeExecuteAsync(CaptureRegionAsync);
            _hotkeyService.DelayedCapture += () => SafeExecuteAsync(CaptureDelayedAsync);
            _hotkeyService.WindowCapture += () => SafeExecuteAsync(CaptureWindowAsync);

            if (_hotkeyService.RegisterHotkeys())
            {
                StatusText.Text = "준비됨 (단축키 활성화)";
            }
            else
            {
                StatusText.Text = "준비됨 (단축키 등록 실패)";
            }
        }
    }

    #region 비동기 작업 헬퍼

    private async void SafeExecuteAsync(Func<Task> asyncAction, string actionName = "")
    {
        Debug.WriteLine($"[SafeExecuteAsync] 시작: {actionName}");
        Services.Capture.CaptureLogger.DebugLog("MainWindow", $"[SafeExecuteAsync] 시작: {actionName}");
        try
        {
            await asyncAction();
            Debug.WriteLine($"[SafeExecuteAsync] 완료: {actionName}");
            Services.Capture.CaptureLogger.DebugLog("MainWindow", $"[SafeExecuteAsync] 완료: {actionName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SafeExecuteAsync] 예외 ({actionName}): {ex}");
            Services.Capture.CaptureLogger.Error("MainWindow", $"[SafeExecuteAsync] 예외 ({actionName})", ex);
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"오류: {ex.Message}";
                if (_settings.ShowToastNotification)
                {
                    _notificationService.ShowCaptureError(ex.Message);
                }
            });
        }
    }

    #endregion

    #region 캡처 기능

    private async Task CaptureFullScreenAsync()
    {
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            StatusText.Text = "전체 화면 캡처 중...";

            var wasVisible = IsVisible;
            if (wasVisible) Hide();
            await Task.Delay(200);

            var result = await _captureManager.CaptureFullScreenAsync();

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
        Services.Capture.CaptureLogger.Info("RegionCapture", $"=== 시작, _isCapturing={_isCapturing} ===");
        Debug.WriteLine($"[CaptureRegionAsync] 시작, _isCapturing={_isCapturing}");
        if (_isCapturing) 
        {
            Services.Capture.CaptureLogger.Warn("RegionCapture", "이미 캡처 중, 리턴");
            Debug.WriteLine("[CaptureRegionAsync] 이미 캡처 중, 리턴");
            return;
        }
        _isCapturing = true;
        Services.Capture.CaptureLogger.DebugLog("RegionCapture", "_isCapturing=true 설정됨");

        try
        {
            // 창 숨기기 (DXGI는 창 위치와 무관하게 데스크톱 직접 캡처)
            Services.Capture.CaptureLogger.DebugLog("RegionCapture", "창 숨기기");
            Hide();

            // DWM이 창을 완전히 숨길 시간
            await Task.Delay(300);

            Services.Capture.CaptureLogger.DebugLog("RegionCapture", "창 숨김 완료, DXGI로 캡처 시작");

            // DXGI 사용 (CaptureManager를 통해 - 이벤트 없이)
            Services.Capture.CaptureLogger.DebugLog("RegionCapture", "CaptureManager.CaptureFullScreenRawAsync() 호출");
            var captureResult = await _captureManager.CaptureFullScreenRawAsync();
            System.Drawing.Bitmap? capturedScreen = captureResult.Success ? captureResult.Image : null;
            
            if (capturedScreen != null)
            {
                Services.Capture.CaptureLogger.DebugLog("RegionCapture", $"캡처 성공: {capturedScreen.Width}x{capturedScreen.Height}");
            }
            else
            {
                Services.Capture.CaptureLogger.Error("RegionCapture", "CopyFromScreen 실패");
            }

            Services.Capture.CaptureLogger.DebugLog("RegionCapture", $"캡처 결과: {capturedScreen?.Width}x{capturedScreen?.Height}");
            
            if (capturedScreen == null)
            {
                System.Diagnostics.Debug.WriteLine("[RegionCapture] capturedScreen is null");
                Show();
                StatusText.Text = "화면 캡처 실패";
                if (_settings.ShowToastNotification)
                {
                    _notificationService.ShowCaptureError("화면 캡처에 실패했습니다.");
                }
                return;
            }
            
            Services.Capture.CaptureLogger.DebugLog("RegionCapture", "CaptureScreen 완료, Overlay 생성 중...");
            System.Diagnostics.Debug.WriteLine("[RegionCapture] CaptureOverlay 생성 중...");

            CaptureOverlay overlay;
            try
            {
                overlay = new CaptureOverlay(capturedScreen);
                Services.Capture.CaptureLogger.DebugLog("RegionCapture", "CaptureOverlay 생성 완료");
            }
            catch (Exception ex)
            {
                Services.Capture.CaptureLogger.Error("RegionCapture", "CaptureOverlay 생성 실패", ex);
                System.Diagnostics.Debug.WriteLine($"[RegionCapture] CaptureOverlay 생성 예외: {ex}");
                Show();
                StatusText.Text = "오버레이 생성 실패";
                capturedScreen.Dispose();
                return;
            }

            Services.Capture.CaptureLogger.DebugLog("RegionCapture", "ShowDialog() 호출");
            System.Diagnostics.Debug.WriteLine("[RegionCapture] Overlay 표시 중...");
            bool? dialogResult;
            try
            {
                dialogResult = overlay.ShowDialog();
            }
            catch (Exception ex)
            {
                Services.Capture.CaptureLogger.Error("RegionCapture", "ShowDialog 실패", ex);
                System.Diagnostics.Debug.WriteLine($"[RegionCapture] ShowDialog 예외: {ex}");
                dialogResult = false;
            }
            Services.Capture.CaptureLogger.DebugLog("RegionCapture", $"ShowDialog 결과: {dialogResult}");
            System.Diagnostics.Debug.WriteLine($"[RegionCapture] Overlay 결과: {dialogResult}, SelectedRegion: {overlay.SelectedRegion}");

            if (dialogResult == true && overlay.SelectedRegion != System.Drawing.Rectangle.Empty && overlay.CapturedScreen != null)
            {
                System.Diagnostics.Debug.WriteLine("[RegionCapture] 영역 선택됨, 자르기 진행...");
                var imageRegion = overlay.ImageRegion;
                StatusText.Text = $"영역 캡처 중...";

                var cropX = Math.Max(0, imageRegion.X);
                var cropY = Math.Max(0, imageRegion.Y);
                var cropWidth = Math.Min(imageRegion.Width, overlay.CapturedScreen.Width - cropX);
                var cropHeight = Math.Min(imageRegion.Height, overlay.CapturedScreen.Height - cropY);

                // 유효한 크기인지 확인
                if (cropWidth > 0 && cropHeight > 0)
                {
                    var cropRect = new System.Drawing.Rectangle(cropX, cropY, cropWidth, cropHeight);
                    var croppedImage = overlay.CapturedScreen.Clone(cropRect, overlay.CapturedScreen.PixelFormat);

                    var result = new CaptureResult
                    {
                        Success = true,
                        Image = croppedImage,
                        EngineName = "RegionCapture"
                    };
                    HandleCaptureResult(result);
                }
            }

            // CapturedScreen은 항상 Dispose (null-safe)
            overlay.CapturedScreen?.Dispose();

            Show();
            InvalidateVisual();
            UpdateLayout();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RegionCapture] 예외 발생: {ex}");
            Show();
            StatusText.Text = "영역 캡처 중 오류 발생";
            if (_settings.ShowToastNotification)
            {
                _notificationService.ShowCaptureError($"영역 캡처 오류: {ex.Message}");
            }
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async Task CaptureDelayedAsync()
    {
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            Hide();

            for (int i = _settings.DelaySeconds; i > 0; i--)
            {
                StatusText.Text = $"{i}초 후 캡처...";
                await Task.Delay(1000);
            }

            var result = await _captureManager.CaptureFullScreenAsync();
            HandleCaptureResult(result);

            Show();
            InvalidateVisual();
            UpdateLayout();
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async Task CaptureWindowAsync()
    {
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            Hide();
            await Task.Delay(200);

            // CaptureManager를 통해 활성 창 캡처 (DXGI → GDI → PrintWindow 순으로 시도)
            var windowInfo = _windowCaptureService.GetForegroundWindowInfo();
            CaptureResult result;
            
            if (windowInfo != null)
            {
                result = await _captureManager.CaptureWindowAsync(windowInfo.Handle);
            }
            else
            {
                // 창 정보를 가져올 수 없으면 기본 ActiveWindow 캡처
                result = await _captureManager.CaptureActiveWindowAsync();
            }

            Show();
            InvalidateVisual();
            UpdateLayout();

            HandleCaptureResult(result);
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async Task CaptureScrollAsync()
    {
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            // 스크롤 캡처 방식 선택
            var captureChoice = System.Windows.MessageBox.Show(
                "스크롤 캡처 방식을 선택하세요:\n\n" +
                "● [예] Chrome 전체 페이지 캡처 (권장)\n" +
                "   - 스티칭 없이 완벽한 이미지\n" +
                "   - Chrome이 자동으로 시작됩니다\n\n" +
                "● [아니오] 일반 스크롤 캡처\n" +
                "   - 모든 프로그램에서 사용 가능\n" +
                "   - 스크롤하며 캡처 후 합성",
                "스크롤 캡처 방식 선택",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (captureChoice == MessageBoxResult.Cancel)
            {
                return;
            }

            if (captureChoice == MessageBoxResult.Yes)
            {
                await CaptureWithChromeCdpAsync();
                return;
            }

            // 일반 스크롤 캡처
            System.Windows.MessageBox.Show(
                "스크롤 캡처를 시작합니다.\n\n" +
                "1. 확인을 누르면 3초 후 캡처가 시작됩니다.\n" +
                "2. 캡처할 창을 클릭하여 활성화하세요.\n" +
                "3. 자동으로 스크롤하며 캡처합니다.",
                "스크롤 캡처",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Hide();

            // 3초 카운트다운
            for (int i = 3; i > 0; i--)
            {
                await Task.Delay(1000);
            }

            StatusText.Text = "스크롤 캡처 중...";

            _scrollCaptureService.ProgressChanged += (current, total) =>
            {
                Dispatcher.Invoke(() => StatusText.Text = $"스크롤 캡처 중... ({current}장)");
            };

            var bitmap = await _scrollCaptureService.CaptureScrollingWindowAsync(400);

            Show();
            InvalidateVisual();
            UpdateLayout();

            if (bitmap != null)
            {
                var result = new CaptureResult
                {
                    Success = true,
                    Image = bitmap,
                    EngineName = "ScrollCapture"
                };
                HandleCaptureResult(result);
            }
            else
            {
                StatusText.Text = "스크롤 캡처 실패";
                if (_settings.ShowToastNotification)
                {
                    _notificationService.ShowCaptureError("스크롤 캡처에 실패했습니다.");
                }
            }
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async Task CaptureWithChromeCdpAsync()
    {
        try
        {
            // 항상 URL 입력 받기 (Google Sheets 등 동적 페이지 지원)
            var urlDialog = new Views.UrlInputDialog();
            if (urlDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(urlDialog.Url))
            {
                return;
            }
            string targetUrl = urlDialog.Url;

            _chromeCaptureService.StatusChanged += status =>
            {
                Dispatcher.Invoke(() => StatusText.Text = status);
            };

            // URL을 지정하여 캡처 (Google Sheets는 자동으로 키보드 스크롤 캡처 사용)
            Bitmap? bitmap = await _chromeCaptureService.CaptureUrlAsync(targetUrl);

            if (bitmap != null)
            {
                var result = new CaptureResult
                {
                    Success = true,
                    Image = bitmap,
                    EngineName = "ChromeCDP"
                };
                HandleCaptureResult(result);
            }
            else
            {
                StatusText.Text = "Chrome 캡처 실패";
                if (_settings.ShowToastNotification)
                {
                    _notificationService.ShowCaptureError("Chrome CDP 캡처에 실패했습니다.");
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Chrome 캡처 오류: {ex.Message}";
            if (_settings.ShowToastNotification)
            {
                _notificationService.ShowCaptureError($"Chrome 캡처 오류: {ex.Message}");
            }
        }
    }

    private const int MaxHistoryCount = 10;

    private void HandleCaptureResult(CaptureResult result)
    {
        Dispatcher.Invoke(() =>
        {
            if (result.Success && result.Image != null)
            {
                // 테스트: AutoSave 강제 활성화
                _settings.AutoSave = true;
                Services.Capture.CaptureLogger.Info("MainWindow", $"[HandleCaptureResult] AutoSave={_settings.AutoSave}, SaveFolder={_settings.SaveFolder}");
                
                // 자동 저장
                if (_settings.AutoSave)
                {
                    var fileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.{_settings.ImageFormat.ToLower()}";
                    var filePath = Path.Combine(_settings.SaveFolder, fileName);
                    Services.Capture.CaptureLogger.Info("MainWindow", $"[HandleCaptureResult] 저장 시도: {filePath}");

                    try
                    {
                        Directory.CreateDirectory(_settings.SaveFolder);
                        var format = _settings.ImageFormat.ToUpper() switch
                        {
                            "PNG" => ImageFormat.Png,
                            "JPEG" or "JPG" => ImageFormat.Jpeg,
                            "BMP" => ImageFormat.Bmp,
                            _ => ImageFormat.Png
                        };
                        result.Image.Save(filePath, format);
                        result.SavedFilePath = filePath;
                        Services.Capture.CaptureLogger.Info("MainWindow", $"[HandleCaptureResult] 저장 성공: {filePath}");
                    }
                    catch (Exception ex)
                    {
                        Services.Capture.CaptureLogger.Error("MainWindow", "[HandleCaptureResult] 저장 실패", ex);
                        Debug.WriteLine($"자동 저장 실패: {ex.Message}");
                    }
                }

                // 히스토리 관리
                while (_captureHistory.Count >= MaxHistoryCount)
                {
                    var oldest = _captureHistory[0];
                    _captureHistory.RemoveAt(0);
                    oldest.Dispose();

                    if (ThumbnailPanel.Children.Count > 0)
                    {
                        ThumbnailPanel.Children.RemoveAt(0);
                    }
                }

                _captureHistory.Add(result);
                _selectedCapture = result;

                AddThumbnail(result);

                EmptyState.Visibility = Visibility.Collapsed;
                ThumbnailScrollViewer.Visibility = Visibility.Visible;

                EngineText.Text = result.EngineName;
                SizeText.Text = $"{result.Image.Width} x {result.Image.Height}";

                if (!string.IsNullOrEmpty(result.SavedFilePath))
                {
                    SavePathText.Text = result.SavedFilePath;
                }

                StatusText.Text = $"캡처 완료 - {result.EngineName}";
                CaptureLogger.Info("MainWindow", $"캡처 성공 - {result.EngineName}, {result.Image.Width}x{result.Image.Height}");
                CaptureLogger.FlushToFile(); // 즉시 로그 저장

                ThumbnailScrollViewer.ScrollToRightEnd();

                // 알림
                if (_settings.ShowToastNotification)
                {
                    _notificationService.ShowCaptureSuccess("캡처 완료", result.SavedFilePath);
                }
                else if (_settings.PlaySound)
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }

                // 자동으로 편집기 열기
                if (_settings.AutoOpenEditor && result.Image != null)
                {
                    OpenImageEditor(result);
                }
            }
            else
            {
                StatusText.Text = $"캡처 실패: {result.ErrorMessage}";
                CaptureLogger.Error("MainWindow", $"캡처 실패: {result.ErrorMessage}");
                CaptureLogger.FlushToFile(); // 즉시 로그 저장
                
                if (_settings.ShowToastNotification)
                {
                    _notificationService.ShowCaptureError(result.ErrorMessage ?? "알 수 없는 오류");
                }
                else
                {
                    System.Windows.MessageBox.Show(result.ErrorMessage, "캡처 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        });
    }

    private void AddThumbnail(CaptureResult result)
    {
        // null 체크
        if (result.Image == null)
        {
            return;
        }

        var bitmapSource = ConvertToBitmapSource(result.Image);

        var border = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 37)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(5),
            Padding = new Thickness(5),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = $"좌클릭: 클립보드 복사\n우클릭: 더 많은 옵션\n{result.Image.Width} x {result.Image.Height}\n{result.EngineName}",
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
        border.MouseRightButtonDown += Thumbnail_RightClick;
        border.MouseEnter += Thumbnail_MouseEnter;
        border.MouseLeave += Thumbnail_MouseLeave;

        ThumbnailPanel.Children.Add(border);
        UpdateThumbnailSelection(border, true);
    }

    private void Thumbnail_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is CaptureResult result)
        {
            _selectedCapture = result;

            var contextMenu = new System.Windows.Controls.ContextMenu();

            var copyItem = new System.Windows.Controls.MenuItem { Header = "클립보드에 복사" };
            copyItem.Click += (s, args) => CopyToClipboard(result);
            contextMenu.Items.Add(copyItem);

            var editItem = new System.Windows.Controls.MenuItem { Header = "편집" };
            editItem.Click += (s, args) => OpenImageEditor(result);
            contextMenu.Items.Add(editItem);

            var saveAsItem = new System.Windows.Controls.MenuItem { Header = "다른 이름으로 저장..." };
            saveAsItem.Click += (s, args) => SaveAs(result);
            contextMenu.Items.Add(saveAsItem);

            if (_ocrService.IsAvailable)
            {
                var ocrItem = new System.Windows.Controls.MenuItem { Header = "텍스트 추출 (OCR)" };
                ocrItem.Click += async (s, args) => await ExtractTextAsync(result);
                contextMenu.Items.Add(ocrItem);
            }

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var deleteItem = new System.Windows.Controls.MenuItem { Header = "삭제" };
            deleteItem.Click += (s, args) => DeleteCapture(result, border);
            contextMenu.Items.Add(deleteItem);

            contextMenu.IsOpen = true;
        }
    }

    private void CopyToClipboard(CaptureResult result)
    {
        if (result.Image == null) return;

        try
        {
            System.Windows.Clipboard.SetImage(ConvertToBitmapSource(result.Image));
            StatusText.Text = "클립보드에 복사됨";
            if (_settings.ShowToastNotification)
            {
                _notificationService.ShowClipboardCopy();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"복사 실패: {ex.Message}";
        }
    }

    private void OpenImageEditor(CaptureResult result)
    {
        if (result.Image == null) return;

        var editor = new ImageEditorWindow((Bitmap)result.Image.Clone());
        editor.Owner = this;

        if (editor.ShowDialog() == true && editor.EditedImage != null)
        {
            // 편집된 이미지로 교체
            result.Image.Dispose();
            result.Image = editor.EditedImage;

            // 썸네일 업데이트
            UpdateThumbnailImage(result);
            StatusText.Text = "이미지 편집 완료";
        }
    }

    private void UpdateThumbnailImage(CaptureResult result)
    {
        foreach (var child in ThumbnailPanel.Children)
        {
            if (child is System.Windows.Controls.Border border && border.Tag == result)
            {
                if (border.Child is System.Windows.Controls.Image img && result.Image != null)
                {
                    img.Source = ConvertToBitmapSource(result.Image);
                }
                break;
            }
        }
    }

    private void SaveAs(CaptureResult result)
    {
        if (result.Image == null) return;

        var dialog = new SaveFileDialog
        {
            Filter = "PNG 이미지|*.png|JPEG 이미지|*.jpg|BMP 이미지|*.bmp",
            DefaultExt = "png",
            FileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var format = Path.GetExtension(dialog.FileName).ToLower() switch
            {
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                _ => ImageFormat.Png
            };

            result.Image.Save(dialog.FileName, format);
            result.SavedFilePath = dialog.FileName;
            SavePathText.Text = dialog.FileName;
            StatusText.Text = "저장 완료";

            if (_settings.ShowToastNotification)
            {
                _notificationService.ShowCaptureSuccess("저장 완료", dialog.FileName);
            }
        }
    }

    private async Task ExtractTextAsync(CaptureResult result)
    {
        if (result.Image == null) return;

        StatusText.Text = "텍스트 추출 중...";

        var ocrResult = await _ocrService.ExtractTextAsync(result.Image);

        if (ocrResult.Success && !string.IsNullOrWhiteSpace(ocrResult.Text))
        {
            System.Windows.Clipboard.SetText(ocrResult.Text);
            StatusText.Text = "텍스트가 클립보드에 복사됨";

            System.Windows.MessageBox.Show(
                $"추출된 텍스트:\n\n{ocrResult.Text}\n\n(클립보드에 복사됨)",
                "OCR 결과",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            StatusText.Text = "텍스트를 찾을 수 없음";
            System.Windows.MessageBox.Show(
                ocrResult.ErrorMessage ?? "이미지에서 텍스트를 찾을 수 없습니다.",
                "OCR 결과",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DeleteCapture(CaptureResult result, System.Windows.Controls.Border border)
    {
        _captureHistory.Remove(result);
        ThumbnailPanel.Children.Remove(border);
        result.Dispose();

        if (_captureHistory.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            ThumbnailScrollViewer.Visibility = Visibility.Collapsed;
            _selectedCapture = null;
        }
        else
        {
            _selectedCapture = _captureHistory.LastOrDefault();
        }

        StatusText.Text = "삭제됨";
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

            foreach (var child in ThumbnailPanel.Children)
            {
                if (child is System.Windows.Controls.Border b)
                {
                    UpdateThumbnailSelection(b, b.Tag == _selectedCapture);
                }
            }

            if (result.Image != null)
            {
                EngineText.Text = result.EngineName;
                SizeText.Text = $"{result.Image.Width} x {result.Image.Height}";
                if (!string.IsNullOrEmpty(result.SavedFilePath))
                {
                    SavePathText.Text = result.SavedFilePath;
                }

                CopyToClipboard(result);
                CopyOverlay.Visibility = Visibility.Visible;

                await Task.Delay(800);
                CopyOverlay.Visibility = Visibility.Collapsed;
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
        Services.Capture.CaptureLogger.Info("MainWindow", $"[OnCaptureCompleted] 캡처 완료: {result.EngineName}, SavedPath={result.SavedFilePath}");
        // CaptureManager에서 이미 저장됨, UI 업데이트만 수행
        if (result.Success && result.Image != null)
        {
            Dispatcher.Invoke(() =>
            {
                AddThumbnail(result);
                StatusText.Text = string.IsNullOrEmpty(result.SavedFilePath) 
                    ? $"캡처 성공 - {result.EngineName}" 
                    : $"캡처 저장됨: {Path.GetFileName(result.SavedFilePath)}";
            });
        }
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
        Services.Capture.CaptureLogger.Info("MainWindow", "[CaptureRegion_Click] 버튼 클릭됨");
        Debug.WriteLine("[CaptureRegion_Click] 버튼 클릭됨");
        SafeExecuteAsync(CaptureRegionAsync, "CaptureRegionAsync");
    }

    private void CaptureWindow_Click(object sender, RoutedEventArgs e)
    {
        SafeExecuteAsync(CaptureWindowAsync);
    }

    private void CaptureScroll_Click(object sender, RoutedEventArgs e)
    {
        SafeExecuteAsync(CaptureScrollAsync);
    }

    private void MonitorSelectButton_Click(object sender, RoutedEventArgs e)
    {
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

            // 단축키 설정 변경 시 재등록
            if (_settings.EnableGlobalHotkeys && !_hotkeyService.IsRegistered)
            {
                _hotkeyService.RegisterHotkeys();
            }
            else if (!_settings.EnableGlobalHotkeys && _hotkeyService.IsRegistered)
            {
                _hotkeyService.UnregisterHotkeys();
            }
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

        MonitorSelectHint.Text = $"{monitors.Count}개 모니터";

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

        var wasVisible = IsVisible;
        if (wasVisible) Hide();
        await Task.Delay(200);

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

    private void TrayMenu_Window_Click(object sender, RoutedEventArgs e)
    {
        SafeExecuteAsync(CaptureWindowAsync);
    }

    private void TrayMenu_Scroll_Click(object sender, RoutedEventArgs e)
    {
        SafeExecuteAsync(CaptureScrollAsync);
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
        CaptureLogger.Info("MainWindow", "앱 종료 - 로그 플러시");
        CaptureLogger.FlushToFile();
        _hotkeyService.Dispose();
        _captureManager.Dispose();
        foreach (var capture in _captureHistory)
        {
            capture.Dispose();
        }
        _captureHistory.Clear();
        TrayIcon.Dispose();
    }

    #endregion

    #region CaptureScreen Direct (MainWindow에 구현)

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
        IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    private static System.Drawing.Bitmap? CaptureScreenDirect()
    {
        Services.Capture.CaptureLogger.DebugLog("MainWindow", "[CaptureScreenDirect] CopyFromScreen 사용 (재시도 포함)");
        
        // 검은 화면 감지 및 최대 3회 재시도
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            Services.Capture.CaptureLogger.DebugLog("MainWindow", $"[CaptureScreenDirect] 시도 {attempt}/3");
            
            var bitmap = CaptureScreenWithCopyFromScreen();
            
            if (bitmap == null)
            {
                Services.Capture.CaptureLogger.Warn("MainWindow", $"[CaptureScreenDirect] 시도 {attempt} 실패: null 반환");
                if (attempt < 3) Thread.Sleep(200 * attempt);
                continue;
            }
            
            // 검은 화면 체크
            if (IsBlackImage(bitmap))
            {
                Services.Capture.CaptureLogger.Warn("MainWindow", $"[CaptureScreenDirect] 시도 {attempt}: 검은 화면 감지, 재시도...");
                bitmap.Dispose();
                if (attempt < 3) Thread.Sleep(300 * attempt);
                continue;
            }
            
            Services.Capture.CaptureLogger.DebugLog("MainWindow", $"[CaptureScreenDirect] 성공 (시도 {attempt}): {bitmap.Width}x{bitmap.Height}");
            return bitmap;
        }
        
        Services.Capture.CaptureLogger.Error("MainWindow", "[CaptureScreenDirect] 3회 시도 후 실패");
        return null;
    }

    private static System.Drawing.Bitmap? CaptureScreenWithCopyFromScreen()
    {
        try
        {
            var virtualScreen = SystemInformation.VirtualScreen;
            Services.Capture.CaptureLogger.DebugLog("MainWindow", $"[CaptureScreenWithCopyFromScreen] VirtualScreen: {virtualScreen.Width}x{virtualScreen.Height} @ ({virtualScreen.X},{virtualScreen.Y})");
            
            var result = new System.Drawing.Bitmap(virtualScreen.Width, virtualScreen.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(result))
            {
                // SourceCopy만 사용 (CaptureBlt는 일부 환경에서 InvalidEnumArgumentException)
                g.CopyFromScreen(virtualScreen.X, virtualScreen.Y, 0, 0,
                    new System.Drawing.Size(virtualScreen.Width, virtualScreen.Height),
                    CopyPixelOperation.SourceCopy);
            }
            Services.Capture.CaptureLogger.DebugLog("MainWindow", $"[CaptureScreenWithCopyFromScreen] 성공: {result.Width}x{result.Height}");
            return result;
        }
        catch (Exception ex)
        {
            Services.Capture.CaptureLogger.Error("MainWindow", "[CaptureScreenWithCopyFromScreen] 예외", ex);
            return null;
        }
    }

    private static bool IsBlackImage(System.Drawing.Bitmap bitmap)
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
        return (double)blackCount / sampleCount >= 0.85;
    }

    /// <summary>
    /// 완전한 검은 화멧만 감지 (임계값 더 엄격)
    /// </summary>
    private static bool IsAlmostBlackImage(System.Drawing.Bitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return true;
        
        // 더 많은 샘플로 체크
        int sampleCount = Math.Min(100, Math.Max(20, bitmap.Width * bitmap.Height / 50));
        int blackCount = 0;
        var random = new Random();
        
        for (int i = 0; i < sampleCount; i++)
        {
            int x = random.Next(bitmap.Width);
            int y = random.Next(bitmap.Height);
            try
            {
                var pixel = bitmap.GetPixel(x, y);
                // 완전 검은색에 가까운 것만 카운트 (임계값 5)
                if (pixel.R < 5 && pixel.G < 5 && pixel.B < 5)
                    blackCount++;
            }
            catch { }
        }
        
        // 95% 이상이 완전 검은색이어야 실패로 간주
        bool isBlack = (double)blackCount / sampleCount >= 0.95;
        if (isBlack)
        {
            Services.Capture.CaptureLogger.Warn("MainWindow", $"[IsAlmostBlackImage] 검은 화면 감지: {blackCount}/{sampleCount} ({(double)blackCount/sampleCount:P1})");
        }
        return isBlack;
    }

    #endregion
}
