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

    // 물리적 좌표로 시작점과 현재점 저장 (물리적 픽셀)
    private System.Drawing.Point _startPointPhysical;
    private System.Drawing.Point _currentPointPhysical;
    private bool _isSelecting;
    private Rectangle _selectedRegion;

    // 가상 화면 정보 (물리적 픽셀)
    private readonly int _screenX;
    private readonly int _screenY;
    private readonly int _screenWidth;
    private readonly int _screenHeight;

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

        // 가상 화면 크기 (System.Windows.Forms 사용)
        var virtualScreen = SystemInformation.VirtualScreen;
        _screenX = virtualScreen.X;
        _screenY = virtualScreen.Y;
        _screenWidth = virtualScreen.Width;
        _screenHeight = virtualScreen.Height;

        // 전달받은 캡처 이미지 저장
        _capturedScreen = capturedScreen;

        // 배경 이미지 설정
        var bitmapSource = ConvertToBitmapSource(_capturedScreen);
        BackgroundImage.Source = bitmapSource;
        BackgroundImage.Width = _screenWidth;
        BackgroundImage.Height = _screenHeight;

        // 창 설정
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _screenX;
        Top = _screenY;
        Width = _screenWidth;
        Height = _screenHeight;

        // 마우스 이동 이벤트로 십자선 업데이트
        MouseMove += UpdateCrosshair;

        // DPI 스케일 계산
        Loaded += (s, e) =>
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                _dpiScale = source.CompositionTarget.TransformToDevice.M11;
            }
        };
    }

    /// <summary>
    /// 물리적 마우스 좌표 가져오기 (DPI 스케일 영향 없음)
    /// </summary>
    private System.Drawing.Point GetPhysicalMousePosition()
    {
        GetCursorPos(out POINT pt);
        return new System.Drawing.Point(pt.X - _screenX, pt.Y - _screenY);
    }

    private void UpdateCrosshair(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // 물리적 마우스 좌표 사용 (DPI 스케일 영향 없음)
        var pos = GetPhysicalMousePosition();
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

    /// <summary>
    /// 화면을 캡처하고 비트맵 반환 (오버레이 표시 전 호출)
    /// CopyFromScreen만 사용 (보안 프로그램 호환성)
    /// </summary>
    public static System.Drawing.Bitmap? CaptureScreen()
    {
        try
        {
            var virtualScreen = SystemInformation.VirtualScreen;
            var result = new System.Drawing.Bitmap(virtualScreen.Width, virtualScreen.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.CopyFromScreen(virtualScreen.X, virtualScreen.Y, 0, 0,
                    new System.Drawing.Size(virtualScreen.Width, virtualScreen.Height),
                    CopyPixelOperation.SourceCopy);
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    private BitmapSource ConvertToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
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
        // 물리적 마우스 좌표 사용 (DPI 스케일 영향 없음)
        _startPointPhysical = GetPhysicalMousePosition();
        _isSelecting = true;

        HelpPanel.Visibility = Visibility.Collapsed;

        SelectionBorder.Visibility = Visibility.Visible;
        SizeLabel.Visibility = Visibility.Visible;

        Canvas.SetLeft(SelectionBorder, _startPointPhysical.X);
        Canvas.SetTop(SelectionBorder, _startPointPhysical.Y);
        SelectionBorder.Width = 0;
        SelectionBorder.Height = 0;
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isSelecting) return;

        // 물리적 마우스 좌표 사용 (DPI 스케일 영향 없음)
        _currentPointPhysical = GetPhysicalMousePosition();

        int x = Math.Min(_startPointPhysical.X, _currentPointPhysical.X);
        int y = Math.Min(_startPointPhysical.Y, _currentPointPhysical.Y);
        int width = Math.Abs(_currentPointPhysical.X - _startPointPhysical.X);
        int height = Math.Abs(_currentPointPhysical.Y - _startPointPhysical.Y);

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;

        SizeText.Text = $"{width} x {height}";

        var labelTop = y - 30;
        if (labelTop < 0) labelTop = y + height + 5;

        Canvas.SetLeft(SizeLabel, x);
        Canvas.SetTop(SizeLabel, labelTop);

        UpdateBackgroundMask(x, y, width, height);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;

        _isSelecting = false;

        // 물리적 마우스 좌표 사용 (DPI 스케일 영향 없음)
        _currentPointPhysical = GetPhysicalMousePosition();

        int x = Math.Min(_startPointPhysical.X, _currentPointPhysical.X);
        int y = Math.Min(_startPointPhysical.Y, _currentPointPhysical.Y);
        int width = Math.Abs(_currentPointPhysical.X - _startPointPhysical.X);
        int height = Math.Abs(_currentPointPhysical.Y - _startPointPhysical.Y);

        if (width < 10 || height < 10)
        {
            DialogResult = false;
            Close();
            return;
        }

        // 물리적 좌표 그대로 사용
        _selectedRegion = new Rectangle(_screenX + x, _screenY + y, width, height);
        _imageRegion = new Rectangle(x, y, width, height);

        DialogResult = true;
        Close();
    }

    private void UpdateBackgroundMask(double x, double y, double width, double height)
    {
        var fullRect = new RectangleGeometry(new Rect(0, 0, _screenWidth, _screenHeight));
        var selectionRect = new RectangleGeometry(new Rect(x, y, width, height));

        var combinedGeometry = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            fullRect,
            selectionRect);

        DimOverlay.Clip = combinedGeometry;
    }
}
