using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Screenshot.Services;

/// <summary>
/// 스크롤 캡처 서비스 - 긴 페이지를 자동 스크롤하며 캡처
/// </summary>
public class ScrollCaptureService
{
    #region Win32 API

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetScrollInfo(IntPtr hWnd, int nBar, ref SCROLLINFO lpScrollInfo);

    [DllImport("user32.dll")]
    private static extern int SetScrollPos(IntPtr hWnd, int nBar, int nPos, bool bRedraw);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, IntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private const uint PW_CLIENTONLY = 0x1;
    private const uint PW_RENDERFULLCONTENT = 0x2;

    // 마우스 이벤트 플래그
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    // 키보드 이벤트 플래그
    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // 가상 키 코드
    private const byte VK_CONTROL = 0x11;
    private const byte VK_HOME = 0x24;
    private const byte VK_END = 0x23;
    private const byte VK_PRIOR = 0x21; // Page Up
    private const byte VK_NEXT = 0x22;  // Page Down

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public uint cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }

    private const int SB_VERT = 1;
    private const uint SIF_ALL = 0x17;
    private const uint WM_VSCROLL = 0x0115;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const int SB_PAGEDOWN = 3;
    private const int SB_BOTTOM = 7;
    private const int SB_TOP = 6;
    private const int SRCCOPY = 0x00CC0020;

    #endregion

    /// <summary>
    /// 스크롤 캡처 진행률 이벤트
    /// </summary>
    public event Action<int, int>? ProgressChanged;

    /// <summary>
    /// 활성 창의 스크롤 캡처
    /// </summary>
    public async Task<Bitmap?> CaptureScrollingWindowAsync(int delayMs = 300)
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return null;

        return await CaptureScrollingWindowAsync(hWnd, delayMs);
    }

    /// <summary>
    /// 지정된 창의 스크롤 캡처
    /// </summary>
    public async Task<Bitmap?> CaptureScrollingWindowAsync(IntPtr hWnd, int delayMs = 300)
    {
        if (hWnd == IntPtr.Zero) return null;

        // 창을 포커스
        SetForegroundWindow(hWnd);
        await Task.Delay(100);

        // 창 크기 가져오기
        if (!GetClientRect(hWnd, out var clientRect))
            return null;

        var captureWidth = clientRect.Width;
        var captureHeight = clientRect.Height;

        if (captureWidth <= 0 || captureHeight <= 0)
            return null;

        // 캡처 목록
        var captures = new List<Bitmap>();

        try
        {
            // 먼저 맨 위로 스크롤
            ScrollToTop(hWnd);
            await Task.Delay(delayMs);

            // 첫 번째 캡처
            var firstCapture = CaptureClientArea(hWnd);
            if (firstCapture == null) return null;
            captures.Add(firstCapture);

            // 이전 캡처와 비교할 해시
            var previousHash = GetImageHash(firstCapture);
            int maxScrollAttempts = 50; // 최대 스크롤 횟수 제한
            int sameHashCount = 0;

            ProgressChanged?.Invoke(1, -1);

            for (int i = 0; i < maxScrollAttempts; i++)
            {
                // 한 페이지 아래로 스크롤
                ScrollDown(hWnd, captureHeight - 50); // 약간 겹치게 스크롤
                await Task.Delay(delayMs);

                // 캡처
                var capture = CaptureClientArea(hWnd);
                if (capture == null) break;

                // 이전 캡처와 동일한지 확인 (스크롤 끝 감지)
                var currentHash = GetImageHash(capture);
                if (currentHash == previousHash)
                {
                    sameHashCount++;
                    if (sameHashCount >= 2) // 2번 연속 같으면 끝
                    {
                        capture.Dispose();
                        break;
                    }
                }
                else
                {
                    sameHashCount = 0;
                }

                captures.Add(capture);
                previousHash = currentHash;

                ProgressChanged?.Invoke(captures.Count, -1);
            }

            // 캡처된 이미지들 합치기
            if (captures.Count == 1)
            {
                return captures[0];
            }

            return StitchImages(captures, captureHeight - 50);
        }
        finally
        {
            // 첫 번째를 제외하고 정리 (합쳐진 경우 첫 번째도 정리됨)
            if (captures.Count > 1)
            {
                foreach (var cap in captures)
                {
                    cap.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// 클라이언트 영역 캡처 (PrintWindow API 사용 - 하드웨어 가속 앱 지원)
    /// </summary>
    private Bitmap? CaptureClientArea(IntPtr hWnd)
    {
        // 창 전체 크기 가져오기
        if (!GetWindowRect(hWnd, out var windowRect))
            return null;

        var width = windowRect.Width;
        var height = windowRect.Height;

        if (width <= 0 || height <= 0)
            return null;

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            var hdcDest = g.GetHdc();

            try
            {
                // PrintWindow API 사용 (하드웨어 가속 앱도 캡처 가능)
                // PW_RENDERFULLCONTENT: DirectX/OpenGL 등 렌더링된 콘텐츠도 캡처
                bool success = PrintWindow(hWnd, hdcDest, PW_RENDERFULLCONTENT);

                if (!success)
                {
                    // 실패 시 기본 PrintWindow 시도
                    PrintWindow(hWnd, hdcDest, 0);
                }
            }
            finally
            {
                g.ReleaseHdc(hdcDest);
            }
        }

        // 클라이언트 영역만 추출
        if (GetClientRect(hWnd, out var clientRect))
        {
            // 클라이언트 영역의 오프셋 계산 (타이틀바, 테두리 제외)
            POINT clientPoint = new POINT { X = 0, Y = 0 };
            ClientToScreen(hWnd, ref clientPoint);

            int offsetX = clientPoint.X - windowRect.Left;
            int offsetY = clientPoint.Y - windowRect.Top;
            int clientWidth = clientRect.Width;
            int clientHeight = clientRect.Height;

            if (clientWidth > 0 && clientHeight > 0 &&
                offsetX >= 0 && offsetY >= 0 &&
                offsetX + clientWidth <= width &&
                offsetY + clientHeight <= height)
            {
                // 클라이언트 영역만 잘라내기
                var clientBitmap = new Bitmap(clientWidth, clientHeight, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(clientBitmap))
                {
                    g.DrawImage(bitmap,
                        new Rectangle(0, 0, clientWidth, clientHeight),
                        new Rectangle(offsetX, offsetY, clientWidth, clientHeight),
                        GraphicsUnit.Pixel);
                }
                bitmap.Dispose();
                return clientBitmap;
            }
        }

        return bitmap;
    }

    /// <summary>
    /// 맨 위로 스크롤 (Ctrl+Home 키 시뮬레이션)
    /// </summary>
    private void ScrollToTop(IntPtr hWnd)
    {
        // 창 활성화
        SetForegroundWindow(hWnd);
        Thread.Sleep(50);

        // Ctrl+Home 키 시뮬레이션 (문서 맨 위로)
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
        keybd_event(VK_HOME, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
        Thread.Sleep(50);
        keybd_event(VK_HOME, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        Thread.Sleep(100);

        // 추가로 마우스 휠 업도 시도 (창 중앙에 마우스 위치 후)
        if (GetWindowRect(hWnd, out var rect))
        {
            int centerX = (rect.Left + rect.Right) / 2;
            int centerY = (rect.Top + rect.Bottom) / 2;

            // 현재 마우스 위치 저장
            GetCursorPos(out var originalPos);

            // 창 중앙으로 마우스 이동
            SetCursorPos(centerX, centerY);
            Thread.Sleep(50);

            // 휠 업 (양수 = 위로 스크롤)
            for (int i = 0; i < 10; i++)
            {
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, 120 * 5, IntPtr.Zero);
                Thread.Sleep(30);
            }

            // 마우스 위치 복원
            SetCursorPos(originalPos.X, originalPos.Y);
        }
    }

    /// <summary>
    /// 아래로 스크롤 (실제 마우스 휠 이벤트 사용)
    /// </summary>
    private void ScrollDown(IntPtr hWnd, int pixels)
    {
        // 창 활성화
        SetForegroundWindow(hWnd);

        if (GetWindowRect(hWnd, out var rect))
        {
            int centerX = (rect.Left + rect.Right) / 2;
            int centerY = (rect.Top + rect.Bottom) / 2;

            // 현재 마우스 위치 저장
            GetCursorPos(out var originalPos);

            // 창 중앙으로 마우스 이동
            SetCursorPos(centerX, centerY);
            Thread.Sleep(30);

            // 휠 다운 (음수 = 아래로 스크롤)
            // 3번 휠 다운으로 대략 한 페이지 정도 스크롤
            for (int i = 0; i < 5; i++)
            {
                mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -120 * 2, IntPtr.Zero);
                Thread.Sleep(30);
            }

            // 마우스 위치 복원
            SetCursorPos(originalPos.X, originalPos.Y);
        }
    }

    /// <summary>
    /// 이미지 해시 계산 (변경 감지용)
    /// </summary>
    private long GetImageHash(Bitmap bitmap)
    {
        // 하단 100픽셀 영역의 간단한 해시
        long hash = 0;
        int sampleHeight = Math.Min(100, bitmap.Height);
        int startY = bitmap.Height - sampleHeight;

        // 안전한 방식으로 픽셀 샘플링
        for (int y = startY; y < bitmap.Height; y += 10)
        {
            for (int x = 0; x < bitmap.Width; x += 10)
            {
                var pixel = bitmap.GetPixel(x, y);
                hash += pixel.R + pixel.G + pixel.B;
            }
        }

        return hash;
    }

    /// <summary>
    /// 이미지들을 세로로 이어붙이기
    /// </summary>
    private Bitmap StitchImages(List<Bitmap> images, int overlap)
    {
        if (images.Count == 0) return new Bitmap(1, 1);
        if (images.Count == 1) return (Bitmap)images[0].Clone();

        int width = images[0].Width;

        // 전체 높이 계산 (겹치는 부분 고려)
        int totalHeight = images[0].Height;
        for (int i = 1; i < images.Count; i++)
        {
            // 겹치는 부분 찾기 (이미지 높이를 초과하지 않도록 제한)
            int actualOverlap = FindOverlap(images[i - 1], images[i], overlap);
            actualOverlap = Math.Min(actualOverlap, images[i].Height - 1);
            int addedHeight = images[i].Height - actualOverlap;
            if (addedHeight > 0)
            {
                totalHeight += addedHeight;
            }
        }

        // 최소 높이 보장
        totalHeight = Math.Max(totalHeight, 1);

        var result = new Bitmap(width, totalHeight, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(result))
        {
            g.Clear(Color.White);

            int currentY = 0;

            // 첫 번째 이미지
            g.DrawImage(images[0], 0, 0);
            currentY = images[0].Height;

            // 나머지 이미지들
            for (int i = 1; i < images.Count; i++)
            {
                int actualOverlap = FindOverlap(images[i - 1], images[i], overlap);
                actualOverlap = Math.Min(actualOverlap, images[i].Height - 1);
                currentY -= actualOverlap;
                currentY = Math.Max(currentY, 0); // 음수 방지
                g.DrawImage(images[i], 0, currentY);
                currentY += images[i].Height;
            }
        }

        return result;
    }

    /// <summary>
    /// 두 이미지 간의 겹치는 부분 찾기
    /// </summary>
    private int FindOverlap(Bitmap upper, Bitmap lower, int maxOverlap)
    {
        // 경계 검사: 이미지가 너무 작으면 기본값 반환
        if (upper.Height < 20 || lower.Height < 20)
        {
            return Math.Min(maxOverlap / 2, Math.Min(upper.Height, lower.Height) - 1);
        }

        int width = Math.Min(upper.Width, lower.Width);
        int searchHeight = Math.Min(maxOverlap, Math.Min(upper.Height, lower.Height) / 2);

        // 상단 이미지의 하단 부분과 하단 이미지의 상단 부분 비교
        for (int overlap = searchHeight; overlap > 10; overlap -= 5)
        {
            if (CompareRegions(upper, lower, overlap))
            {
                return overlap;
            }
        }

        return Math.Min(maxOverlap / 2, Math.Min(upper.Height, lower.Height) - 1); // 기본값 (경계 보호)
    }

    /// <summary>
    /// 두 영역이 일치하는지 비교
    /// </summary>
    private bool CompareRegions(Bitmap upper, Bitmap lower, int overlap)
    {
        // 경계 검사: overlap이 이미지 높이를 초과하면 false
        if (overlap <= 0 || overlap > upper.Height || overlap > lower.Height)
        {
            return false;
        }

        int width = Math.Min(upper.Width, lower.Width);
        int upperStartY = upper.Height - overlap;

        // upperStartY가 음수면 false
        if (upperStartY < 0)
        {
            return false;
        }

        int matchCount = 0;
        int totalSamples = 0;

        // 샘플링하여 비교
        for (int y = 0; y < overlap; y += 5)
        {
            int upperY = upperStartY + y;
            // 경계 검사
            if (upperY >= upper.Height || y >= lower.Height)
            {
                break;
            }

            for (int x = 0; x < width; x += 10)
            {
                // 경계 검사
                if (x >= upper.Width || x >= lower.Width)
                {
                    break;
                }

                var upperPixel = upper.GetPixel(x, upperY);
                var lowerPixel = lower.GetPixel(x, y);

                totalSamples++;

                // 색상 차이가 작으면 일치로 간주
                int diff = Math.Abs(upperPixel.R - lowerPixel.R) +
                           Math.Abs(upperPixel.G - lowerPixel.G) +
                           Math.Abs(upperPixel.B - lowerPixel.B);

                if (diff < 30) matchCount++;
            }
        }

        // 90% 이상 일치하면 true
        return totalSamples > 0 && (double)matchCount / totalSamples > 0.9;
    }
}
