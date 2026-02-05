using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Screenshot.Views;

/// <summary>
/// WinForms 기반 영역 선택 오버레이
/// WPF Image 컨트롤의 블랙스크린 문제를 우회하기 위해 WinForms 사용
/// </summary>
public class CaptureOverlayForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;

    private Point _startPoint;
    private Point _currentPoint;
    private bool _isSelecting;
    private Rectangle _selectedRegion;
    private Rectangle _imageRegion;

    private readonly int _screenX;
    private readonly int _screenY;
    private readonly int _screenWidth;
    private readonly int _screenHeight;

    private Bitmap? _capturedScreen;
    private Bitmap? _backgroundBitmap;

    // 선택 영역 브러시/펜
    private readonly Pen _selectionPen = new(Color.FromArgb(0, 120, 212), 2);
    private readonly SolidBrush _dimBrush = new(Color.FromArgb(128, 0, 0, 0));
    private readonly SolidBrush _labelBgBrush = new(Color.FromArgb(204, 0, 0, 0));
    private readonly Font _sizeFont = new("Consolas", 10f);
    private readonly Font _helpFont = new("Segoe UI", 14f);
    private readonly Font _helpSubFont = new("Segoe UI", 10f);

    private bool _showHelp = true;

    public Rectangle SelectedRegion => _selectedRegion;
    public Rectangle ImageRegion => _imageRegion;
    public Bitmap? CapturedScreen => _capturedScreen;

    public CaptureOverlayForm(Bitmap capturedScreen)
    {
        // DPI 자동 스케일링 비활성화 - 물리적 픽셀로 직접 제어
        AutoScaleMode = AutoScaleMode.None;

        var virtualScreen = SystemInformation.VirtualScreen;
        _screenX = virtualScreen.X;
        _screenY = virtualScreen.Y;
        _screenWidth = virtualScreen.Width;
        _screenHeight = virtualScreen.Height;

        _capturedScreen = capturedScreen;

        // 배경 이미지 복사 (원본 보존)
        _backgroundBitmap = new Bitmap(capturedScreen);

        // Form 설정
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;

        // 이벤트 핸들러
        KeyDown += OnKeyDown;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        Paint += OnPaint;

        // Shown 이벤트에서 Win32 API로 정확한 물리적 위치/크기 강제 설정
        Shown += (s, e) =>
        {
            SetWindowPos(Handle, HWND_TOPMOST, _screenX, _screenY, _screenWidth, _screenHeight, SWP_SHOWWINDOW);

            GetWindowRect(Handle, out var rect);
            Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                $"Shown 후 실제 크기: Win32Rect={rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top}, " +
                $"ClientSize={ClientSize.Width}x{ClientSize.Height}, Bounds={Bounds}");
        };

        Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
            $"WinForms 오버레이 생성: Screen={_screenX},{_screenY},{_screenWidth}x{_screenHeight}, BG={_backgroundBitmap.Width}x{_backgroundBitmap.Height}");
    }

    /// <summary>
    /// 화면 캡처 (CopyFromScreen)
    /// </summary>
    public static Bitmap? CaptureScreen()
    {
        try
        {
            var virtualScreen = SystemInformation.VirtualScreen;
            var result = new Bitmap(virtualScreen.Width, virtualScreen.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.CopyFromScreen(virtualScreen.X, virtualScreen.Y, 0, 0,
                    new Size(virtualScreen.Width, virtualScreen.Height),
                    CopyPixelOperation.SourceCopy);
            }

            // 디버그: 캡처 원본 저장
            try
            {
                var debugDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SmartCapture", "Debug");
                System.IO.Directory.CreateDirectory(debugDir);
                var debugPath = System.IO.Path.Combine(debugDir, $"winforms_capture_{DateTime.Now:HHmmss}.png");
                result.Save(debugPath, ImageFormat.Png);
                Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                    $"디버그 이미지 저장: {debugPath}, 크기: {result.Width}x{result.Height}");
            }
            catch { }

            return result;
        }
        catch (Exception ex)
        {
            Services.Capture.CaptureLogger.Error("CaptureOverlayForm", $"화면 캡처 실패: {ex.Message}", ex);
            return null;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
        else if (e.KeyCode == Keys.Enter)
        {
            _selectedRegion = new Rectangle(_screenX, _screenY, _screenWidth, _screenHeight);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        _startPoint = e.Location;
        _isSelecting = true;
        _showHelp = false;
        Invalidate();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isSelecting) return;

        _currentPoint = e.Location;
        Invalidate();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isSelecting || e.Button != MouseButtons.Left) return;

        _isSelecting = false;
        _currentPoint = e.Location;

        int x = Math.Min(_startPoint.X, _currentPoint.X);
        int y = Math.Min(_startPoint.Y, _currentPoint.Y);
        int width = Math.Abs(_currentPoint.X - _startPoint.X);
        int height = Math.Abs(_currentPoint.Y - _startPoint.Y);

        if (width < 10 || height < 10)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        // Form 좌표 → 이미지(물리적 픽셀) 좌표 변환
        // Win32 API로 실제 창 크기 가져오기 (DPI 스케일링 무관)
        GetWindowRect(Handle, out var winRect);
        int actualW = winRect.Right - winRect.Left;
        int actualH = winRect.Bottom - winRect.Top;
        double scaleX = (double)_screenWidth / actualW;
        double scaleY = (double)_screenHeight / actualH;

        int imgX = (int)Math.Round(x * scaleX);
        int imgY = (int)Math.Round(y * scaleY);
        int imgW = (int)Math.Round(width * scaleX);
        int imgH = (int)Math.Round(height * scaleY);

        _selectedRegion = new Rectangle(_screenX + imgX, _screenY + imgY, imgW, imgH);
        _imageRegion = new Rectangle(imgX, imgY, imgW, imgH);

        Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
            $"영역 선택: Form({x},{y},{width}x{height}) → Image({imgX},{imgY},{imgW}x{imgH}), Scale={scaleX:F2}x{scaleY:F2}");

        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;

        // 배경 이미지를 1:1 픽셀로 직접 그리기 (보간 없음)
        if (_backgroundBitmap != null)
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.DrawImageUnscaled(_backgroundBitmap, 0, 0);
        }

        if (_showHelp)
        {
            DrawHelpPanel(g);
            return;
        }

        if (!_isSelecting) return;

        int x = Math.Min(_startPoint.X, _currentPoint.X);
        int y = Math.Min(_startPoint.Y, _currentPoint.Y);
        int width = Math.Abs(_currentPoint.X - _startPoint.X);
        int height = Math.Abs(_currentPoint.Y - _startPoint.Y);

        // 어두운 오버레이 (선택 영역 제외)
        var region = new Region(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
        if (width > 0 && height > 0)
        {
            region.Exclude(new Rectangle(x, y, width, height));
        }
        g.FillRegion(_dimBrush, region);
        region.Dispose();

        // 선택 사각형
        if (width > 0 && height > 0)
        {
            g.DrawRectangle(_selectionPen, x, y, width, height);
        }

        // 크기 레이블
        var sizeText = $"{width} x {height}";
        var textSize = g.MeasureString(sizeText, _sizeFont);
        var labelX = (float)x;
        var labelY = y - textSize.Height - 8;
        if (labelY < 0) labelY = y + height + 4;

        g.FillRectangle(_labelBgBrush, labelX, labelY, textSize.Width + 12, textSize.Height + 6);
        g.DrawString(sizeText, _sizeFont, Brushes.White, labelX + 6, labelY + 3);
    }

    private void DrawHelpPanel(Graphics g)
    {
        var helpText = "드래그하여 캡처할 영역을 선택하세요";
        var subText = "ESC: 취소  |  Enter: 전체 화면 캡처";

        var helpSize = g.MeasureString(helpText, _helpFont);
        var subSize = g.MeasureString(subText, _helpSubFont);

        var panelWidth = Math.Max(helpSize.Width, subSize.Width) + 60;
        var panelHeight = helpSize.Height + subSize.Height + 40;

        var panelX = (ClientSize.Width - panelWidth) / 2;
        var panelY = (ClientSize.Height - panelHeight) / 2;

        // 패널 배경
        using var panelBrush = new SolidBrush(Color.FromArgb(204, 30, 30, 30));
        var panelRect = new RectangleF(panelX, panelY, panelWidth, panelHeight);
        g.FillRectangle(panelBrush, panelRect);

        // 메인 텍스트
        var textX = panelX + (panelWidth - helpSize.Width) / 2;
        var textY = panelY + 15;
        g.DrawString(helpText, _helpFont, Brushes.White, textX, textY);

        // 서브 텍스트
        using var grayBrush = new SolidBrush(Color.FromArgb(136, 136, 136));
        var subX = panelX + (panelWidth - subSize.Width) / 2;
        var subY = textY + helpSize.Height + 10;
        g.DrawString(subText, _helpSubFont, grayBrush, subX, subY);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _selectionPen.Dispose();
            _dimBrush.Dispose();
            _labelBgBrush.Dispose();
            _sizeFont.Dispose();
            _helpFont.Dispose();
            _helpSubFont.Dispose();
            _backgroundBitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
