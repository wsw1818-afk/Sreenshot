using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
    // 물리적 좌표로 시작점과 현재점 저장
    private System.Windows.Point _startPoint;
    private System.Windows.Point _currentPoint;
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
        _startPoint = e.GetPosition(this);
        _isSelecting = true;

        HelpPanel.Visibility = Visibility.Collapsed;

        SelectionBorder.Visibility = Visibility.Visible;
        SizeLabel.Visibility = Visibility.Visible;

        Canvas.SetLeft(SelectionBorder, _startPoint.X);
        Canvas.SetTop(SelectionBorder, _startPoint.Y);
        SelectionBorder.Width = 0;
        SelectionBorder.Height = 0;

        CaptureMouse();
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isSelecting) return;

        _currentPoint = e.GetPosition(this);

        double x = Math.Min(_startPoint.X, _currentPoint.X);
        double y = Math.Min(_startPoint.Y, _currentPoint.Y);
        double width = Math.Abs(_currentPoint.X - _startPoint.X);
        double height = Math.Abs(_currentPoint.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;

        SizeText.Text = $"{(int)width} x {(int)height}";

        var labelTop = y - 30;
        if (labelTop < 0) labelTop = y + height + 5;

        Canvas.SetLeft(SizeLabel, x);
        Canvas.SetTop(SizeLabel, labelTop);

        UpdateBackgroundMask(x, y, width, height);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;

        ReleaseMouseCapture();
        _isSelecting = false;

        _currentPoint = e.GetPosition(this);

        double x = Math.Min(_startPoint.X, _currentPoint.X);
        double y = Math.Min(_startPoint.Y, _currentPoint.Y);
        double width = Math.Abs(_currentPoint.X - _startPoint.X);
        double height = Math.Abs(_currentPoint.Y - _startPoint.Y);

        if (width < 10 || height < 10)
        {
            DialogResult = false;
            Close();
            return;
        }

        int physicalX = _screenX + (int)x;
        int physicalY = _screenY + (int)y;

        _selectedRegion = new Rectangle(physicalX, physicalY, (int)width, (int)height);
        _imageRegion = new Rectangle((int)x, (int)y, (int)width, (int)height);

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
