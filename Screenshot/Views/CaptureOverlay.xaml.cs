using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Forms;

namespace Screenshot.Views;

/// <summary>
/// 영역 선택 오버레이 - 화면 캡처 후 그 위에서 선택
/// </summary>
public partial class CaptureOverlay : Window
{
    // DPI 관련 Win32 API
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    // WPF 좌표로 시작점과 현재점 저장 (UI 표시용)
    private System.Windows.Point _startPointWpf;
    private System.Windows.Point _currentPointWpf;
    private bool _isSelecting;
    private Rectangle _selectedRegion;

    // 가상 화면 정보 (물리적 픽셀)
    private readonly int _screenX;
    private readonly int _screenY;
    private readonly int _screenWidth;
    private readonly int _screenHeight;

    // WPF 좌표계 화면 크기 (DPI 스케일 적용)
    private double _wpfScreenWidth;
    private double _wpfScreenHeight;

    // 캡처된 전체 화면 이미지
    private System.Drawing.Bitmap? _capturedScreen;

    // 이미지 내 선택 영역 (잘라내기용)
    private Rectangle _imageRegion;

    // 십자선 길이
    private const double CrosshairLength = 20;

    // DPI 스케일
    private double _dpiScale = 1.0;

    public Rectangle SelectedRegion => _selectedRegion;
    public Rectangle ImageRegion => _imageRegion;
    public System.Drawing.Bitmap? CapturedScreen => _capturedScreen;

    /// <summary>
    /// 미리 캡처된 화면 이미지를 받아서 오버레이 생성
    /// </summary>
    public CaptureOverlay(System.Drawing.Bitmap capturedScreen)
    {
        InitializeComponent();

        // 가상 화면 크기 (System.Windows.Forms 사용) - 물리적 픽셀
        var virtualScreen = SystemInformation.VirtualScreen;
        _screenX = virtualScreen.X;
        _screenY = virtualScreen.Y;
        _screenWidth = virtualScreen.Width;
        _screenHeight = virtualScreen.Height;

        // 전달받은 캡처 이미지 저장
        _capturedScreen = capturedScreen;

        // DPI 스케일 계산 (WPF는 DIP 단위를 사용)
        using (var g = Graphics.FromHwnd(IntPtr.Zero))
        {
            _dpiScale = g.DpiX / 96.0;
        }
        
        // WPF DIP 크기 = 물리적 픽셀 / DPI 스케일
        _wpfScreenWidth = _screenWidth / _dpiScale;
        _wpfScreenHeight = _screenHeight / _dpiScale;

        // 배경 이미지 설정
        Services.Capture.CaptureLogger.DebugLog("CaptureOverlay", $"이미지 변환 시작: {_capturedScreen.Width}x{_capturedScreen.Height}");
        var bitmapSource = ConvertToBitmapSource(_capturedScreen);
        Services.Capture.CaptureLogger.DebugLog("CaptureOverlay", $"이미지 변환 완료: {bitmapSource.PixelWidth}x{bitmapSource.PixelHeight}");
        
        BackgroundImage.Source = bitmapSource;
        BackgroundImage.Width = _wpfScreenWidth;
        BackgroundImage.Height = _wpfScreenHeight;
        Services.Capture.CaptureLogger.DebugLog("CaptureOverlay", $"BackgroundImage 설정 완료: {BackgroundImage.Width}x{BackgroundImage.Height}");

        // 창 설정 - 가상 화면 전체를 덮도록 설정 (멀티모니터 지원)
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _screenX / _dpiScale;
        Top = _screenY / _dpiScale;
        Width = _wpfScreenWidth;
        Height = _wpfScreenHeight;
        
        // 창이 화면을 완전히 덮도록 설정
        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.NoResize;

        // 마우스 이동 이벤트로 십자선 업데이트
        MouseMove += UpdateCrosshair;
    }

