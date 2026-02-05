using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Screenshot.Views;

/// <summary>
/// WinForms 기반 영역 선택 오버레이
/// 단일 Form에서 Paint로 배경+선택 UI 직접 렌더링
/// </summary>
public class CaptureOverlayForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

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

    // 임시 파일 경로에서 로드한 독립 비트맵 (표시 전용)
    private Bitmap? _displayBitmap;
    private string? _tempFilePath;

    private bool _showHelp = true;
    private bool _inputEnabled;
    private bool _closingByUser;

    // 그리기 리소스
    private readonly Pen _selPen = new(Color.FromArgb(0, 120, 212), 2);
    private readonly SolidBrush _dimBrush = new(Color.FromArgb(128, 0, 0, 0));
    private readonly SolidBrush _labelBg = new(Color.FromArgb(204, 0, 0, 0));
    private readonly Font _sizeFont = new("Consolas", 10f);
    private readonly Font _helpFont = new("Segoe UI", 14f);
    private readonly Font _helpSubFont = new("Segoe UI", 10f);

    public Rectangle SelectedRegion => _selectedRegion;
    public Rectangle ImageRegion => _imageRegion;
    public Bitmap? CapturedScreen => _capturedScreen;

    public CaptureOverlayForm(Bitmap capturedScreen)
    {
        AutoScaleMode = AutoScaleMode.None;

        var virtualScreen = SystemInformation.VirtualScreen;
        _screenX = virtualScreen.X;
        _screenY = virtualScreen.Y;
        _screenWidth = virtualScreen.Width;
        _screenHeight = virtualScreen.Height;

        _capturedScreen = capturedScreen;

        // 캡처된 이미지를 임시 파일로 저장 후 독립적으로 로드
        // → GDI+ 비트맵 핸들 공유 문제 완전 우회
        try
        {
            _tempFilePath = Path.Combine(Path.GetTempPath(), $"smartcapture_overlay_{Guid.NewGuid():N}.png");
            capturedScreen.Save(_tempFilePath, ImageFormat.Png);

            // 파일에서 독립 비트맵 로드 (파일 락 방지 위해 MemoryStream 사용)
            var bytes = File.ReadAllBytes(_tempFilePath);
            using var ms = new MemoryStream(bytes);
            _displayBitmap = new Bitmap(ms);

            Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                $"임시파일 로드 성공: {_tempFilePath}, DisplayBmp={_displayBitmap.Width}x{_displayBitmap.Height}");
        }
        catch (Exception ex)
        {
            Services.Capture.CaptureLogger.Error("CaptureOverlayForm",
                $"임시파일 로드 실패, 원본 비트맵 사용: {ex.Message}", ex);
            _displayBitmap = capturedScreen;
        }

        // Form 설정
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        KeyPreview = true;

        // 더블 버퍼링 직접 설정
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint, true);
        SetStyle(ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

        // 이벤트
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
        Paint += OnPaint;

        // 포커스 상실 시 Win32 API로 강제 최상단 복귀
        Deactivate += (s, e) =>
        {
            try
            {
                if (IsDisposed || _closingByUser || !IsHandleCreated) return;
                Services.Capture.CaptureLogger.Info("CaptureOverlayForm", "Deactivate 발생 - Win32 최상단 복귀");
                SetWindowPos(Handle, HWND_TOPMOST, _screenX, _screenY, _screenWidth, _screenHeight, SWP_SHOWWINDOW);
                SetForegroundWindow(Handle);
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Services.Capture.CaptureLogger.Warn("CaptureOverlayForm", $"Deactivate 처리 중 예외: {ex.Message}");
            }
        };

        FormClosing += (s, e) =>
        {
            Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                $"FormClosing: DialogResult={DialogResult}, CloseReason={e.CloseReason}, ByUser={_closingByUser}");
        };

        // Shown 이벤트에서 Win32 API로 물리적 크기 강제 + 강제 다시 그리기
        Shown += (s, e) =>
        {
            SetWindowPos(Handle, HWND_TOPMOST, _screenX, _screenY, _screenWidth, _screenHeight, SWP_SHOWWINDOW);

            GetWindowRect(Handle, out var rect);
            Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                $"Shown: Win32={rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top}, " +
                $"Client={ClientSize.Width}x{ClientSize.Height}, " +
                $"DisplayBmp={_displayBitmap?.Width}x{_displayBitmap?.Height}, " +
                $"DisplayBmpDPI={_displayBitmap?.HorizontalResolution:F0}x{_displayBitmap?.VerticalResolution:F0}");

            // 강제 다시 그리기
            Invalidate();
            Update();

            // 마우스 왼쪽 버튼이 떼어진 후에만 입력 활성화 (버튼 클릭 잔여 이벤트 방지)
            Task.Run(async () =>
            {
                // 마우스 버튼이 떼어질 때까지 대기 (최대 2초)
                for (int i = 0; i < 40; i++)
                {
                    await Task.Delay(50);
                    if ((Control.MouseButtons & MouseButtons.Left) == 0) break;
                }
                await Task.Delay(50); // 추가 버퍼
                _inputEnabled = true;
            });
        };

        Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
            $"생성: Screen={_screenX},{_screenY},{_screenWidth}x{_screenHeight}, " +
            $"Img={capturedScreen.Width}x{capturedScreen.Height}, " +
            $"DisplayBmp={_displayBitmap?.Width}x{_displayBitmap?.Height}");
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;

        // 1. 배경 이미지 그리기
        if (_displayBitmap != null)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;

            g.DrawImage(_displayBitmap, 0, 0, ClientSize.Width, ClientSize.Height);

            g.CompositingMode = CompositingMode.SourceOver;
        }
        else
        {
            g.Clear(Color.Black);
        }

        // 2. 도움말 텍스트
        if (_showHelp)
        {
            DrawHelp(g);
            return;
        }

        // 3. 선택 영역 그리기
        if (_isSelecting && _selectionRect.Width > 0 && _selectionRect.Height > 0)
        {
            // 어두운 오버레이 (선택 영역 제외)
            using var region = new Region(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            region.Exclude(_selectionRect);
            g.FillRegion(_dimBrush, region);

            // 선택 사각형 테두리
            g.DrawRectangle(_selPen, _selectionRect);

            // 크기 레이블
            var text = $"{_selectionRect.Width} x {_selectionRect.Height}";
            var sz = g.MeasureString(text, _sizeFont);
            float lx = _selectionRect.X;
            float ly = _selectionRect.Y - sz.Height - 8;
            if (ly < 0) ly = _selectionRect.Bottom + 4;

            g.FillRectangle(_labelBg, lx, ly, sz.Width + 12, sz.Height + 6);
            g.DrawString(text, _sizeFont, Brushes.White, lx + 6, ly + 3);
        }
    }

    private Rectangle _selectionRect;

    private void DrawHelp(Graphics g)
    {
        var t1 = "드래그하여 캡처할 영역을 선택하세요";
        var t2 = "ESC: 취소  |  Enter: 전체 화면 캡처";
        var s1 = g.MeasureString(t1, _helpFont);
        var s2 = g.MeasureString(t2, _helpSubFont);
        var pw = Math.Max(s1.Width, s2.Width) + 60;
        var ph = s1.Height + s2.Height + 40;
        var px = (ClientSize.Width - pw) / 2;
        var py = (ClientSize.Height - ph) / 2;

        using var bg = new SolidBrush(Color.FromArgb(204, 30, 30, 30));
        g.FillRectangle(bg, px, py, pw, ph);
        g.DrawString(t1, _helpFont, Brushes.White, px + (pw - s1.Width) / 2, py + 15);
        using var gray = new SolidBrush(Color.FromArgb(136, 136, 136));
        g.DrawString(t2, _helpSubFont, gray, px + (pw - s2.Width) / 2, py + 15 + s1.Height + 10);
    }

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

            // 캡처 결과 검증: 픽셀 샘플링으로 검은 화면인지 확인
            int blackCount = 0;
            int sampleCount = 0;
            var checkPoints = new[] {
                (result.Width / 4, result.Height / 4),
                (result.Width / 2, result.Height / 2),
                (result.Width * 3 / 4, result.Height * 3 / 4),
                (100, 100),
                (result.Width - 100, 100)
            };
            foreach (var (px, py) in checkPoints)
            {
                if (px >= 0 && px < result.Width && py >= 0 && py < result.Height)
                {
                    var pixel = result.GetPixel(px, py);
                    sampleCount++;
                    if (pixel.R < 10 && pixel.G < 10 && pixel.B < 10) blackCount++;
                    Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                        $"캡처픽셀({px},{py}): R={pixel.R} G={pixel.G} B={pixel.B} A={pixel.A}");
                }
            }

            // 디버그: 캡처된 이미지 파일로 저장
            try
            {
                var debugDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SmartCapture", "Debug");
                Directory.CreateDirectory(debugDir);
                var debugPath = Path.Combine(debugDir, $"winforms_capture_{DateTime.Now:HHmmss}.png");
                result.Save(debugPath, ImageFormat.Png);
                Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                    $"디버그이미지 저장: {debugPath}");
            }
            catch { }

            Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                $"캡처 완료: {result.Width}x{result.Height}, DPI={result.HorizontalResolution:F0}x{result.VerticalResolution:F0}, " +
                $"BlackPixels={blackCount}/{sampleCount}");

            return result;
        }
        catch (Exception ex)
        {
            Services.Capture.CaptureLogger.Error("CaptureOverlayForm", $"캡처 실패: {ex.Message}", ex);
            return null;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            _closingByUser = true;
            DialogResult = DialogResult.Cancel;
            Close();
        }
        else if (e.KeyCode == Keys.Enter)
        {
            _closingByUser = true;
            _selectedRegion = new Rectangle(_screenX, _screenY, _screenWidth, _screenHeight);
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (!_inputEnabled) return;

        _startPoint = e.Location;
        _isSelecting = true;
        _showHelp = false;
        Invalidate();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isSelecting) return;

        _currentPoint = e.Location;
        _selectionRect = GetSelectionRect();
        Invalidate();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isSelecting || e.Button != MouseButtons.Left) return;

        _isSelecting = false;
        _currentPoint = e.Location;

        var sel = GetSelectionRect();

        if (sel.Width < 10 || sel.Height < 10)
        {
            // 선택 영역이 너무 작으면 도움말로 복귀 (취소하지 않음)
            _showHelp = true;
            Invalidate();
            return;
        }

        // 좌표 변환: 폼 좌표 → 이미지 좌표
        GetWindowRect(Handle, out var winRect);
        int actualW = Math.Max(1, winRect.Right - winRect.Left);
        int actualH = Math.Max(1, winRect.Bottom - winRect.Top);
        double scaleX = (double)_screenWidth / actualW;
        double scaleY = (double)_screenHeight / actualH;

        int imgX = (int)Math.Round(sel.X * scaleX);
        int imgY = (int)Math.Round(sel.Y * scaleY);
        int imgW = (int)Math.Round(sel.Width * scaleX);
        int imgH = (int)Math.Round(sel.Height * scaleY);

        _selectedRegion = new Rectangle(_screenX + imgX, _screenY + imgY, imgW, imgH);
        _imageRegion = new Rectangle(imgX, imgY, imgW, imgH);

        Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
            $"선택: Panel({sel.X},{sel.Y},{sel.Width}x{sel.Height}) → Img({imgX},{imgY},{imgW}x{imgH}), Scale={scaleX:F2}x{scaleY:F2}");

        _closingByUser = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private Rectangle GetSelectionRect()
    {
        int x = Math.Min(_startPoint.X, _currentPoint.X);
        int y = Math.Min(_startPoint.Y, _currentPoint.Y);
        int w = Math.Abs(_currentPoint.X - _startPoint.X);
        int h = Math.Abs(_currentPoint.Y - _startPoint.Y);
        return new Rectangle(x, y, w, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _displayBitmap?.Dispose();
            _displayBitmap = null;

            // 임시 파일 삭제
            if (_tempFilePath != null && File.Exists(_tempFilePath))
            {
                try { File.Delete(_tempFilePath); }
                catch (Exception ex)
                {
                    Services.Capture.CaptureLogger.Warn("CaptureOverlayForm", $"임시 파일 삭제 실패: {_tempFilePath}, {ex.Message}");
                }
            }

            _selPen.Dispose();
            _dimBrush.Dispose();
            _labelBg.Dispose();
            _sizeFont.Dispose();
            _helpFont.Dispose();
            _helpSubFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
