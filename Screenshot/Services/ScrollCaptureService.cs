using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Screenshot.Services.Capture;

namespace Screenshot.Services;

/// <summary>
/// 스크롤 캡처 서비스 - DXGI 캡처 + Page Down 키 기반 스크롤
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
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const byte VK_CONTROL = 0x11;
    private const byte VK_HOME = 0x24;
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

    #endregion

    private readonly CaptureManager _captureManager;

    public ScrollCaptureService(CaptureManager captureManager)
    {
        _captureManager = captureManager;
    }

    /// <summary>
    /// 스크롤 캡처 진행률 이벤트
    /// </summary>
    public event Action<int, int>? ProgressChanged;

    /// <summary>
    /// 활성 창의 스크롤 캡처
    /// </summary>
    public async Task<Bitmap?> CaptureScrollingWindowAsync(int delayMs = 400)
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return null;

        return await CaptureScrollingWindowAsync(hWnd, delayMs);
    }

    /// <summary>
    /// 지정된 창의 스크롤 캡처 (DXGI + Page Down 기반)
    /// </summary>
    public async Task<Bitmap?> CaptureScrollingWindowAsync(IntPtr hWnd, int delayMs = 400)
    {
        if (hWnd == IntPtr.Zero) return null;

        // 창을 포커스
        SetForegroundWindow(hWnd);
        await Task.Delay(200);

        // 캡처 목록
        var captures = new List<Bitmap>();

        try
        {
            // 맨 위로 스크롤 (Ctrl+Home)
            ScrollToTop(hWnd);
            await Task.Delay(delayMs);

            // 첫 번째 캡처 (DXGI로 전체 화면 → 창 클라이언트 영역 크롭)
            var firstCapture = await CaptureClientAreaAsync(hWnd);
            if (firstCapture == null) return null;
            captures.Add(firstCapture);

            var previousHash = GetImageHash(firstCapture);
            int maxScrollAttempts = 50;
            int sameHashCount = 0;

            ProgressChanged?.Invoke(1, -1);

            for (int i = 0; i < maxScrollAttempts; i++)
            {
                // Page Down 키로 한 페이지 스크롤
                ScrollDownOnePage(hWnd);
                await Task.Delay(delayMs);

                // DXGI 캡처
                var capture = await CaptureClientAreaAsync(hWnd);
                if (capture == null) break;

                // 스크롤 끝 감지 (이전 캡처와 동일하면 끝)
                var currentHash = GetImageHash(capture);
                if (currentHash == previousHash)
                {
                    sameHashCount++;
                    if (sameHashCount >= 2)
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

            // 이미지 합치기
            if (captures.Count == 1)
            {
                return captures[0];
            }

            // Page Down은 대부분 viewport 높이만큼 스크롤 → overlap 힌트는 높이의 15%
            int overlapHint = (int)(captures[0].Height * 0.15);
            return StitchImages(captures, overlapHint);
        }
        finally
        {
            if (captures.Count > 1)
            {
                foreach (var cap in captures)
                    cap.Dispose();
            }
        }
    }

    /// <summary>
    /// DXGI 전체 화면 캡처 → 창의 클라이언트 영역만 크롭
    /// </summary>
    private async Task<Bitmap?> CaptureClientAreaAsync(IntPtr hWnd)
    {
        // DXGI로 전체 화면 캡처
        var rawResult = await _captureManager.CaptureFullScreenRawAsync();
        if (!rawResult.Success || rawResult.Image == null) return null;

        using var fullScreen = rawResult.Image;

        // 창의 클라이언트 영역 좌표 계산
        if (!GetWindowRect(hWnd, out var windowRect)) return null;
        if (!GetClientRect(hWnd, out var clientRect)) return null;

        var clientPoint = new POINT { X = 0, Y = 0 };
        ClientToScreen(hWnd, ref clientPoint);

        int clientX = clientPoint.X;
        int clientY = clientPoint.Y;
        int clientWidth = clientRect.Width;
        int clientHeight = clientRect.Height;

        if (clientWidth <= 0 || clientHeight <= 0) return null;

        // 전체 화면 비트맵 범위 체크
        if (clientX < 0) { clientWidth += clientX; clientX = 0; }
        if (clientY < 0) { clientHeight += clientY; clientY = 0; }
        if (clientX + clientWidth > fullScreen.Width)
            clientWidth = fullScreen.Width - clientX;
        if (clientY + clientHeight > fullScreen.Height)
            clientHeight = fullScreen.Height - clientY;

        if (clientWidth <= 0 || clientHeight <= 0) return null;

        // 클라이언트 영역 크롭
        var cropRect = new Rectangle(clientX, clientY, clientWidth, clientHeight);
        return fullScreen.Clone(cropRect, fullScreen.PixelFormat);
    }

    /// <summary>
    /// 맨 위로 스크롤 (Ctrl+Home)
    /// </summary>
    private void ScrollToTop(IntPtr hWnd)
    {
        SetForegroundWindow(hWnd);
        Thread.Sleep(50);

        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
        keybd_event(VK_HOME, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
        Thread.Sleep(50);
        keybd_event(VK_HOME, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
    }

    /// <summary>
    /// Page Down 키로 한 페이지 스크롤
    /// </summary>
    private void ScrollDownOnePage(IntPtr hWnd)
    {
        SetForegroundWindow(hWnd);
        Thread.Sleep(30);

        keybd_event(VK_NEXT, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
        Thread.Sleep(30);
        keybd_event(VK_NEXT, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
    }

    /// <summary>
    /// 이미지 해시 계산 (위치 가중 해시, 충돌 방지)
    /// </summary>
    private long GetImageHash(Bitmap bitmap)
    {
        unchecked
        {
            long hash = 17;
            int sampleHeight = Math.Min(200, bitmap.Height);
            int startY = bitmap.Height - sampleHeight;

            for (int y = startY; y < bitmap.Height; y += 8)
            {
                for (int x = 0; x < bitmap.Width; x += 12)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    hash = hash * 31 + ((long)pixel.R << 16 | (long)pixel.G << 8 | pixel.B);
                    hash = hash * 31 + (x * 7919L + y * 104729L);
                }
            }

            return hash;
        }
    }

    /// <summary>
    /// 이미지들을 세로로 이어붙이기
    /// </summary>
    private Bitmap StitchImages(List<Bitmap> images, int overlapHint)
    {
        if (images.Count == 0) return new Bitmap(1, 1);
        if (images.Count == 1) return (Bitmap)images[0].Clone();

        int width = images[0].Width;

        // 전체 높이 계산
        int totalHeight = images[0].Height;
        for (int i = 1; i < images.Count; i++)
        {
            int actualOverlap = FindOverlap(images[i - 1], images[i], overlapHint);
            actualOverlap = Math.Min(actualOverlap, images[i].Height - 1);
            int addedHeight = images[i].Height - actualOverlap;
            if (addedHeight > 0)
                totalHeight += addedHeight;
        }

        totalHeight = Math.Max(totalHeight, 1);

        var result = new Bitmap(width, totalHeight, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(result))
        {
            g.Clear(Color.White);

            int currentY = 0;
            g.DrawImage(images[0], 0, 0);
            currentY = images[0].Height;

            for (int i = 1; i < images.Count; i++)
            {
                int actualOverlap = FindOverlap(images[i - 1], images[i], overlapHint);
                actualOverlap = Math.Min(actualOverlap, images[i].Height - 1);
                currentY -= actualOverlap;
                currentY = Math.Max(currentY, 0);
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
        if (upper.Height < 20 || lower.Height < 20)
            return Math.Min(maxOverlap / 2, Math.Min(upper.Height, lower.Height) - 1);

        int width = Math.Min(upper.Width, lower.Width);
        int searchHeight = Math.Min(maxOverlap, Math.Min(upper.Height, lower.Height) / 2);

        for (int overlap = searchHeight; overlap > 10; overlap -= 5)
        {
            if (CompareRegions(upper, lower, overlap))
                return overlap;
        }

        return Math.Min(maxOverlap / 2, Math.Min(upper.Height, lower.Height) - 1);
    }

    /// <summary>
    /// 두 영역이 일치하는지 비교
    /// </summary>
    private bool CompareRegions(Bitmap upper, Bitmap lower, int overlap)
    {
        if (overlap <= 0 || overlap > upper.Height || overlap > lower.Height)
            return false;

        int width = Math.Min(upper.Width, lower.Width);
        int upperStartY = upper.Height - overlap;
        if (upperStartY < 0) return false;

        int matchCount = 0;
        int totalSamples = 0;

        for (int y = 0; y < overlap; y += 5)
        {
            int upperY = upperStartY + y;
            if (upperY >= upper.Height || y >= lower.Height) break;

            for (int x = 0; x < width; x += 10)
            {
                if (x >= upper.Width || x >= lower.Width) break;

                var upperPixel = upper.GetPixel(x, upperY);
                var lowerPixel = lower.GetPixel(x, y);

                totalSamples++;

                int diff = Math.Abs(upperPixel.R - lowerPixel.R) +
                           Math.Abs(upperPixel.G - lowerPixel.G) +
                           Math.Abs(upperPixel.B - lowerPixel.B);

                if (diff < 30) matchCount++;
            }
        }

        return totalSamples > 0 && (double)matchCount / totalSamples > 0.9;
    }
}