    /// <summary>
    /// 물리적 마우스 좌표 가져오기 (이미지 좌표계)
    /// </summary>
    private System.Drawing.Point GetPhysicalMousePosition()
    {
        GetCursorPos(out POINT pt);
        return new System.Drawing.Point(pt.X - _screenX, pt.Y - _screenY);
    }

    /// <summary>
    /// WPF 좌표를 물리적 좌표로 변환 (DPI 스케일 적용)
    /// </summary>
    private System.Drawing.Point WpfToPhysical(System.Windows.Point wpfPoint)
    {
        return new System.Drawing.Point(
            (int)Math.Round(wpfPoint.X * _dpiScale),
            (int)Math.Round(wpfPoint.Y * _dpiScale));
    }

    private void UpdateCrosshair(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // WPF 좌표 사용 (UI 표시용)
        var pos = e.GetPosition(this);
        double x = pos.X;
        double y = pos.Y;

        // 가로선 (배경)
        CrosshairHorizontalBg.X1 = x - CrosshairLength;
        CrosshairHorizontalBg.Y1 = y;
        CrosshairHorizontalBg.X2 = x + CrosshairLength;
        CrosshairHorizontalBg.Y2 = y;

        // 가로선 (흰색)
        CrosshairHorizontal.X1 = x - CrosshairLength;
        CrosshairHorizontal.Y1 = y;
        CrosshairHorizontal.X2 = x + CrosshairLength;
        CrosshairHorizontal.Y2 = y;

        // 세로선 (배경)
        CrosshairVerticalBg.X1 = x;
        CrosshairVerticalBg.Y1 = y - CrosshairLength;
        CrosshairVerticalBg.X2 = x;
        CrosshairVerticalBg.Y2 = y + CrosshairLength;

        // 세로선 (흰색)
        CrosshairVertical.X1 = x;
        CrosshairVertical.Y1 = y - CrosshairLength;
        CrosshairVertical.X2 = x;
        CrosshairVertical.Y2 = y + CrosshairLength;

        // 중앙 원
        Canvas.SetLeft(CrosshairCenter, x - 3);
        Canvas.SetTop(CrosshairCenter, y - 3);
    }

    // Win32 API for BitBlt
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
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

    /// <summary>
    /// 화면을 캡처하고 비트맵 반환 (오버레이 표시 전 호출)
    /// BitBlt 사용 (CopyFromScreen보다 안정적)
    /// </summary>
    public static System.Drawing.Bitmap? CaptureScreen()
    {
        // CopyFromScreen만 사용 (BitBlt는 Windows 11에서 검은 화면 문제)
        return CaptureScreenWithCopyFromScreen();
    }

    private static System.Drawing.Bitmap? CaptureScreenWithCopyFromScreen()
    {
        System.Diagnostics.Debug.WriteLine("[CaptureOverlay.CaptureScreen] CopyFromScreen 폭백 시도");
        try
        {
            var virtualScreen = SystemInformation.VirtualScreen;
            var result = new System.Drawing.Bitmap(virtualScreen.Width, virtualScreen.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                // SourceCopy만 사용 (CaptureBlt는 일부 환경에서 InvalidEnumArgumentException)
                g.CopyFromScreen(virtualScreen.X, virtualScreen.Y, 0, 0,
                    new System.Drawing.Size(virtualScreen.Width, virtualScreen.Height),
                    CopyPixelOperation.SourceCopy);
            }
            System.Diagnostics.Debug.WriteLine($"[CaptureOverlay.CaptureScreen] CopyFromScreen 성공: {result.Width}x{result.Height}");
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CaptureOverlay.CaptureScreen] CopyFromScreen 실패: {ex.Message}");
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

    private BitmapSource ConvertToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        // MemoryStream 방식 - 가장 안정적
        var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = ms;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmapImage.EndInit();
        
        // 로드 완료 대기
        bitmapImage.Freeze();
        
        // 스트림은 Freeze 후에 닫음
        ms.Dispose();
        
        return bitmapImage;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            _selectedRegion = new Rectangle(_screenX, _screenY, _screenWidth, _screenHeight);
            DialogResult = true;
            Close();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // WPF 좌표 저장 (UI 표시용)
        _startPointWpf = e.GetPosition(this);
        _isSelecting = true;

        HelpPanel.Visibility = Visibility.Collapsed;
        DimOverlay.Visibility = Visibility.Visible;  // 어두운 오버레이 표시

        SelectionBorder.Visibility = Visibility.Visible;
        SizeLabel.Visibility = Visibility.Visible;

        Canvas.SetLeft(SelectionBorder, _startPointWpf.X);
        Canvas.SetTop(SelectionBorder, _startPointWpf.Y);
        SelectionBorder.Width = 0;
        SelectionBorder.Height = 0;
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isSelecting) return;

