using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Screenshot.Services;

/// <summary>
/// Windows OCR API를 사용한 텍스트 인식 서비스
/// </summary>
public class OcrService
{
    private OcrEngine? _ocrEngine;

    public OcrService()
    {
        // 한국어 OCR 엔진 초기화 시도
        _ocrEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ko"));

        // 한국어가 없으면 영어로 대체
        if (_ocrEngine == null)
        {
            _ocrEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));
        }

        // 영어도 없으면 시스템 기본 언어
        _ocrEngine ??= OcrEngine.TryCreateFromUserProfileLanguages();
    }

    /// <summary>
    /// OCR 사용 가능 여부
    /// </summary>
    public bool IsAvailable => _ocrEngine != null;

    /// <summary>
    /// 이미지에서 텍스트 추출
    /// </summary>
    public async Task<OcrResult> ExtractTextAsync(Bitmap image)
    {
        if (_ocrEngine == null)
        {
            return new OcrResult
            {
                Success = false,
                ErrorMessage = "OCR 엔진을 초기화할 수 없습니다. Windows 언어 팩을 설치해주세요."
            };
        }

        try
        {
            // Bitmap을 SoftwareBitmap으로 변환
            using var stream = new InMemoryRandomAccessStream();
            using var memoryStream = new MemoryStream();

            image.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;

            var bytes = memoryStream.ToArray();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            // OCR 실행
            var ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);

            var extractedText = ocrResult.Text;
            var lines = ocrResult.Lines.Select(l => new OcrLine
            {
                Text = l.Text,
                Words = l.Words.Select(w => new OcrWord
                {
                    Text = w.Text,
                    BoundingRect = new Rectangle(
                        (int)w.BoundingRect.X,
                        (int)w.BoundingRect.Y,
                        (int)w.BoundingRect.Width,
                        (int)w.BoundingRect.Height)
                }).ToList()
            }).ToList();

            return new OcrResult
            {
                Success = true,
                Text = extractedText,
                Lines = lines
            };
        }
        catch (Exception ex)
        {
            return new OcrResult
            {
                Success = false,
                ErrorMessage = $"OCR 처리 중 오류 발생: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 사용 가능한 OCR 언어 목록
    /// </summary>
    public static List<string> GetAvailableLanguages()
    {
        return OcrEngine.AvailableRecognizerLanguages
            .Select(l => $"{l.DisplayName} ({l.LanguageTag})")
            .ToList();
    }
}

/// <summary>
/// OCR 결과
/// </summary>
public class OcrResult
{
    public bool Success { get; set; }
    public string Text { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public List<OcrLine> Lines { get; set; } = new();
}

/// <summary>
/// OCR 인식된 줄
/// </summary>
public class OcrLine
{
    public string Text { get; set; } = "";
    public List<OcrWord> Words { get; set; } = new();
}

/// <summary>
/// OCR 인식된 단어
/// </summary>
public class OcrWord
{
    public string Text { get; set; } = "";
    public Rectangle BoundingRect { get; set; }
}
