using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Screenshot.Models;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace Screenshot.Services.Capture;

public class WinRtCapture : ICaptureEngine, IDisposable
{
    public string Name => "WinRT Hardware";
    public int Priority => 10; // 높은 우선순위 (WinRT를 먼저 시도)
    
    public bool IsAvailable
    {
        get
        {
            try
            {
                // STA 스레드에서만 WinRT COM이 정상 작동
                if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                    return false;
                    
                return GraphicsCaptureSession.IsSupported();
            }
            catch
            {
                return false;
            }
        }
    }

    public CaptureResult CaptureFullScreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return new CaptureResult { Success = false, ErrorMessage = "활성 창이 없습니다" };
            
            // WinRT Interop 인터페이스 획득
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            if (interop == null)
                return new CaptureResult { Success = false, ErrorMessage = "GraphicsCaptureItem Interop을 생성할 수 없습니다" };
            
            var item = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
            if (item == null)
                return new CaptureResult { Success = false, ErrorMessage = "캡처 항목을 생성할 수 없습니다" };
                
            return CaptureItem(item);
        }
        catch (COMException comEx)
        {
            return new CaptureResult { Success = false, ErrorMessage = $"WinRT COM 오류: {comEx.Message} (코드: 0x{comEx.HResult:X8})" };
        }
        catch (UnauthorizedAccessException)
        {
            return new CaptureResult { Success = false, ErrorMessage = "캡처 권한이 거부되었습니다 (Windows 설정에서 화면 캡처 권한을 확인하세요)" };
        }
        catch (Exception ex)
        {
            return new CaptureResult { Success = false, ErrorMessage = $"WinRT 오류: {ex.Message}" };
        }
    }

    public CaptureResult CaptureMonitor(int monitorIndex) => CaptureFullScreen();
    public CaptureResult CaptureActiveWindow() => CaptureFullScreen();

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

    private CaptureResult CaptureItem(GraphicsCaptureItem item)
    {
        // STA 스레드 확인 (WinRT COM은 STA 필요)
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            return new CaptureResult { Success = false, ErrorMessage = "WinRT 캡처는 STA 스레드에서만 가능합니다" };
        }

        SharpDX.Direct3D11.Device? d3dDevice = null;
        IDirect3DDevice? device = null;
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? session = null;
        ManualResetEvent? frameReceived = null;

        try
        {
            d3dDevice = new SharpDX.Direct3D11.Device(
                SharpDX.Direct3D.DriverType.Hardware, 
                SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport);
            device = CreateDirect3DDeviceFromD3D11Device(d3dDevice);
            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device, 
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 
                1, 
                item.Size);
            session = framePool.CreateCaptureSession(item);
            
            // Windows 11에서만 지원되는 기능 (테두리 숨김)
            try 
            { 
                var property = session.GetType().GetProperty("IsBorderRequired");
                if (property != null)
                    property.SetValue(session, false);
            } 
            catch { }
            session.IsCursorCaptureEnabled = false;

            Bitmap? resultBitmap = null;
            frameReceived = new ManualResetEvent(false);
            var captureError = (Exception?)null;

            framePool.FrameArrived += (s, e) =>
            {
                try
                {
                    using var frame = framePool.TryGetNextFrame();
                    if (frame != null && resultBitmap == null)
                    {
                        resultBitmap = FrameToBitmap(frame);
                        frameReceived.Set();
                    }
                }
                catch (Exception ex) 
                { 
                    captureError = ex;
                    Debug.WriteLine($"FrameArrived 오류: {ex.Message}");
                    frameReceived.Set(); // 오류 발생 시에도 신호를 본냄
                }
            };

            session.StartCapture();
            
            // 3초 타임아웃 대기
            var waitResult = frameReceived.WaitOne(3000);
            
            if (captureError != null)
            {
                return new CaptureResult { Success = false, ErrorMessage = $"프레임 캡처 오류: {captureError.Message}" };
            }
            
            if (!waitResult)
            {
                return new CaptureResult { Success = false, ErrorMessage = "캡처 타임아웃 (3초 초과)" };
            }

            return resultBitmap != null 
                ? new CaptureResult { Success = true, Image = resultBitmap, EngineName = Name }
                : new CaptureResult { Success = false, ErrorMessage = "캡처 실패 (프레임 없음)" };
        }
        finally
        {
            frameReceived?.Dispose();
            session?.Dispose();
            framePool?.Dispose();
            device?.Dispose();
            d3dDevice?.Dispose();
        }
    }

    private Bitmap FrameToBitmap(Direct3D11CaptureFrame frame)
    {
        using var bitmap = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask().Result;
        if (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8)
        {
            return SoftwareBitmapToBitmap(bitmap);
        }
        else
        {
            using var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            return SoftwareBitmapToBitmap(converted);
        }
    }

    private unsafe Bitmap SoftwareBitmapToBitmap(SoftwareBitmap softwareBitmap)
    {
        var width = softwareBitmap.PixelWidth;
        var height = softwareBitmap.PixelHeight;
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var buffer = softwareBitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        
        byte* data;
        uint capacity;
        ((IMemoryBufferByteAccess)reference).GetBuffer(out data, out capacity);

        var bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var srcStride = buffer.GetPlaneDescription(0).Stride;
            for (int y = 0; y < height; y++)
                Buffer.MemoryCopy(data + y * srcStride, (byte*)bitmapData.Scan0 + y * bitmapData.Stride, bitmapData.Stride, Math.Min(bitmapData.Stride, srcStride));
        }
        finally { bitmap.UnlockBits(bitmapData); }
        return bitmap;
    }

    private static IDirect3DDevice CreateDirect3DDeviceFromD3D11Device(SharpDX.Direct3D11.Device d3dDevice)
    {
        var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var graphicsDevice);
        
        if (hr < 0) // HRESULT 오류 체크
        {
            throw new COMException($"CreateDirect3D11DeviceFromDXGIDevice 실패 (HRESULT: 0x{hr:X8})", hr);
        }
        
        if (graphicsDevice == IntPtr.Zero)
        {
            throw new InvalidOperationException("Direct3D 디바이스 생성 실패 (null 반환)");
        }
        
        return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice);
    }

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        GraphicsCaptureItem CreateForWindow([In] IntPtr window, [In] ref Guid riid);
    }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        unsafe void GetBuffer(out byte* buffer, out uint capacity);
    }

    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A200-4FCBDB5E5C8A");
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    public void Dispose() { }
}
