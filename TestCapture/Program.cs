using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

const int DebugPort = 9222;
const string TargetUrl = "https://docs.google.com/spreadsheets/d/1-h7gQvW4lQE8shLCw1P-4UgayMsxNeyp09JisETlQGk/edit?gid=376124359#gid=376124359";

Console.WriteLine("=== Google Sheets 키보드 스크롤 캡처 테스트 ===\n");

// 1. Chrome 디버그 모드로 시작
Console.WriteLine("1. Chrome 디버그 모드 시작 중...");

var chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
var debugUserDataDir = Path.Combine(Path.GetTempPath(), "SmartCapture_ChromeDebug");
var arguments = $"--remote-debugging-port={DebugPort} --user-data-dir=\"{debugUserDataDir}\" --no-first-run --no-default-browser-check --window-size=1400,900 \"{TargetUrl}\"";

Process.Start(new ProcessStartInfo
{
    FileName = chromePath,
    Arguments = arguments,
    UseShellExecute = false
});

// 2. 디버그 포트 연결 대기
Console.WriteLine("2. 디버그 포트 연결 대기 중...");
using var http = new HttpClient();
string? wsUrl = null;

for (int i = 0; i < 20; i++)
{
    await Task.Delay(500);
    try
    {
        var json = await http.GetStringAsync($"http://localhost:{DebugPort}/json");
        var tabs = JsonSerializer.Deserialize<JsonElement[]>(json);
        foreach (var tab in tabs!)
        {
            if (tab.TryGetProperty("type", out var type) && type.GetString() == "page" &&
                tab.TryGetProperty("url", out var url) && url.GetString()!.Contains("docs.google.com") &&
                tab.TryGetProperty("webSocketDebuggerUrl", out var ws))
            {
                wsUrl = ws.GetString();
                break;
            }
        }
        if (!string.IsNullOrEmpty(wsUrl)) break;
    }
    catch { }
}

if (string.IsNullOrEmpty(wsUrl))
{
    Console.WriteLine("Chrome 연결 실패!");
    return;
}

Console.WriteLine($"   연결 성공!\n");

// 페이지 로딩 대기
Console.WriteLine("3. 페이지 로딩 대기 (7초)...");
await Task.Delay(7000);

// 3. WebSocket 연결
Console.WriteLine("4. WebSocket 연결...");
using var client = new ClientWebSocket();
await client.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

async Task<JsonElement?> SendCommand(string method, object parameters)
{
    var messageId = Random.Shared.Next(1, 100000);
    var message = new { id = messageId, method = method, @params = parameters };
    var messageJson = JsonSerializer.Serialize(message);
    var bytes = Encoding.UTF8.GetBytes(messageJson);

    await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

    var buffer = new byte[10 * 1024 * 1024];
    var result = new StringBuilder();

    while (true)
    {
        var response = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        result.Append(Encoding.UTF8.GetString(buffer, 0, response.Count));
        if (response.EndOfMessage) break;
    }

    var responseJson = JsonSerializer.Deserialize<JsonElement>(result.ToString());
    if (responseJson.TryGetProperty("result", out var res))
        return res;
    return null;
}

async Task<Bitmap?> CaptureViewport()
{
    var screenshot = await SendCommand("Page.captureScreenshot", new
    {
        format = "png",
        captureBeyondViewport = false,
        fromSurface = true
    });

    if (screenshot == null) return null;

    var base64 = screenshot.Value.GetProperty("data").GetString();
    var imageBytes = Convert.FromBase64String(base64!);
    using var ms = new MemoryStream(imageBytes);
    return new Bitmap(ms);
}

