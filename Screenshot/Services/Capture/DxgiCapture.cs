using System.Drawing;
using System.Drawing.Imaging;
using Screenshot.Models;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Resource = SharpDX.DXGI.Resource;

namespace Screenshot.Services.Capture;

/// <summary>
/// DXGI Desktop Duplication을 사용한 캡처 엔진
/// DirectX 11 기반으로 GPU 가속, 일부 보안 프로그램 우회 가능
/// </summary>
public class DxgiCapture : CaptureEngineBase
{
    public override string Name => "DXGI Desktop Duplication";
    public override int Priority => 2; // Magnification 다음

    private Device? _device;
    private OutputDuplication? _duplicatedOutput;
    private Texture2D? _stagingTexture;
    private int _outputIndex;

    public override bool IsAvailable
    {
        get
        {
            try
            {
                // Windows 8 이상에서만 지원
                var os = Environment.OSVersion;
                if (os.Platform != PlatformID.Win32NT || os.Version.Major < 6 || (os.Version.Major == 6 && os.Version.Minor < 2))
                {
                    return false;
                }

                return TryInitialize(0);
            }
            catch
            {
                return false;
            }
            finally
            {
                Cleanup();
            }
        }
    }

    private bool TryInitialize(int outputIndex)
    {
        try
        {
            Cleanup();

            // Direct3D 11 디바이스 생성
            _device = new Device(SharpDX.Direct3D.DriverType.Hardware,
                DeviceCreationFlags.BgraSupport);

            // DXGI 어댑터 및 출력 가져오기
            using var dxgiDevice = _device.QueryInterface<SharpDX.DXGI.Device>();
            using var adapter = dxgiDevice.Adapter;
            using var output = adapter.GetOutput(outputIndex);
            using var output1 = output.QueryInterface<Output1>();

            // Desktop Duplication 생성
            _duplicatedOutput = output1.DuplicateOutput(_device);
            _outputIndex = outputIndex;

            return true;
        }
        catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.NotCurrentlyAvailable.Result.Code)
        {
            // 다른 앱이 이미 듀플리케이션 사용 중
            return false;
        }
        catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.Unsupported.Result.Code)
        {
            // 지원되지 않는 환경
            return false;
        }
        catch
        {
            return false;
        }
    }

    public override CaptureResult CaptureFullScreen()
    {
        // 모든 모니터 캡처 (가상 화면)
        var bounds = DpiHelper.GetVirtualScreenBounds();

        // DXGI는 모니터별로 캡처하므로, 모든 모니터를 합성
        var monitors = DpiHelper.GetAllMonitors();
        if (monitors.Count == 1)
        {
            return CaptureMonitor(0);
        }

        // 여러 모니터 합성
        var fullBitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(fullBitmap);
        g.Clear(Color.Black);

        bool anySuccess = false;
        foreach (var monitor in monitors)
        {
            var result = CaptureMonitor(monitor.Index);
            if (result.Success && result.Image != null)
            {
                // 모니터 위치에 맞게 그리기
                var offsetX = monitor.Bounds.X - bounds.X;
                var offsetY = monitor.Bounds.Y - bounds.Y;
                g.DrawImage(result.Image, offsetX, offsetY);
                result.Image.Dispose();
                anySuccess = true;
            }
        }

        if (!anySuccess)
        {
            fullBitmap.Dispose();
            return new CaptureResult
            {
                Success = false,
                EngineName = Name,
                ErrorMessage = "모든 모니터 캡처 실패"
            };
        }

        return new CaptureResult
        {
            Success = true,
            Image = fullBitmap,
            CaptureArea = bounds,
            EngineName = Name,
            CapturedAt = DateTime.Now
        };
    }

    public override CaptureResult CaptureMonitor(int monitorIndex)
    {
        try
        {
            if (!TryInitialize(monitorIndex))
            {
                return new CaptureResult
                {
                    Success = false,
                    EngineName = Name,
                    ErrorMessage = $"DXGI 초기화 실패 (모니터 {monitorIndex})"
                };
            }

            var frame = CaptureFrame();
            if (frame == null)
            {
                return new CaptureResult
                {
                    Success = false,
                    EngineName = Name,
                    ErrorMessage = "프레임 캡처 실패"
                };
            }

            var monitors = DpiHelper.GetAllMonitors();
            var monitor = monitors.FirstOrDefault(m => m.Index == monitorIndex);

            return new CaptureResult
            {
                Success = true,
                Image = frame,
                CaptureArea = monitor?.Bounds ?? new Rectangle(0, 0, frame.Width, frame.Height),
                EngineName = Name,
                CapturedAt = DateTime.Now,
                MonitorIndex = monitorIndex
            };
        }
        catch (Exception ex)
        {
            return new CaptureResult
            {
                Success = false,
                EngineName = Name,
                ErrorMessage = $"DXGI 캡처 예외: {ex.Message}"
            };
        }
    }

    public override CaptureResult CaptureRegion(Rectangle region)
    {
        // 먼저 해당 영역이 포함된 모니터 찾기
        var monitors = DpiHelper.GetAllMonitors();

        // 모니터가 없으면 에러
        if (monitors.Count == 0)
        {
            return new CaptureResult
            {
                Success = false,
                EngineName = Name,
                ErrorMessage = "모니터를 찾을 수 없습니다."
            };
        }

        // 영역과 교차하는 모니터 찾기, 없으면 첫 번째 모니터 사용
        var targetMonitor = monitors.FirstOrDefault(m => m.Bounds.IntersectsWith(region));

        // 교차하지 않으면 영역의 중심점이 포함된 모니터 찾기
        if (targetMonitor == null)
        {
            var centerX = region.X + region.Width / 2;
            var centerY = region.Y + region.Height / 2;
            targetMonitor = monitors.FirstOrDefault(m =>
                centerX >= m.Bounds.Left && centerX < m.Bounds.Right &&
                centerY >= m.Bounds.Top && centerY < m.Bounds.Bottom);
        }

        // 그래도 없으면 기본 모니터
        targetMonitor ??= monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

        // 모니터 전체 캡처 후 영역 잘라내기
        var monitorResult = CaptureMonitor(targetMonitor.Index);
        if (!monitorResult.Success || monitorResult.Image == null)
        {
            return monitorResult;
        }

        try
        {
            // 모니터 좌표를 기준으로 영역 계산
            var relativeX = region.X - targetMonitor.Bounds.X;
            var relativeY = region.Y - targetMonitor.Bounds.Y;
            var cropRect = new Rectangle(
                Math.Max(0, relativeX),
                Math.Max(0, relativeY),
                Math.Min(region.Width, monitorResult.Image.Width - relativeX),
                Math.Min(region.Height, monitorResult.Image.Height - relativeY)
            );

            if (cropRect.Width <= 0 || cropRect.Height <= 0)
            {
                monitorResult.Dispose();
                return new CaptureResult
                {
                    Success = false,
                    EngineName = Name,
                    ErrorMessage = "잘라낼 영역이 유효하지 않습니다."
                };
            }

            var croppedBitmap = monitorResult.Image.Clone(cropRect, PixelFormat.Format32bppArgb);
            monitorResult.Dispose();

            return new CaptureResult
            {
                Success = true,
                Image = croppedBitmap,
                CaptureArea = region,
                EngineName = Name,
                CapturedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            monitorResult.Dispose();
            return new CaptureResult
            {
                Success = false,
                EngineName = Name,
                ErrorMessage = $"영역 자르기 실패: {ex.Message}"
            };
        }
    }

    private Bitmap? CaptureFrame()
    {
        if (_duplicatedOutput == null || _device == null)
            return null;

        try
        {
            // 프레임 획득 시도 (최대 500ms 대기)
            var result = _duplicatedOutput.TryAcquireNextFrame(500, out var frameInfo, out var desktopResource);

            if (result.Failure)
            {
                return null;
            }

            using (desktopResource)
            using (var texture = desktopResource.QueryInterface<Texture2D>())
            {
                var desc = texture.Description;

                // 스테이징 텍스처 생성 (CPU 접근 가능)
                if (_stagingTexture == null ||
                    _stagingTexture.Description.Width != desc.Width ||
                    _stagingTexture.Description.Height != desc.Height)
                {
                    _stagingTexture?.Dispose();
                    _stagingTexture = new Texture2D(_device, new Texture2DDescription
                    {
                        CpuAccessFlags = CpuAccessFlags.Read,
                        BindFlags = BindFlags.None,
                        Format = desc.Format,
                        Width = desc.Width,
                        Height = desc.Height,
                        OptionFlags = ResourceOptionFlags.None,
                        MipLevels = 1,
                        ArraySize = 1,
                        SampleDescription = { Count = 1, Quality = 0 },
                        Usage = ResourceUsage.Staging
                    });
                }

                // GPU -> CPU 복사
                _device.ImmediateContext.CopyResource(texture, _stagingTexture);

                // 데이터 읽기
                var dataBox = _device.ImmediateContext.MapSubresource(
                    _stagingTexture, 0, MapMode.Read, MapFlags.None);

                try
                {
                    var bitmap = new Bitmap(desc.Width, desc.Height, PixelFormat.Format32bppArgb);
                    var bitmapData = bitmap.LockBits(
                        new Rectangle(0, 0, desc.Width, desc.Height),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);

                    try
                    {
                        // 행 단위로 복사 (stride 차이 처리)
                        var sourcePtr = dataBox.DataPointer;
                        var destPtr = bitmapData.Scan0;
                        var rowBytes = desc.Width * 4;

                        for (int y = 0; y < desc.Height; y++)
                        {
                            Utilities.CopyMemory(destPtr, sourcePtr, rowBytes);
                            sourcePtr = IntPtr.Add(sourcePtr, dataBox.RowPitch);
                            destPtr = IntPtr.Add(destPtr, bitmapData.Stride);
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(bitmapData);
                    }

                    return bitmap;
                }
                finally
                {
                    _device.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
                }
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                _duplicatedOutput?.ReleaseFrame();
            }
            catch { }
        }
    }

    private void Cleanup()
    {
        _stagingTexture?.Dispose();
        _stagingTexture = null;

        _duplicatedOutput?.Dispose();
        _duplicatedOutput = null;

        _device?.Dispose();
        _device = null;
    }

    public override void Dispose()
    {
        Cleanup();
        base.Dispose();
    }
}
