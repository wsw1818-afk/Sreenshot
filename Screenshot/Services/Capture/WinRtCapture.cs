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

/// <summary>
/// WinRT Graphics Capture API - Windows 10 1903+ 하드웨어 가속 캡처
/// </summary>
public class WinRtCapture : ICaptureEngine, IDisposable
{
    public string Name => "WinRT Hardware";
    public int Priority => 10;
    
    public bool IsAvailable
    {
        get
        {
            try
            {
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
        // 반드시 STA 스레드에서 실행
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            CaptureResult? result = null;
            var staThread = new Thread(() =>
            {
                try
                {
                    result = CaptureFullScreenInternal();
                }
                catch (Exception ex)
                {
                    CaptureLogger.LogWinRt("STA 스레드 캡처 실패", ex);
                    result = new CaptureResult { Success = false, ErrorMessage = ex.Message };
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join(10000); // 10초 타임아웃
            
            if (staThread.IsAlive)
            {
                staThread.Interrupt();
                return new CaptureResult { Success = false, ErrorMessage = "WinRT 캡처 타임아웃" };
            }
            
            return result ?? new CaptureResult { Success = false, ErrorMessage = "WinRT 캡처 결과 없음" };
        }
        
        return CaptureFullScreenInternal();
    }

    private CaptureResult CaptureFullScreenInternal()
    {
        var sw = Stopwatch.StartNew();
        CaptureLogger.LogWinRt("=== 전체 화면 캡처 시작 (STA) ===");
        
        try
        {
            // 모니터 기반 캡처 (Desktop Duplication보다 안정적)
            var monitors = GetAllMonitors();
            if (monitors.Count == 0)
            {
                return new CaptureResult { Success = false, ErrorMessage = "모니터를 찾을 수 없습니다" };
            }

            // 주 모니터 캡처
            var primaryMonitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            CaptureLogger.Verbose("WinRT", $"주 모니터 선택: {primaryMonitor.Bounds}");
            
            return CaptureMonitorInternal(primaryMonitor);
        }
        catch (COMException comEx)
        {
            CaptureLogger.LogWinRt($"COM 오류 (0x{comEx.HResult:X8})", comEx);
            return new CaptureResult { Success = false, ErrorMessage = $"WinRT COM 오류: {comEx.Message}" };
        }
        catch (Exception ex)
        {
            CaptureLogger.LogWinRt("캡처 오류", ex);
            return new CaptureResult { Success = false, ErrorMessage = $"WinRT 오류: {ex.Message}" };
        }
    }

    private CaptureResult CaptureMonitorInternal(MonitorInfo monitor)
    {
        var sw = Stopwatch.StartNew();
        
        SharpDX.Direct3D11.Device? d3dDevice = null;
        IDirect3DDevice? device = null;
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? session = null;
        ManualResetEvent? frameReceived = null;

        try
        {
            // 모니터 핸들로 GraphicsCaptureItem 생성
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            if (interop == null)
            {
                return new CaptureResult { Success = false, ErrorMessage = "GraphicsCaptureItem Interop 실패" };
            }

            // 활성 창 대신 모니터 캡처 시도 (더 안정적)
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return new CaptureResult { Success = false, ErrorMessage = "활성 창이 없습니다" };
            }

            var item = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
            if (item == null)
            {
                return new CaptureResult { Success = false, ErrorMessage = "캡처 항목 생성 실패" };
            }

            CaptureLogger.Verbose("WinRT", $"캡처 항목 생성됨: {item.Size}");

            // D3D11 디바이스 생성
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
            
            // Windows 11 테두리 숨김
            try 
            { 
                var property = session.GetType().GetProperty("IsBorderRequired");
                property?.SetValue(session, false);
            } 
            catch { }
            
            session.IsCursorCaptureEnabled = false;

            Bitmap? resultBitmap = null;
            frameReceived = new ManualResetEvent(false);
            var captureError = (Exception?)null;
            var frameCount = 0;

            framePool.FrameArrived += (s, e) =>
            {
                frameCount++;
                try
                {
                    using var frame = framePool.TryGetNextFrame();
                    if (frame != null && resultBitmap == null)
                    {
                        CaptureLogger.Verbose("WinRT", $"프레임 #{frameCount} 수신 - {frame.ContentSize}");
                        resultBitmap = FrameToBitmap(frame);
                        frameReceived.Set();
                    }
                }
                catch (Exception ex) 
                { 
                    captureError = ex;
                    frameReceived.Set();
                }
            };

            session.StartCapture();
            
            // 3초 타임아웃 대기
            var waitResult = frameReceived.WaitOne(3000);
            
            sw.Stop();
            
            if (captureError != null)
            {
                return new CaptureResult { Success = false, ErrorMessage = $"프레임 오류: {captureError.Message}" };
            }
            
            if (!waitResult)
            {
                return new CaptureResult { Success = false, ErrorMessage = "캡처 타임아웃" };
            }

            if (resultBitmap != null)
            {
                CaptureLogger.LogWinRt($"캡처 성공 - {resultBitmap.Width}x{resultBitmap.Height}, {sw.ElapsedMilliseconds}ms");
                return new CaptureResult { Success = true, Image = resultBitmap, EngineName = Name };
            }
            
            return new CaptureResult { Success = false, ErrorMessage = "프레임 없음" };
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
        
        if (hr < 0)
        {
            throw new COMException($"CreateDirect3D11DeviceFromDXGIDevice 실패 (HRESULT: 0x{hr:X8})", hr);
        }
        
        if (graphicsDevice == IntPtr.Zero)
        {
            throw new InvalidOperationException("Direct3D 디바이스 생성 실패");
        }
        
        return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevice);
    }

    private List<MonitorInfo> GetAllMonitors()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, hdcMonitor, lprcMonitor, dwData) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                monitors.Add(new MonitorInfo
                {
                    Bounds = new Rectangle(
                        mi.rcMonitor.left,
                        mi.rcMonitor.top,
                        mi.rcMonitor.right - mi.rcMonitor.left,
                        mi.rcMonitor.bottom - mi.rcMonitor.top),
                    IsPrimary = (mi.dwFlags & 1) == 1
                });
            }
            return true;
        }, IntPtr.Zero);
        return monitors;
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

    [DllImport("user32.dll")] 
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private class MonitorInfo
    {
        public Rectangle Bounds { get; set; }
        public bool IsPrimary { get; set; }
    }

    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A200-4FCBDB5E5C8A");

    public void Dispose() 
    { 
    }
}
