using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Screenshot.Views;

/// <summary>
/// WinForms 기반 영역 선택 오버레이
/// PictureBox로 배경 이미지를 확실하게 표시
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

    // PictureBox로 배경 이미지 표시
    private readonly PictureBox _pictureBox;
    // 투명 패널로 선택 영역 오버레이
    private readonly SelectionPanel _selectionPanel;

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

        // Form 설정
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        KeyPreview = true;

        // PictureBox - 배경 이미지 표시 (가장 확실한 방법)
        _pictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.StretchImage,
            Image = capturedScreen
        };

        // 투명 선택 패널 - PictureBox 위에 겹침
        _selectionPanel = new SelectionPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        _selectionPanel.ShowHelp = true;

        // 선택 패널의 이벤트
        _selectionPanel.MouseDown += OnMouseDown;
        _selectionPanel.MouseMove += OnMouseMove;
        _selectionPanel.MouseUp += OnMouseUp;

        // 순서 중요: 선택 패널이 PictureBox 위에 와야 함
        Controls.Add(_selectionPanel);
        Controls.Add(_pictureBox);
        _selectionPanel.BringToFront();

        // 키보드 이벤트
        KeyDown += OnKeyDown;

        // Shown 이벤트에서 Win32 API로 물리적 크기 강제
        Shown += (s, e) =>
        {
            SetWindowPos(Handle, HWND_TOPMOST, _screenX, _screenY, _screenWidth, _screenHeight, SWP_SHOWWINDOW);

            GetWindowRect(Handle, out var rect);
            Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                $"Shown: Win32={rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top}, " +
                $"Client={ClientSize.Width}x{ClientSize.Height}, " +
                $"PB={_pictureBox.Width}x{_pictureBox.Height}, " +
                $"ImgDPI={capturedScreen.HorizontalResolution:F0}x{capturedScreen.VerticalResolution:F0}");
        };

        Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
            $"생성: Screen={_screenX},{_screenY},{_screenWidth}x{_screenHeight}, Img={capturedScreen.Width}x{capturedScreen.Height}");
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

            Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                $"캡처 완료: {result.Width}x{result.Height}, DPI={result.HorizontalResolution:F0}x{result.VerticalResolution:F0}");

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
        _selectionPanel.ShowHelp = false;
        _selectionPanel.Invalidate();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isSelecting) return;

        _currentPoint = e.Location;
        _selectionPanel.SelectionRect = GetSelectionRect();
        _selectionPanel.Invalidate();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isSelecting || e.Button != MouseButtons.Left) return;

        _isSelecting = false;
        _currentPoint = e.Location;

        var sel = GetSelectionRect();

        if (sel.Width < 10 || sel.Height < 10)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        // 좌표 변환: 패널 좌표 → 이미지 좌표
        GetWindowRect(Handle, out var winRect);
        int actualW = winRect.Right - winRect.Left;
        int actualH = winRect.Bottom - winRect.Top;
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
            _pictureBox.Image = null; // Dispose 방지 (원본은 외부 관리)
            _pictureBox.Dispose();
            _selectionPanel.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// 투명 선택 영역 패널 - PictureBox 위에 겹쳐서 표시
    /// </summary>
    private class SelectionPanel : Panel
    {
        public Rectangle SelectionRect { get; set; }
        public bool ShowHelp { get; set; } = true;

        private readonly Pen _selPen = new(Color.FromArgb(0, 120, 212), 2);
        private readonly SolidBrush _dimBrush = new(Color.FromArgb(128, 0, 0, 0));
        private readonly SolidBrush _labelBg = new(Color.FromArgb(204, 0, 0, 0));
        private readonly Font _sizeFont = new("Consolas", 10f);
        private readonly Font _helpFont = new("Segoe UI", 14f);
        private readonly Font _helpSubFont = new("Segoe UI", 10f);

        public SelectionPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 배경 그리지 않음 (투명)
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            if (ShowHelp)
            {
                DrawHelp(g);
                return;
            }

            if (SelectionRect.Width <= 0 || SelectionRect.Height <= 0) return;

            // 어두운 오버레이 (선택 영역 제외)
            using var region = new Region(new Rectangle(0, 0, Width, Height));
            region.Exclude(SelectionRect);
            g.FillRegion(_dimBrush, region);

            // 선택 사각형
            g.DrawRectangle(_selPen, SelectionRect);

            // 크기 레이블
            var text = $"{SelectionRect.Width} x {SelectionRect.Height}";
            var sz = g.MeasureString(text, _sizeFont);
            float lx = SelectionRect.X;
            float ly = SelectionRect.Y - sz.Height - 8;
            if (ly < 0) ly = SelectionRect.Bottom + 4;

            g.FillRectangle(_labelBg, lx, ly, sz.Width + 12, sz.Height + 6);
            g.DrawString(text, _sizeFont, Brushes.White, lx + 6, ly + 3);
        }

        private void DrawHelp(Graphics g)
        {
            var t1 = "드래그하여 캡처할 영역을 선택하세요";
            var t2 = "ESC: 취소  |  Enter: 전체 화면 캡처";
            var s1 = g.MeasureString(t1, _helpFont);
            var s2 = g.MeasureString(t2, _helpSubFont);
            var pw = Math.Max(s1.Width, s2.Width) + 60;
            var ph = s1.Height + s2.Height + 40;
            var px = (Width - pw) / 2;
            var py = (Height - ph) / 2;

            using var bg = new SolidBrush(Color.FromArgb(204, 30, 30, 30));
            g.FillRectangle(bg, px, py, pw, ph);
            g.DrawString(t1, _helpFont, Brushes.White, px + (pw - s1.Width) / 2, py + 15);
            using var gray = new SolidBrush(Color.FromArgb(136, 136, 136));
            g.DrawString(t2, _helpSubFont, gray, px + (pw - s2.Width) / 2, py + 15 + s1.Height + 10);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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
}
