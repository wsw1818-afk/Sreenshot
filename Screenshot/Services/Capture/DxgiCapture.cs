using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using Screenshot.Models;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace Screenshot.Services.Capture;

/// <summary>
/// DXGI Desktop Duplication API 캡처 엔진
/// </summary>
public class DxgiCapture : ICaptureEngine, IDisposable
{
    private SharpDX.Direct3D11.Device? _device;
    private OutputDuplication? _duplication;
    private Output1? _output;
    private Adapter1? _adapter;
    private Factory1? _factory;

    // IsAvailable 캐싱 - 세션 끊김 방지
    private bool? _cachedAvailability;
    private DateTime _lastAvailabilityCheck;
    private static readonly TimeSpan AvailabilityCacheTime = TimeSpan.FromSeconds(30);
    private static readonly object AvailabilityLock = new();
    
    public string Name => "DXGI Hardware";
    public int Priority => 1; // 최우선
    
    public bool IsAvailable
    {
        get
        {
            lock (AvailabilityLock)
            {
                // 캐시된 결과가 유효하면 사용
                if (_cachedAvailability.HasValue && 
                    DateTime.Now - _lastAvailabilityCheck < AvailabilityCacheTime)
                {
                    CaptureLogger.Verbose("DXGI", $"IsAvailable 캐시 사용: {_cachedAvailability.Value}");
                    return _cachedAvailability.Value;
                }

                try
                {
                    // 초기화되어 있고 세션이 유효하면 재사용
                    if (_device != null && _duplication != null)
                    {
                        try
                        {
                            // 세션 유효성 테스트 - Description 조회로 가볍게 체크
                            _ = _duplication.Description;
                            _cachedAvailability = true;
                            _lastAvailabilityCheck = DateTime.Now;
                            CaptureLogger.Verbose("DXGI", "IsAvailable: 기존 세션 유효");
                            return true;
                        }
                        catch (SharpDXException ex) when (ex.ResultCode == SharpDX.DXGI.ResultCode.AccessLost)
                        {
                            CaptureLogger.Warn("DXGI", "세션 만료됨, 재초기화 필요");
                            Dispose();
                        }
                    }
                    
                    TestInitialize();
                    _cachedAvailability = true;
                    CaptureLogger.Verbose("DXGI", "IsAvailable: TestInitialize 성공");
                }
                catch (Exception ex)
                {
                    CaptureLogger.LogDxgi("IsAvailable 체크 실패", ex);
                    _cachedAvailability = false;
                }
                
                _lastAvailabilityCheck = DateTime.Now;
                return _cachedAvailability.Value;
            }
        }
    }

    private void TestInitialize()
    {
        using var factory = new Factory1();
        using var adapter = factory.GetAdapter1(0);
        using var device = new SharpDX.Direct3D11.Device(adapter);
        using var output = adapter.GetOutput(0);
        using var output1 = output.QueryInterface<Output1>();
        using var duplication = output1.DuplicateOutput(device);
    }

    public CaptureResult CaptureFullScreen()
    {
        // 최대 3회 재시도
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var result = TryCaptureFullScreen(attempt);
            if (result.Success && result.Image != null)
            {
                // 검은 화면 체크
                if (!IsBlackImage(result.Image))
                {
                    // 캐시 업데이트
                    lock (AvailabilityLock)
                    {
                        _cachedAvailability = true;
                        _lastAvailabilityCheck = DateTime.Now;
                    }
                    return result;
                }
                
                CaptureLogger.Warn("DXGI", $"시도 {attempt}: 검은 화면, 재시도 중...");
                result.Image.Dispose();
                
                // 리소스 완전 정리 후 재초기화
                FullDispose();
                Thread.Sleep(300 * attempt); // 점진적 대기
            }
            else if (result.Success)
            {
                return result;
            }
            else
            {
                // 오류 시 재시도
                CaptureLogger.Warn("DXGI", $"시도 {attempt} 실패: {result.ErrorMessage}");
                
                if (attempt < 3)
                {
                    FullDispose();
                    Thread.Sleep(300 * attempt);
                }
                else
                {
                    // 최종 실패 - 캐시 무효화
                    lock (AvailabilityLock)
                    {
                        _cachedAvailability = false;
                        _lastAvailabilityCheck = DateTime.Now;
                    }
                    return result;
                }
            }
        }

