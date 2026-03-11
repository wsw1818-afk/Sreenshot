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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // Low-level 키보드 훅 (포커스 상실 시에도 ESC 감지)
    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr hInstance, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_ESCAPE = 0x1B;

    private IntPtr _keyboardHook;
    private LowLevelKeyboardProc? _keyboardProc; // prevent GC

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
    private volatile bool _inputEnabled;
    private volatile bool _closingByUser;
    private System.Windows.Forms.Timer? _safetyTimer;

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

            // DPI를 96으로 강제 설정 (GDI+ 자동 DPI 보정에 의한 확대/축소 방지)
            _displayBitmap.SetResolution(96f, 96f);

            Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                $"임시파일 로드 성공: {_tempFilePath}, DisplayBmp={_displayBitmap.Width}x{_displayBitmap.Height}");
        }
        catch (Exception ex)
        {
            Services.Capture.CaptureLogger.Error("CaptureOverlayForm",
                $"임시파일 로드 실패, 원본 비트맵 복제 사용: {ex.Message}", ex);
            // 원본 참조 대신 복제본 사용 → Dispose 시 원본(_capturedScreen) 보호
            _displayBitmap = (Bitmap)capturedScreen.Clone();
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

        // Deactivate 이벤트는 WndProc에서 WM_ACTIVATE(WA_INACTIVE)를 차단하므로
        // 정상적으로는 발생하지 않지만, 만약 발생 시 로그만 남김
        Deactivate += (s, e) =>
        {
            Services.Capture.CaptureLogger.Warn("CaptureOverlayForm",
                "Deactivate 이벤트 발생 (WndProc 차단 누락?)");
        };

        FormClosing += (s, e) =>
        {
            Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                $"FormClosing: DialogResult={DialogResult}, CloseReason={e.CloseReason}, ByUser={_closingByUser}");

            // 사용자가 닫은 게 아니면 닫기를 차단 (WndProc에서 WM_ACTIVATE 차단하므로 거의 발생 안함)
            if (!_closingByUser && !IsDisposed)
            {
                e.Cancel = true;
                Services.Capture.CaptureLogger.Info("CaptureOverlayForm", "비사용자 닫기 차단 (e.Cancel = true)");
                // 차단 후 오버레이를 다시 최상위로 복구
                BeginInvoke(() =>
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        SetWindowPos(Handle, HWND_TOPMOST, _screenX, _screenY, _screenWidth, _screenHeight, SWP_SHOWWINDOW);
                        ForceSetForeground();
                        Services.Capture.CaptureLogger.Info("CaptureOverlayForm", "비사용자 닫기 차단 후 최상위 복구");
                    }
                });
            }
        };

        // 안전 타이머: 30초 후 자동 취소 (오버레이가 먹통 상태 방지)
        _safetyTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        _safetyTimer.Tick += (s, e) =>
        {
            _safetyTimer?.Stop();
            _safetyTimer?.Dispose();
            _safetyTimer = null;
            if (!_closingByUser && !IsDisposed)
            {
                Services.Capture.CaptureLogger.Warn("CaptureOverlayForm", "안전 타이머 만료 (30초) - 자동 취소");
                _closingByUser = true;
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        _safetyTimer.Start();

        // Shown 이벤트에서 Win32 API로 물리적 크기 강제 + 강제 다시 그리기
        Shown += (s, e) =>
        {
            // 오버레이가 표시된 시점부터 안전 타이머 시작 (초기화 시간 제외)
            _safetyTimer?.Stop();
            _safetyTimer?.Start();

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

            // 키보드 포커스 명시적 설정 (FormBorderStyle.None에서 자동 포커스 안 받는 문제 해결)
            ForceSetForeground();
            Activate();
            Focus();

            // Low-level 키보드 훅 설치 (포커스가 없어도 ESC 감지)
            InstallKeyboardHook();

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

    // ProcessCmdKey: KeyDown보다 상위 레벨에서 키를 잡음 → 포커스 문제 시에도 ESC 확실히 처리
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            _closingByUser = true;
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        if (keyData == Keys.Enter)
        {
            _closingByUser = true;
            _selectedRegion = new Rectangle(_screenX, _screenY, _screenWidth, _screenHeight);
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
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
        // 우클릭으로 취소
        if (e.Button == MouseButtons.Right)
        {
            _closingByUser = true;
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left) return;
        if (!_inputEnabled) return;

        // 사용자가 조작을 시작하면 안전 타이머 완전 해제
        _safetyTimer?.Stop();
        _safetyTimer?.Dispose();
        _safetyTimer = null;

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

    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero) return;

        _keyboardProc = KeyboardHookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(curModule.ModuleName), 0);

        Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
            $"키보드 훅 설치: {(_keyboardHook != IntPtr.Zero ? "성공" : "실패")}");
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            _keyboardProc = null;
            Services.Capture.CaptureLogger.Info("CaptureOverlayForm", "키보드 훅 해제");
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == VK_ESCAPE)
            {
                Services.Capture.CaptureLogger.Info("CaptureOverlayForm", "Low-level 키보드 훅: ESC 감지");

                try
                {
                    // UI 스레드에서 취소 실행
                    if (IsHandleCreated && !IsDisposed && !_closingByUser)
                    {
                        BeginInvoke(() =>
                        {
                            if (IsDisposed || _closingByUser) return;
                            _closingByUser = true;
                            DialogResult = DialogResult.Cancel;
                            Close();
                        });
                        return (IntPtr)1; // ESC 키 소비 (다른 앱에 전달 안 함)
                    }
                }
                catch (ObjectDisposedException) { /* Form이 닫히는 중 - 무시 */ }
                catch (InvalidOperationException) { /* 핸들 무효 - 무시 */ }
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
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
        UninstallKeyboardHook();

        if (disposing)
        {
            _safetyTimer?.Stop();
            _safetyTimer?.Dispose();
            _safetyTimer = null;

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

    /// <summary>
    /// WM_ACTIVATE 메시지에서 WA_INACTIVE를 차단하여 Deactivate 이벤트 자체를 방지.
    /// 오버레이가 ShowDialog()로 표시된 동안에는 절대 포커스를 빼앗기지 않도록 한다.
    /// 이를 통해 Deactivate→FormClosing→복구 시도→교착 상태 문제를 원천 차단.
    /// </summary>
    private const int WM_ACTIVATE = 0x0006;
    private const int WA_INACTIVE = 0;
    private const int WM_NCACTIVATE = 0x0086;

    protected override void WndProc(ref Message m)
    {
        // WM_ACTIVATE: wParam의 하위 워드가 WA_INACTIVE(0)이면 비활성화 시도
        if (m.Msg == WM_ACTIVATE && (m.WParam.ToInt32() & 0xFFFF) == WA_INACTIVE)
        {
            if (!_closingByUser)
            {
                Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                    "WM_ACTIVATE(WA_INACTIVE) 차단");
                // 메시지를 처리하지 않고 무시 (base.WndProc 호출 안함)
                return;
            }
        }

        // WM_NCACTIVATE: 비클라이언트 영역 비활성화도 차단
        if (m.Msg == WM_NCACTIVATE && m.WParam == IntPtr.Zero)
        {
            if (!_closingByUser)
            {
                Services.Capture.CaptureLogger.Info("CaptureOverlayForm",
                    "WM_NCACTIVATE(비활성) 차단");
                m.Result = IntPtr.Zero; // 비활성화 거부
                return;
            }
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// AttachThreadInput을 사용한 강력한 전경 창 전환.
    /// Windows의 전경 창 잠금 정책을 우회하여 확실히 포커스를 가져옵니다.
    /// </summary>
    private void ForceSetForeground()
    {
        try
        {
            var foregroundHwnd = GetForegroundWindow();
            if (foregroundHwnd == Handle) return; // 이미 전경

            var foregroundThread = GetWindowThreadProcessId(foregroundHwnd, out _);
            var currentThread = GetCurrentThreadId();

            // 전경 창의 스레드에 입력을 연결하여 SetForegroundWindow 권한 획득
            if (foregroundThread != currentThread)
                AttachThreadInput(currentThread, foregroundThread, true);

            SetWindowPos(Handle, HWND_TOPMOST, _screenX, _screenY, _screenWidth, _screenHeight, SWP_SHOWWINDOW);
            SetForegroundWindow(Handle);

            if (foregroundThread != currentThread)
                AttachThreadInput(currentThread, foregroundThread, false);
        }
        catch (Exception ex)
        {
            Services.Capture.CaptureLogger.Warn("CaptureOverlayForm", $"ForceSetForeground 실패: {ex.Message}");
            // 폴백: 기본 방식
            SetWindowPos(Handle, HWND_TOPMOST, _screenX, _screenY, _screenWidth, _screenHeight, SWP_SHOWWINDOW);
            SetForegroundWindow(Handle);
        }
    }
}
