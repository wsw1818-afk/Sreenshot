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
        // WPF 좌표 저장 (UI 표시용)
        _startPointWpf = e.GetPosition(this);
        _isSelecting = true;

        HelpPanel.Visibility = Visibility.Collapsed;

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
        if (!_isSelecting) return;

        _isSelecting = false;

        // WPF 좌표 사용 (UI 표시용)
        _currentPointWpf = e.GetPosition(this);

        double wpfX = Math.Min(_startPointWpf.X, _currentPointWpf.X);
        double wpfY = Math.Min(_startPointWpf.Y, _currentPointWpf.Y);
        double wpfWidth = Math.Abs(_currentPointWpf.X - _startPointWpf.X);
        double wpfHeight = Math.Abs(_currentPointWpf.Y - _startPointWpf.Y);

        if (wpfWidth < 10 || wpfHeight < 10)
        {
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
        var fullRect = new RectangleGeometry(new Rect(0, 0, _screenWidth, _screenHeight));
        var selectionRect = new RectangleGeometry(new Rect(x, y, width, height));

        var combinedGeometry = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            fullRect,
            selectionRect);

        DimOverlay.Clip = combinedGeometry;
    }
}