        return new CaptureResult { Success = false, ErrorMessage = "DXGI 3회 재시도 후 실패" };
    }

    private void FullDispose()
    {
        Dispose();
        // GC 강제 호출로 COM 객체 완전 정리
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private CaptureResult TryCaptureFullScreen(int attempt)
    {
        var sw = Stopwatch.StartNew();
        CaptureLogger.LogDxgi($"캡처 시도 #{attempt}");
        
        try
        {
            Initialize();
            if (_duplication == null)
                return new CaptureResult { Success = false, ErrorMessage = "DXGI 초기화 실패" };

            // 이전 프레임 해제 (중요!)
            try { _duplication.ReleaseFrame(); } catch { }

            // 새 프레임 획득
            Thread.Sleep(100); // 프레임 준비 시간
            
            var result = _duplication.TryAcquireNextFrame(2000, out var frameInfo, out var desktopResource);
            
            CaptureLogger.Verbose("DXGI", $"TryAcquireNextFrame: {result.Code:X8}, AccumulatedFrames={frameInfo.AccumulatedFrames}");
            
            if (!result.Success)
            {
                return new CaptureResult { Success = false, ErrorMessage = $"프레임 획득 실패: 0x{result.Code:X8}" };
            }

            try
            {
                using var texture = desktopResource.QueryInterface<Texture2D>();
                var bitmap = TextureToBitmap(texture);
                
                sw.Stop();
                CaptureLogger.LogDxgi($"캡처 완료 - {bitmap.Width}x{bitmap.Height}, {sw.ElapsedMilliseconds}ms");
                
                return new CaptureResult
                {
                    Success = true,
                    Image = bitmap,
                    EngineName = Name
                };
            }
            finally
            {
                _duplication.ReleaseFrame();
            }
        }
        catch (SharpDXException dxEx) when (dxEx.ResultCode == SharpDX.DXGI.ResultCode.AccessLost)
        {
            CaptureLogger.LogDxgi("DXGI 세션 종료됨 (AccessLost)", dxEx);
            Dispose();
            return new CaptureResult { Success = false, ErrorMessage = "DXGI 세션 종료됨" };
        }
        catch (SharpDXException dxEx) when (dxEx.ResultCode == SharpDX.DXGI.ResultCode.AccessDenied)
        {
            CaptureLogger.LogDxgi("DXGI 접근 거부됨", dxEx);
            return new CaptureResult { Success = false, ErrorMessage = "DXGI 접근 거부됨 (보안 화면)" };
        }
        catch (Exception ex)
        {
            CaptureLogger.LogDxgi("DXGI 예외", ex);
            Dispose();
            return new CaptureResult { Success = false, ErrorMessage = $"DXGI 오류: {ex.Message}" };
        }
    }

    public CaptureResult CaptureMonitor(int monitorIndex)
    {
        var monitors = DpiHelper.GetAllMonitors();
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
        {
            return new CaptureResult
            {
                Success = false,
                EngineName = Name,
                ErrorMessage = $"잘못된 모니터 인덱스: {monitorIndex}"
            };
        }

        return CaptureRegion(monitors[monitorIndex].Bounds);
    }

    public CaptureResult CaptureActiveWindow()
    {
        return CaptureFullScreen();
    }

    public CaptureResult CaptureRegion(Rectangle region)
    {
        var result = CaptureFullScreen();
        if (!result.Success || result.Image == null) return result;

        try
        {
            using var original = result.Image;
            var cropped = new Bitmap(region.Width, region.Height);
            using (var g = Graphics.FromImage(cropped))
                g.DrawImage(original, new Rectangle(0, 0, region.Width, region.Height), region, GraphicsUnit.Pixel);
            
            return new CaptureResult { Success = true, Image = cropped, EngineName = Name };
        }
        catch (Exception ex)
        {
            return new CaptureResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private void Initialize()
    {
        if (_device != null) return;
        
        CaptureLogger.DebugLog("DXGI", "초기화 시작");
        
        Factory1? factory = null;
        Adapter1? adapter = null;
        Output1? output1 = null;
        
        try
        {
            factory = new Factory1();
            adapter = factory.GetAdapter1(0);
            
            CaptureLogger.Verbose("DXGI", $"어댑터: {adapter.Description.Description}");
            
            _device = new SharpDX.Direct3D11.Device(adapter);
            var output = adapter.GetOutput(0);
            output1 = output.QueryInterface<Output1>();
            
            _duplication = output1.DuplicateOutput(_device);
            
            CaptureLogger.DebugLog("DXGI", "Desktop Duplication 초기화 성공");
            
            _factory = factory;
            _adapter = adapter;
            _output = output1;
            
            factory = null;
            adapter = null;
            output1 = null;
        }
        catch
        {
            output1?.Dispose();
            adapter?.Dispose();
            factory?.Dispose();
            throw;
        }
    }

    private Bitmap TextureToBitmap(Texture2D texture)
    {
        var desc = texture.Description;
        var width = desc.Width;
        var height = desc.Height;

        var stagingDesc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desc.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        };

        if (_device == null) throw new InvalidOperationException("DXGI 디바이스 초기화되지 않음");
        
        using var stagingTexture = new Texture2D(_device, stagingDesc);
        _device.ImmediateContext.CopyResource(texture, stagingTexture);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var dataBox = _device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
        
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            unsafe
            {
                for (int y = 0; y < height; y++)
                {
                    var srcPtr = (byte*)dataBox.DataPointer + y * dataBox.RowPitch;
                    var destPtr = (byte*)bitmapData.Scan0 + y * bitmapData.Stride;
                    Utilities.CopyMemory((IntPtr)destPtr, (IntPtr)srcPtr, Math.Min(dataBox.RowPitch, bitmapData.Stride));
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
            _device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
        }

        return bitmap;
    }

    private static bool IsBlackImage(Bitmap bitmap) => CaptureEngineBase.IsBlackImage(bitmap);

    public void Dispose()
    {
        _duplication?.Dispose();
        _output?.Dispose();
        _device?.Dispose();
        _adapter?.Dispose();
        _factory?.Dispose();
        _duplication = null;
        _output = null;
        _device = null;
        _adapter = null;
        _factory = null;
    }
}
