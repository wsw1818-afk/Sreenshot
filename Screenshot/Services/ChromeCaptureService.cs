using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Net.WebSockets;

namespace Screenshot.Services;

/// <summary>
/// Chrome DevTools Protocol을 사용한 전체 페이지 캡처 서비스
/// </summary>
public class ChromeCaptureService
{
    private readonly HttpClient _httpClient = new();
    private const int DefaultDebugPort = 9222;
    private int _commandId;

    /// <summary>
    /// 캡처 진행 상태 이벤트
    /// </summary>
    public event Action<string>? StatusChanged;

    /// <summary>
    /// Chrome 전체 페이지 캡처 (CDP 사용)
    /// </summary>
    public async Task<Bitmap?> CaptureFullPageAsync(int debugPort = DefaultDebugPort)
    {
        try
        {
            StatusChanged?.Invoke("Chrome 연결 중...");
            var wsUrl = await GetWebSocketDebuggerUrlAsync(debugPort);

            if (string.IsNullOrEmpty(wsUrl))
            {
                StatusChanged?.Invoke("Chrome 디버그 모드 시작 중...");
                if (!await StartChromeWithDebugging(debugPort))
                    return null;

                await Task.Delay(2000);
                wsUrl = await GetWebSocketDebuggerUrlAsync(debugPort);

                if (string.IsNullOrEmpty(wsUrl))
                {
                    StatusChanged?.Invoke("Chrome 연결 실패");
                    return null;
                }
            }

            StatusChanged?.Invoke("페이지 캡처 중...");
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

            var screenshotResult = await SendCdpCommandAsync(ws, "Page.captureScreenshot", new
            {
                format = "png",
                captureBeyondViewport = true,
                fromSurface = true
            });

            if (screenshotResult == null || !screenshotResult.HasValue)
            {
                StatusChanged?.Invoke("캡처 실패");
                return null;
            }

            var base64Data = screenshotResult.Value.GetProperty("data").GetString();
            if (string.IsNullOrEmpty(base64Data))
                return null;

            var imageBytes = Convert.FromBase64String(base64Data);
            using var ms = new MemoryStream(imageBytes);
            var bitmap = new Bitmap(ms);

            StatusChanged?.Invoke("캡처 완료");
            return bitmap;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"오류: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 스크롤 가능한 전체 페이지 캡처 (레이아웃 메트릭스 사용)
    /// </summary>
    public async Task<Bitmap?> CaptureFullScrollablePageAsync(int debugPort = DefaultDebugPort)
    {
        try
        {
            StatusChanged?.Invoke("Chrome 연결 중...");
            var wsUrl = await GetWebSocketDebuggerUrlAsync(debugPort);

            if (string.IsNullOrEmpty(wsUrl))
            {
                if (!await StartChromeWithDebugging(debugPort))
                    return null;

                wsUrl = await GetWebSocketDebuggerUrlAsync(debugPort);
                if (string.IsNullOrEmpty(wsUrl))
                {
                    StatusChanged?.Invoke("Chrome 연결 실패");
                    return null;
                }
            }

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

            StatusChanged?.Invoke("페이지 크기 측정 중...");
            var layoutMetrics = await SendCdpCommandAsync(ws, "Page.getLayoutMetrics", new { });

            if (layoutMetrics == null || !layoutMetrics.HasValue)
                return null;

            var contentSize = layoutMetrics.Value.GetProperty("contentSize");
            var fullWidth = contentSize.GetProperty("width").GetDouble();
            var fullHeight = contentSize.GetProperty("height").GetDouble();

            // 매우 큰 페이지는 키보드 스크롤 캡처로 전환
            if (fullHeight > 8000)
            {
                StatusChanged?.Invoke("긴 페이지 감지 - 스크롤 캡처로 전환...");
                return await CaptureWithKeyboardScrollAsync(debugPort);
            }

            StatusChanged?.Invoke($"전체 페이지 캡처 중... ({fullWidth}x{fullHeight})");

            try
            {
                await SendCdpCommandAsync(ws, "Emulation.setDeviceMetricsOverride", new
                {
                    mobile = false,
                    width = (int)fullWidth,
                    height = (int)fullHeight,
                    deviceScaleFactor = 1
                });

                await Task.Delay(500);

                var screenshotResult = await SendCdpCommandAsync(ws, "Page.captureScreenshot", new
                {
                    format = "png",
                    captureBeyondViewport = true,
                    fromSurface = true,
                    clip = new
                    {
                        x = 0,
                        y = 0,
                        width = fullWidth,
                        height = fullHeight,
                        scale = 1
                    }
                });

                if (screenshotResult == null || !screenshotResult.HasValue)
                    return null;

                var base64Data = screenshotResult.Value.GetProperty("data").GetString();
                if (string.IsNullOrEmpty(base64Data))
                    return null;

                var imageBytes = Convert.FromBase64String(base64Data);
                using var ms = new MemoryStream(imageBytes);
                var bitmap = new Bitmap(ms);

                StatusChanged?.Invoke("캡처 완료");
                return bitmap;
            }
            finally
            {
                // 예외 발생 시에도 뷰포트 복원 보장
                try
                {
                    await SendCdpCommandAsync(ws, "Emulation.clearDeviceMetricsOverride", new { });
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"오류: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Chrome 디버그 WebSocket URL 가져오기
    /// </summary>
    private async Task<string?> GetWebSocketDebuggerUrlAsync(int port, string? targetUrl = null)
    {
        try
        {
            var response = await _httpClient.GetStringAsync($"http://localhost:{port}/json");
            var tabs = JsonSerializer.Deserialize<JsonElement[]>(response);

            if (tabs != null && tabs.Length > 0)
            {
                if (!string.IsNullOrEmpty(targetUrl))
                {
                    var targetUrlBase = targetUrl.Split('?')[0].Split('#')[0];
                    foreach (var tab in tabs)
                    {
                        if (tab.TryGetProperty("type", out var type) &&
                            type.GetString() == "page" &&
                            tab.TryGetProperty("url", out var tabUrl))
                        {
                            var tabUrlString = tabUrl.GetString();
                            if (!string.IsNullOrEmpty(tabUrlString) &&
                                tabUrlString.Contains(targetUrlBase) &&
                                tab.TryGetProperty("webSocketDebuggerUrl", out var wsUrl))
                            {
                                return wsUrl.GetString();
                            }
                        }
                    }
                }

                foreach (var tab in tabs)
                {
                    if (tab.TryGetProperty("type", out var type) &&
                        type.GetString() == "page" &&
                        tab.TryGetProperty("webSocketDebuggerUrl", out var wsUrl))
                    {
                        return wsUrl.GetString();
                    }
                }
            }
        }
        catch
        {
            // 연결 실패
        }

        return null;
    }

    /// <summary>
    /// Chrome을 디버그 모드로 시작 (별도 프로필 사용)
    /// </summary>
    private async Task<bool> StartChromeWithDebugging(int port, string? url = null)
    {
        try
        {
            var chromePaths = new[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Google\Chrome\Application\chrome.exe")
            };

            string? chromePath = null;
            foreach (var path in chromePaths)
            {
                if (File.Exists(path))
                {
                    chromePath = path;
                    break;
                }
            }

            if (chromePath == null)
            {
                StatusChanged?.Invoke("Chrome을 찾을 수 없습니다.");
                return false;
            }

            var debugUserDataDir = Path.Combine(Path.GetTempPath(), "SmartCapture_ChromeDebug");

            var arguments = $"--remote-debugging-port={port} --user-data-dir=\"{debugUserDataDir}\" --no-first-run --no-default-browser-check";
            if (!string.IsNullOrEmpty(url))
                arguments += $" \"{url}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = arguments,
                UseShellExecute = false
            };

            StatusChanged?.Invoke("디버그 모드 Chrome 시작 중...");
            Process.Start(startInfo);

            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                var wsUrl = await GetWebSocketDebuggerUrlAsync(port);
                if (!string.IsNullOrEmpty(wsUrl))
                {
                    StatusChanged?.Invoke("Chrome 디버그 모드 연결됨");
                    return true;
                }
            }

            StatusChanged?.Invoke("Chrome 디버그 모드 연결 실패");
            return false;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Chrome 시작 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// CDP 명령 전송 (id 매칭 + 타임아웃)
    /// </summary>
    private async Task<JsonElement?> SendCdpCommandAsync(ClientWebSocket ws, string method, object parameters)
    {
        try
        {
            var messageId = Interlocked.Increment(ref _commandId);
            var message = new
            {
                id = messageId,
                method = method,
                @params = parameters
            };

            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

            // 15초 타임아웃으로 응답 대기
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            while (!cts.Token.IsCancellationRequested)
            {
                var fullMessage = await ReceiveFullMessageAsync(ws, cts.Token);
                var responseJson = JsonSerializer.Deserialize<JsonElement>(fullMessage);

                // id가 일치하는 응답만 처리 (이벤트 메시지는 건너뛰기)
                if (responseJson.TryGetProperty("id", out var idProp) && idProp.GetInt32() == messageId)
                {
                    if (responseJson.TryGetProperty("result", out var resultElement))
                        return resultElement;
                    return null; // 에러 응답
                }
                // id 불일치 → 이벤트 메시지, 다음 메시지 대기
            }
        }
        catch (OperationCanceledException)
        {
            // 타임아웃
        }
        catch
        {
            // 명령 실패
        }

        return null;
    }

    /// <summary>
    /// WebSocket에서 완전한 메시지 하나를 수신
    /// </summary>
    private async Task<string> ReceiveFullMessageAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024]; // 64KB 버퍼
        var result = new StringBuilder();

        while (true)
        {
            var response = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            result.Append(Encoding.UTF8.GetString(buffer, 0, response.Count));
            if (response.EndOfMessage) break;
        }

        return result.ToString();
    }

    /// <summary>
    /// Chrome이 디버그 모드로 실행 중인지 확인
    /// </summary>
    public async Task<bool> IsChromeDebugAvailableAsync(int port = DefaultDebugPort)
    {
        var wsUrl = await GetWebSocketDebuggerUrlAsync(port);
        return !string.IsNullOrEmpty(wsUrl);
    }

    /// <summary>
    /// URL을 지정하여 Chrome 전체 페이지 캡처 (기존 Chrome 재사용)
    /// </summary>
    public async Task<Bitmap?> CaptureUrlAsync(string url, int debugPort = DefaultDebugPort)
    {
        try
        {
            StatusChanged?.Invoke("Chrome 연결 확인 중...");

            // 1. 기존 디버그 Chrome이 있는지 확인
            var wsUrl = await GetWebSocketDebuggerUrlAsync(debugPort);

            if (!string.IsNullOrEmpty(wsUrl))
            {
                // 기존 Chrome에서 URL 네비게이션
                StatusChanged?.Invoke("기존 Chrome에서 페이지 로딩 중...");
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
                await SendCdpCommandAsync(ws, "Page.navigate", new { url });
                await Task.Delay(3000); // 로딩 대기
            }
            else
            {
                // 새 Chrome 시작
                StatusChanged?.Invoke("Chrome 시작 중...");
                if (!await StartChromeWithDebugging(debugPort, url))
                    return null;

                await Task.Delay(3000);
            }

            // Google Sheets/Docs는 키보드 스크롤 캡처
            if (url.Contains("docs.google.com/spreadsheets") ||
                url.Contains("docs.google.com/document"))
            {
                return await CaptureWithKeyboardScrollAsync(debugPort, url: url);
            }

            // 일반 페이지는 전체 페이지 캡처
            return await CaptureFullScrollablePageAsync(debugPort);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"오류: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 키보드 스크롤을 사용한 전체 페이지 캡처 (Google Sheets 등 동적 콘텐츠용)
    /// </summary>
    public async Task<Bitmap?> CaptureWithKeyboardScrollAsync(int debugPort = DefaultDebugPort, int maxCaptures = 20, string? url = null)
    {
        try
        {
            StatusChanged?.Invoke("Chrome 연결 중...");
            var wsUrl = await GetWebSocketDebuggerUrlAsync(debugPort, url);

            if (string.IsNullOrEmpty(wsUrl))
            {
                StatusChanged?.Invoke("Chrome 연결 실패");
                return null;
            }

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

            // 페이지 클릭하여 포커스
            StatusChanged?.Invoke("페이지 포커스 설정 중...");
            await SendMouseClickAsync(ws, 500, 400);
            await Task.Delay(500);

            // Ctrl+Home으로 맨 위로 이동
            StatusChanged?.Invoke("페이지 상단으로 이동 중...");
            await SendKeyAsync(ws, "Home", 36, ctrl: true);
            await Task.Delay(500);

            // 스크롤하며 캡처
            var captures = new List<Bitmap>();
            long? lastHash = null;

            for (int i = 0; i < maxCaptures; i++)
            {
                StatusChanged?.Invoke($"캡처 중... ({i + 1}/{maxCaptures})");

                var bitmap = await CaptureViewportAsync(ws);
                if (bitmap != null)
                {
                    long currentHash = GetImageHash(bitmap);
                    if (lastHash != null && currentHash == lastHash)
                    {
                        StatusChanged?.Invoke("스크롤 끝 감지");
                        bitmap.Dispose();
                        break;
                    }
                    lastHash = currentHash;
                    captures.Add(bitmap);
                }

                // Page Down으로 스크롤
                await SendKeyAsync(ws, "PageDown", 34);
                await Task.Delay(400);
            }

            if (captures.Count == 0)
            {
                StatusChanged?.Invoke("캡처 실패");
                return null;
            }

            StatusChanged?.Invoke($"이미지 합성 중... ({captures.Count}장)");

            Bitmap? result = null;
            try
            {
                if (captures.Count == 1)
                {
                    result = captures[0];
                }
                else
                {
                    result = StitchImages(captures);
                    foreach (var cap in captures)
                        cap.Dispose();
                }
            }
            catch
            {
                foreach (var cap in captures)
                    cap.Dispose();
                throw;
            }

            StatusChanged?.Invoke("캡처 완료");
            return result;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"오류: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 뷰포트 스크린샷 캡처
    /// </summary>
    private async Task<Bitmap?> CaptureViewportAsync(ClientWebSocket ws)
    {
        var screenshot = await SendCdpCommandAsync(ws, "Page.captureScreenshot", new
        {
            format = "png",
            captureBeyondViewport = false,
            fromSurface = true
        });

        if (screenshot == null || !screenshot.HasValue)
            return null;

        var base64 = screenshot.Value.GetProperty("data").GetString();
        if (string.IsNullOrEmpty(base64))
            return null;

        var imageBytes = Convert.FromBase64String(base64);
        using var ms = new MemoryStream(imageBytes);
        return new Bitmap(ms);
    }

    /// <summary>
    /// 키 입력 전송
    /// </summary>
    private async Task SendKeyAsync(ClientWebSocket ws, string key, int keyCode, bool ctrl = false)
    {
        var modifiers = ctrl ? 2 : 0;

        await SendCdpCommandAsync(ws, "Input.dispatchKeyEvent", new
        {
            type = "keyDown",
            key = key,
            code = key,
            windowsVirtualKeyCode = keyCode,
            nativeVirtualKeyCode = keyCode,
            modifiers = modifiers
        });

        await Task.Delay(50);

        await SendCdpCommandAsync(ws, "Input.dispatchKeyEvent", new
        {
            type = "keyUp",
            key = key,
            code = key,
            windowsVirtualKeyCode = keyCode,
            nativeVirtualKeyCode = keyCode,
            modifiers = modifiers
        });
    }

    /// <summary>
    /// 마우스 클릭 전송
    /// </summary>
    private async Task SendMouseClickAsync(ClientWebSocket ws, int x, int y)
    {
        await SendCdpCommandAsync(ws, "Input.dispatchMouseEvent", new
        {
            type = "mousePressed",
            x = x,
            y = y,
            button = "left",
            clickCount = 1
        });

        await SendCdpCommandAsync(ws, "Input.dispatchMouseEvent", new
        {
            type = "mouseReleased",
            x = x,
            y = y,
            button = "left",
            clickCount = 1
        });
    }

    /// <summary>
    /// 이미지 해시 계산 (위치 가중, 스크롤 끝 감지용)
    /// </summary>
    private static long GetImageHash(Bitmap bitmap)
    {
        unchecked
        {
            long hash = 17;
            int sampleStep = 50;

            for (int y = bitmap.Height / 2; y < bitmap.Height; y += sampleStep)
            {
                for (int x = 0; x < bitmap.Width; x += sampleStep)
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
    /// 여러 이미지를 세로로 합성
    /// </summary>
    private static Bitmap StitchImages(List<Bitmap> captures)
    {
        int overlap = 150;
        int totalHeight = captures[0].Height + (captures.Count - 1) * (captures[0].Height - overlap);

        var finalImage = new Bitmap(captures[0].Width, Math.Min(totalHeight, 16000));
        using var g = Graphics.FromImage(finalImage);

        int y = 0;
        for (int i = 0; i < captures.Count; i++)
        {
            int srcY = i == 0 ? 0 : overlap;
            int srcHeight = i == 0 ? captures[i].Height : captures[i].Height - overlap;

            if (y + srcHeight > finalImage.Height)
                srcHeight = finalImage.Height - y;

            if (srcHeight > 0)
            {
                g.DrawImage(captures[i],
                    new Rectangle(0, y, captures[i].Width, srcHeight),
                    new Rectangle(0, srcY, captures[i].Width, srcHeight),
                    GraphicsUnit.Pixel);
                y += srcHeight;
            }
        }

        return finalImage;
    }
}
