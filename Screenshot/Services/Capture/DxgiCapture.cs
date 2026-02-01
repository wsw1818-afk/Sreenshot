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
/// 하드웨어 가속, DLP 후킹 우회
/// </summary>
public class DxgiCapture : ICaptureEngine, IDisposable
{
    private SharpDX.Direct3D11.Device? _device;
    private OutputDuplication? _duplication;
    private Output1? _output;
    private Adapter1? _adapter;
    private Factory1? _factory;
    
    public string Name => "DXGI Hardware";
    public int Priority => 20; // 중간 우선순위
    
    public bool IsAvailable
    {
        get
        {
            try
            {
                TestInitialize();
                return true;
            }
            catch
            {
                return false;
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
        try
        {
            Initialize();
            if (_duplication == null)
                return new CaptureResult { Success = false, ErrorMessage = "DXGI 초기화 실패" };

            // 프레임 획득
            var result = _duplication.TryAcquireNextFrame(1000, out var frameInfo, out var desktopResource);
            
            if (!result.Success)
            {
                return new CaptureResult { Success = false, ErrorMessage = "프레임 획득 실패" };
            }

            // 프레임 획득 성공 - 반드시 ReleaseFrame 호출 필요
            try
            {
                using var texture = desktopResource.QueryInterface<Texture2D>();
                var bitmap = TextureToBitmap(texture);
                
                return new CaptureResult
                {
                    Success = true,
                    Image = bitmap,
                    EngineName = Name
                };
            }
            finally
            {
                // 성공/실패 관계없이 프레임은 반드시 해제
                _duplication.ReleaseFrame();
            }
        }
        catch (SharpDXException dxEx) when (dxEx.ResultCode == SharpDX.DXGI.ResultCode.AccessLost)
        {
            // DXGI 오류: 데스크톱 복제 세션이 끊어짐 (Ctrl+Alt+Del, 로그오프 등)
            // 디바이스를 재초기화 필요
            Dispose();
            return new CaptureResult { Success = false, ErrorMessage = "DXGI 세션 종료됨 (재시도 필요)" };
        }
        catch (SharpDXException dxEx) when (dxEx.ResultCode == SharpDX.DXGI.ResultCode.AccessDenied)
        {
            // UAC 화면 등 보안 화면
            return new CaptureResult { Success = false, ErrorMessage = "DXGI 접근 거부됨 (보안 화면)" };
        }
        catch (Exception ex)
        {
            return new CaptureResult { Success = false, ErrorMessage = $"DXGI 오류: {ex.Message}" };
        }
    }

    public CaptureResult CaptureMonitor(int monitorIndex)
    {
        return CaptureFullScreen();
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
        
        Factory1? factory = null;
        Adapter1? adapter = null;
        Output1? output1 = null;
        
        try
        {
            factory = new Factory1();
            adapter = factory.GetAdapter1(0);
            _device = new SharpDX.Direct3D11.Device(adapter);
            var output = adapter.GetOutput(0);
            output1 = output.QueryInterface<Output1>();
            _duplication = output1.DuplicateOutput(_device);
            
            // 성공적으로 초기화된 경우에만 참조 저장
            _factory = factory;
            _adapter = adapter;
            _output = output1;
            
            // 중간 객체들은 Dispose되지 않도록 null 처리
            factory = null;
            adapter = null;
            output1 = null;
        }
        catch
        {
            // 실패 시 정리
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

        // Staging texture 생성 (CPU 접근 가능)
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

        if (_device == null) throw new InvalidOperationException("DXGI 디바이스가 초기화되지 않았습니다");
        
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

    public void Dispose()
    {
        _duplication?.Dispose();
        _output?.Dispose();
        _device?.Dispose();
        _adapter?.Dispose();
        _factory?.Dispose();
    }
}