        // WPF 좌표 사용 (UI 표시용)
        _currentPointWpf = e.GetPosition(this);

        double x = Math.Min(_startPointWpf.X, _currentPointWpf.X);
        double y = Math.Min(_startPointWpf.Y, _currentPointWpf.Y);
        double width = Math.Abs(_currentPointWpf.X - _startPointWpf.X);
        double height = Math.Abs(_currentPointWpf.Y - _startPointWpf.Y);

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;

        // 물리적 픽셀 크기로 표시 (실제 캡처될 크기)
        var physicalWidth = (int)Math.Round(width * _dpiScale);
        var physicalHeight = (int)Math.Round(height * _dpiScale);
        SizeText.Text = $"{physicalWidth} x {physicalHeight}";

        var labelTop = y - 30;
        if (labelTop < 0) labelTop = y + height + 5;

        Canvas.SetLeft(SizeLabel, x);
        Canvas.SetTop(SizeLabel, labelTop);

        UpdateBackgroundMask(x, y, width, height);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[CaptureOverlay] MouseLeftButtonUp - _isSelecting={_isSelecting}");
        
        if (!_isSelecting)
        {
            System.Diagnostics.Debug.WriteLine("[CaptureOverlay] _isSelecting=false, 리턴");
            return;
        }

        _isSelecting = false;

        // WPF 좌표 사용 (UI 표시용)
        _currentPointWpf = e.GetPosition(this);

        double wpfX = Math.Min(_startPointWpf.X, _currentPointWpf.X);
        double wpfY = Math.Min(_startPointWpf.Y, _currentPointWpf.Y);
        double wpfWidth = Math.Abs(_currentPointWpf.X - _startPointWpf.X);
        double wpfHeight = Math.Abs(_currentPointWpf.Y - _startPointWpf.Y);

        System.Diagnostics.Debug.WriteLine($"[CaptureOverlay] 선택 영역: {wpfWidth}x{wpfHeight}");

        if (wpfWidth < 10 || wpfHeight < 10)
        {
            System.Diagnostics.Debug.WriteLine("[CaptureOverlay] 영역 너무 작음 (<10), 취소");
            DialogResult = false;
            Close();
            return;
        }

        // WPF 좌표를 물리적 좌표로 변환 (이미지 캡처용)
        int physicalX = (int)Math.Round(wpfX * _dpiScale);
        int physicalY = (int)Math.Round(wpfY * _dpiScale);
        int physicalWidth = (int)Math.Round(wpfWidth * _dpiScale);
        int physicalHeight = (int)Math.Round(wpfHeight * _dpiScale);

        _selectedRegion = new Rectangle(_screenX + physicalX, _screenY + physicalY, physicalWidth, physicalHeight);
        _imageRegion = new Rectangle(physicalX, physicalY, physicalWidth, physicalHeight);

        DialogResult = true;
        Close();
    }

    private void UpdateBackgroundMask(double x, double y, double width, double height)
    {
        // WPF 좌표계 크기 사용
        var fullRect = new RectangleGeometry(new Rect(0, 0, _wpfScreenWidth, _wpfScreenHeight));
        var selectionRect = new RectangleGeometry(new Rect(x, y, width, height));

        var combinedGeometry = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            fullRect,
            selectionRect);

        DimOverlay.Clip = combinedGeometry;
    }
}
