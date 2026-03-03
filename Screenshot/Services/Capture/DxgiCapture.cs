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

    // 다중 모니터 논리↔물리 매핑 (CaptureVirtualScreen에서 설정, CaptureRegion에서 사용)
    private record MonitorMapping(Rectangle LogicalBounds, Rectangle PhysicalBounds, int PhysicalX, int PhysicalY);
    private List<MonitorMapping>? _lastMonitorMappings;
    
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
        Bitmap? sourceImage = null;

        // 다중 모니터 환경 여부 관계없이 항상 VirtualScreen 합성 캡처 시도
        var vsResult = CaptureVirtualScreen();
        if (vsResult.Success && vsResult.Image != null)
        {
            sourceImage = vsResult.Image;
        }

        if (sourceImage == null)
        {
            // VirtualScreen 합성 캡처 실패 → 주 모니터 단독 폴백
            _lastMonitorMappings = null;
            var result = CaptureFullScreen();
            if (!result.Success || result.Image == null) return result;
            sourceImage = result.Image;
        }

        try
        {
            using var original = sourceImage;

            // 논리 좌표 → 물리 좌표 변환 (모니터 매핑 테이블 사용)
            var physRegion = LogicalToPhysicalRegion(region, original.Width, original.Height);

            // 이미지 범위로 클리핑
            var imageRect = new Rectangle(0, 0, original.Width, original.Height);
            physRegion = Rectangle.Intersect(physRegion, imageRect);

            if (physRegion.Width <= 0 || physRegion.Height <= 0)
            {
                CaptureLogger.Warn("DXGI", $"CaptureRegion 클리핑 후 빈 영역: region={region}, physRegion={physRegion}, image={original.Width}x{original.Height}");
                return new CaptureResult { Success = false, EngineName = Name, ErrorMessage = "DXGI 캡처 영역 범위 초과" };
            }

            CaptureLogger.Info("DXGI", $"CaptureRegion: logical={region} → physical={physRegion}, image={original.Width}x{original.Height}");

            var cropped = new Bitmap(physRegion.Width, physRegion.Height);
            using (var g = Graphics.FromImage(cropped))
                g.DrawImage(original, new Rectangle(0, 0, physRegion.Width, physRegion.Height), physRegion, GraphicsUnit.Pixel);

            return new CaptureResult { Success = true, Image = cropped, EngineName = Name };
        }
        catch (Exception ex)
        {
            return new CaptureResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// 논리 좌표(VirtualScreen 기준) → 물리 좌표(합성 이미지 기준) 변환.
    /// 각 모니터의 DPI 스케일링을 고려하여 정확한 물리 픽셀 위치를 계산합니다.
    /// </summary>
    private Rectangle LogicalToPhysicalRegion(Rectangle logicalRegion, int imageWidth, int imageHeight)
    {
        var mappings = _lastMonitorMappings;
        if (mappings == null || mappings.Count == 0)
        {
            // 매핑 없음 (단일 모니터 또는 FullScreen 폴백) → VirtualScreen 오프셋만 적용
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            return new Rectangle(
                logicalRegion.X - vs.X,
                logicalRegion.Y - vs.Y,
                logicalRegion.Width,
                logicalRegion.Height);
        }

        // 영역이 여러 모니터에 걸칠 수 있으므로, 각 모니터별로 교차 영역을 물리 좌표로 변환 후 합성
        int physLeft = int.MaxValue, physTop = int.MaxValue;
        int physRight = int.MinValue, physBottom = int.MinValue;
        bool hasIntersection = false;

        foreach (var m in mappings)
        {
            var intersection = Rectangle.Intersect(logicalRegion, m.LogicalBounds);
            if (intersection.Width <= 0 || intersection.Height <= 0) continue;

            hasIntersection = true;

            // 논리 좌표에서 모니터 내 상대 위치 (0~1 비율)
            double relX = (double)(intersection.X - m.LogicalBounds.X) / m.LogicalBounds.Width;
            double relY = (double)(intersection.Y - m.LogicalBounds.Y) / m.LogicalBounds.Height;
            double relW = (double)intersection.Width / m.LogicalBounds.Width;
            double relH = (double)intersection.Height / m.LogicalBounds.Height;

            // 물리 좌표로 변환
            int pX = m.PhysicalBounds.X + (int)(relX * m.PhysicalBounds.Width);
            int pY = m.PhysicalBounds.Y + (int)(relY * m.PhysicalBounds.Height);
            int pR = m.PhysicalBounds.X + (int)((relX + relW) * m.PhysicalBounds.Width);
            int pB = m.PhysicalBounds.Y + (int)((relY + relH) * m.PhysicalBounds.Height);

            physLeft = Math.Min(physLeft, pX);
            physTop = Math.Min(physTop, pY);
            physRight = Math.Max(physRight, pR);
            physBottom = Math.Max(physBottom, pB);
        }

        if (!hasIntersection)
        {
            // 어떤 모니터와도 겹치지 않음 → 기본 변환
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            return new Rectangle(
                logicalRegion.X - vs.X,
                logicalRegion.Y - vs.Y,
                logicalRegion.Width,
                logicalRegion.Height);
        }

        return new Rectangle(physLeft, physTop, physRight - physLeft, physBottom - physTop);
    }

    /// <summary>
    /// 다중 모니터 환경에서 각 출력을 개별 캡처한 뒤 VirtualScreen 크기 비트맵에 합성.
    /// 각 모니터의 물리 텍스처를 논리 좌표에 맞춰 그리되, 물리 해상도가 다른 경우(DPI 스케일링)
    /// 텍스처를 원본 크기 그대로 배치합니다.
    /// </summary>
    private CaptureResult CaptureVirtualScreen()
    {
        CaptureLogger.Info("DXGI", "다중 모니터 합성 캡처 시작");
        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;

        // 기존 인스턴스의 DuplicateOutput 세션 정리 (같은 출력에 중복 DuplicateOutput 방지)
        Dispose();

        Factory1? factory = null;
        try
        {
            factory = new Factory1();
            var capturedMonitors = new List<(Bitmap bmp, Rectangle bounds)>();

            // 모든 어댑터 → 모든 출력 열거
            for (int ai = 0; ai < factory.GetAdapterCount1(); ai++)
            {
                Adapter1? adapter = null;
                try
                {
                    adapter = factory.GetAdapter1(ai);
                    for (int oi = 0; oi < 16; oi++)
                    {
                        Output? output = null;
                        Output1? output1 = null;
                        SharpDX.Direct3D11.Device? device = null;
                        OutputDuplication? duplication = null;

                        try
                        {
                            output = adapter.GetOutput(oi);
                            var outputDesc = output.Description;
                            var bounds = new Rectangle(
                                outputDesc.DesktopBounds.Left,
                                outputDesc.DesktopBounds.Top,
                                outputDesc.DesktopBounds.Right - outputDesc.DesktopBounds.Left,
                                outputDesc.DesktopBounds.Bottom - outputDesc.DesktopBounds.Top);

                            CaptureLogger.Info("DXGI", $"출력 [{ai}:{oi}] {outputDesc.DeviceName} - Bounds={bounds}");

                            device = new SharpDX.Direct3D11.Device(adapter);
                            output1 = output.QueryInterface<Output1>();
                            duplication = output1.DuplicateOutput(device);

                            try { duplication.ReleaseFrame(); } catch { }
                            Thread.Sleep(150);

                            // 첫 프레임이 검은 화면일 수 있으므로 최대 3회 재시도
                            Bitmap? capturedBmp = null;
                            for (int retry = 0; retry < 3; retry++)
                            {
                                var frameResult = duplication.TryAcquireNextFrame(2000, out _, out var resource);
                                if (frameResult.Success)
                                {
                                    try
                                    {
                                        using var texture = resource.QueryInterface<Texture2D>();
                                        var bmp = TextureToBitmapStatic(device, texture);

                                        if (!CaptureEngineBase.IsBlackImage(bmp))
                                        {
                                            capturedBmp = bmp;
                                            break;
                                        }

                                        CaptureLogger.Warn("DXGI", $"출력 [{ai}:{oi}] 시도 {retry + 1}: 검은 화면, 재시도");
                                        bmp.Dispose();
                                    }
                                    finally
                                    {
                                        duplication.ReleaseFrame();
                                    }
                                    Thread.Sleep(200 * (retry + 1));
                                }
                                else
                                {
                                    CaptureLogger.Warn("DXGI", $"출력 [{ai}:{oi}] 시도 {retry + 1}: 프레임 획득 실패");
                                    Thread.Sleep(200);
                                }
                            }

                            if (capturedBmp != null)
                            {
                                CaptureLogger.Info("DXGI", $"출력 [{ai}:{oi}] 캡처 성공: Texture={capturedBmp.Width}x{capturedBmp.Height}, LogicalBounds={bounds.Width}x{bounds.Height}");
                                capturedMonitors.Add((capturedBmp, bounds));
                            }
                            else
                            {
                                CaptureLogger.Warn("DXGI", $"출력 [{ai}:{oi}] 3회 재시도 후 실패 (검은 화면)");
                            }
                        }
                        catch (SharpDXException ex) when (ex.ResultCode == SharpDX.DXGI.ResultCode.NotFound)
                        {
                            break; // 더 이상 출력 없음
                        }
                        catch (Exception ex)
                        {
                            CaptureLogger.Warn("DXGI", $"출력 [{ai}:{oi}] 캡처 실패: {ex.Message}");
                        }
                        finally
                        {
                            duplication?.Dispose();
                            output1?.Dispose();
                            device?.Dispose();
                            output?.Dispose();
                        }
                    }
                }
                catch (SharpDXException ex) when (ex.ResultCode == SharpDX.DXGI.ResultCode.NotFound)
                {
                    break; // 더 이상 어댑터 없음
                }
                finally
                {
                    adapter?.Dispose();
                }
            }

            if (capturedMonitors.Count == 0)
            {
                return new CaptureResult { Success = false, EngineName = Name, ErrorMessage = "DXGI 다중 모니터 캡처: 캡처된 출력 없음" };
            }

            // 물리 해상도 기반으로 합성 (스케일업 없이 원본 품질 유지)
            // 각 모니터의 물리 텍스처를 원본 크기 그대로 나란히 배치
            var mappings = new List<MonitorMapping>();
            int totalPhysWidth = 0;
            int maxPhysHeight = 0;

            // 논리 X 좌표 순서대로 정렬 후, 물리 텍스처를 순서대로 배치
            var sorted = capturedMonitors.OrderBy(m => m.bounds.X).ThenBy(m => m.bounds.Y).ToList();
            foreach (var (bmp, bounds) in sorted)
            {
                var physBounds = new Rectangle(totalPhysWidth, 0, bmp.Width, bmp.Height);
                mappings.Add(new MonitorMapping(bounds, physBounds, totalPhysWidth, 0));
                CaptureLogger.Info("DXGI", $"매핑: logical={bounds} → physical=({totalPhysWidth},0,{bmp.Width},{bmp.Height})");
                totalPhysWidth += bmp.Width;
                maxPhysHeight = Math.Max(maxPhysHeight, bmp.Height);
            }

            _lastMonitorMappings = mappings;

            var composite = new Bitmap(totalPhysWidth, maxPhysHeight, PixelFormat.Format32bppArgb);
            composite.SetResolution(96f, 96f);
            using (var g = Graphics.FromImage(composite))
            {
                g.Clear(Color.Black);
                for (int i = 0; i < sorted.Count; i++)
                {
                    var (bmp, _) = sorted[i];
                    var mapping = mappings[i];
                    bmp.SetResolution(96f, 96f);
                    // 물리 텍스처를 원본 크기 그대로 배치 (스케일링 없음)
                    g.DrawImage(bmp, mapping.PhysicalX, mapping.PhysicalY);
                    CaptureLogger.Info("DXGI", $"합성: physPos=({mapping.PhysicalX},{mapping.PhysicalY}), physSize={bmp.Width}x{bmp.Height}");
                    bmp.Dispose();
                }
            }

            CaptureLogger.Info("DXGI", $"다중 모니터 합성 완료(물리): {composite.Width}x{composite.Height}, 모니터 {capturedMonitors.Count}개");
            return new CaptureResult { Success = true, Image = composite, EngineName = Name };
        }
        catch (Exception ex)
        {
            CaptureLogger.Error("DXGI", "다중 모니터 합성 캡처 실패", ex);
            return new CaptureResult { Success = false, EngineName = Name, ErrorMessage = $"DXGI VirtualScreen 실패: {ex.Message}" };
        }
        finally
        {
            factory?.Dispose();
        }
    }

    /// <summary>
    /// 지정된 디바이스와 텍스처로 비트맵 변환 (static 용도, 다중 모니터 캡처에서 사용)
    /// </summary>
    private static Bitmap TextureToBitmapStatic(SharpDX.Direct3D11.Device device, Texture2D texture)
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

        using var stagingTexture = new Texture2D(device, stagingDesc);
        device.ImmediateContext.CopyResource(texture, stagingTexture);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var dataBox = device.ImmediateContext.MapSubresource(stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
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
            device.ImmediateContext.UnmapSubresource(stagingTexture, 0);
        }

        return bitmap;
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