async Task SendKey(string key, int keyCode, bool ctrl = false)
{
    // Input.dispatchKeyEvent 사용
    var modifiers = ctrl ? 2 : 0; // ctrl = 2

    await SendCommand("Input.dispatchKeyEvent", new
    {
        type = "keyDown",
        key = key,
        code = key,
        windowsVirtualKeyCode = keyCode,
        nativeVirtualKeyCode = keyCode,
        modifiers = modifiers
    });

    await Task.Delay(50);

    await SendCommand("Input.dispatchKeyEvent", new
    {
        type = "keyUp",
        key = key,
        code = key,
        windowsVirtualKeyCode = keyCode,
        nativeVirtualKeyCode = keyCode,
        modifiers = modifiers
    });
}

// 4. 스프레드시트 클릭하여 포커스
Console.WriteLine("5. 스프레드시트 포커스...");
await SendCommand("Input.dispatchMouseEvent", new
{
    type = "mousePressed",
    x = 500,
    y = 400,
    button = "left",
    clickCount = 1
});
await SendCommand("Input.dispatchMouseEvent", new
{
    type = "mouseReleased",
    x = 500,
    y = 400,
    button = "left",
    clickCount = 1
});
await Task.Delay(500);

// 5. Ctrl+Home으로 맨 위로
Console.WriteLine("6. 맨 위로 이동 (Ctrl+Home)...");
await SendKey("Home", 36, ctrl: true);
await Task.Delay(500);

// 6. 스크롤하며 캡처
Console.WriteLine("7. 스크롤하며 캡처 시작...");

var captures = new List<Bitmap>();
int maxCaptures = 8;
long? lastHash = null;

for (int i = 0; i < maxCaptures; i++)
{
    Console.WriteLine($"   캡처 {i + 1}...");

    var bitmap = await CaptureViewport();
    if (bitmap != null)
    {
        // 이전 캡처와 비교하여 변화 없으면 중지
        long currentHash = GetImageHash(bitmap);
        if (lastHash != null && currentHash == lastHash)
        {
            Console.WriteLine("   이전 캡처와 동일 - 스크롤 끝");
            bitmap.Dispose();
            break;
        }
        lastHash = currentHash;
        captures.Add(bitmap);
    }

    // Page Down으로 스크롤
    Console.WriteLine($"   Page Down 전송...");
    await SendKey("PageDown", 34);
    await Task.Delay(500);
}

Console.WriteLine($"\n총 {captures.Count}장 캡처 완료");

// 7. 이미지 합성
if (captures.Count > 0)
{
    Console.WriteLine("8. 이미지 합성 중...");

    var outputPath = @"h:\Claude_work\Screenshot\google_sheets_keyboard.png";

    if (captures.Count == 1)
    {
        captures[0].Save(outputPath, ImageFormat.Png);
    }
    else
    {
        // 세로로 합성
        int overlap = 150; // 겹치는 픽셀 (헤더/메뉴 영역)
        int totalHeight = captures[0].Height + (captures.Count - 1) * (captures[0].Height - overlap);

        using var finalImage = new Bitmap(captures[0].Width, Math.Min(totalHeight, 16000));
        using var g = Graphics.FromImage(finalImage);

        int y = 0;
        for (int i = 0; i < captures.Count; i++)
        {
            int srcY = i == 0 ? 0 : overlap; // 첫 번째 이외에는 상단 overlap 제외
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

        finalImage.Save(outputPath, ImageFormat.Png);
    }

    Console.WriteLine($"저장 위치: {outputPath}");
    Console.WriteLine($"이미지 크기: {new FileInfo(outputPath).Length / 1024} KB");

    // 정리
    foreach (var cap in captures)
    {
        cap.Dispose();
    }
}

Console.WriteLine("\n=== 완료 ===");

// 이미지 해시 계산
static long GetImageHash(Bitmap bitmap)
{
    long hash = 0;
    int sampleStep = 50;

    for (int y = bitmap.Height / 2; y < bitmap.Height; y += sampleStep)
    {
        for (int x = 0; x < bitmap.Width; x += sampleStep)
        {
            var pixel = bitmap.GetPixel(x, y);
            hash += pixel.R + pixel.G + pixel.B;
        }
    }

    return hash;
}
