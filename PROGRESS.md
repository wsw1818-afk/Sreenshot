# PROGRESS.md - SmartCapture 개발 진행 상황

## 개요
- **프로젝트**: SmartCapture (스크린샷 캡처 도구)
- **플랫폼**: Windows 10/11 (.NET 8.0)
- **상태**: 코드 개선 완료

---

## 버그 수정 이력 (이전 세션)

### 1. WinRtCapture.cs - 스레드 타임아웃 처리 (P0) [수정 완료]
**위치**: `Screenshot/Services/Capture/WinRtCapture.cs` (Line 54-62)
**문제**: `Thread.Interrupt()`는 스레드를 종료하지 않음
**수정**: `IsBackground = true`를 `Start()` 전에 설정하여 프로세스 종료 시 자동 정리

---

### 2. CaptureOverlayForm.cs - Division by Zero (P1) [수정 완료]
**위치**: `Screenshot/Views/CaptureOverlayForm.cs` - `OnMouseUp`
**문제**: `GetWindowRect` 실패 시 크기가 0이 되어 나누기 예외 발생 가능
**수정**: `Math.Max(1, ...)` 적용

---

### 3. MainWindow.xaml.cs - 이벤트 구독 누수 (P1) [수정 완료]
**위치**: `Screenshot/MainWindow.xaml.cs` - `CaptureScrollAsync`, `CaptureWithChromeCdpAsync`
**문제**: 람다로 이벤트 구독 후 해제 없음 → 반복 호출 시 핸들러 중복 등록
**수정**: 로컬 함수로 분리 후 `try/finally`에서 구독 해제 (`ProgressChanged`, `StatusChanged`)

---

### 4. DxgiCapture.cs - 잘못된 ReleaseFrame (P1) [수정 완료]
**위치**: `Screenshot/Services/Capture/DxgiCapture.cs` - `IsAvailable` getter
**문제**: `IsAvailable` getter에서 `AcquireNextFrame` 없이 `ReleaseFrame()` 호출 → DXGI 오류
**수정**: `_duplication.Description` 속성 조회로 가벼운 유효성 테스트 대체

---

### 5. CaptureManager.cs - Null 참조 방어 (P2) [수정 완료]
**위치**: `Screenshot/Services/Capture/CaptureManager.cs` - `ExecuteCaptureRaw`
**문제**: `result`가 null일 가능성 (ICaptureEngine 구현에 따라)
**수정**: `result?.Success == true`, `result?.Image?.Dispose()` 패턴 적용
**참고**: 현재 구현에서는 실질적 위험 낮음 (방어적 수정)

---

### 6. MainWindow.xaml.cs - _isCapturing 경쟁 조건 (P2) [수정 불필요]
**위치**: `Screenshot/MainWindow.xaml.cs`
**분석 결과**: 모든 호출이 UI 스레드(Dispatcher)에서만 발생하므로 실질적 경쟁 조건 없음
**결론**: SemaphoreSlim 도입은 과잉 설계 → 현재 코드 유지

---

### 7. GdiCapture.cs - 리소스 정리 (P2) [이미 처리됨]
**위치**: `Screenshot/Services/Capture/GdiCapture.cs`
**분석 결과**: 이미 `result.Image?.Dispose()` 호출이 존재 (Line 97, 110, 121)
**결론**: 추가 수정 불필요

---

### 8. CaptureOverlayForm.cs - 임시 파일 삭제 로깅 (P3) [수정 완료]
**위치**: `Screenshot/Views/CaptureOverlayForm.cs` - `Dispose`
**문제**: 삭제 실패가 조용히 무시됨
**수정**: `catch` 블록에 `CaptureLogger.Warn` 로깅 추가

---

### 9. MainWindow.xaml.cs - HandleCaptureResult 파일 존재 검증 (P3) [수정 완료]
**위치**: `Screenshot/MainWindow.xaml.cs` - `HandleCaptureResult`
**문제**: `SavedFilePath`는 있지만 파일이 실제로 삭제된 경우 저장을 건너뜀
**수정**: `File.Exists()` 체크 추가, 파일 누락 시 재저장 로직 추가

---

### 10. CaptureOverlayForm.cs - Deactivate NullReferenceException (P1) [수정 완료]
**위치**: `Screenshot/Views/CaptureOverlayForm.cs` - Deactivate 핸들러
**문제**: Deactivate 이벤트가 Form Dispose 후에 발생하면 Handle 접근 시 NullReferenceException
**수정**: `IsDisposed`, `IsHandleCreated` 체크 + `ObjectDisposedException` catch 추가

---

### 11. CaptureOverlayForm.cs - 영역 캡처 자동 취소 (P1) [수정 완료]
**위치**: `Screenshot/Views/CaptureOverlayForm.cs` - FormClosing 핸들러
**문제**: 시스템 Deactivate로 인한 FormClosing(CloseReason=None)이 오버레이를 자동 닫음
**수정**: `!_closingByUser`일 때 `e.Cancel = true`로 비사용자 닫기 차단

---

## 코드 개선 (현재 세션)

### 12. MainWindow.xaml.cs - CaptureMonitorAsync 이중 호출 방어 (P2) [수정 완료]
**위치**: `Screenshot/MainWindow.xaml.cs` - `CaptureMonitorAsync`
**문제**: 다른 캡처 메서드에는 `_isCapturing` 가드가 있지만 모니터 캡처에는 없음
**수정**: `_isCapturing` 가드 + `try/finally` 패턴 추가

---

### 13. MainWindow.xaml.cs - 데드코드 제거 (P2) [수정 완료]
**위치**: `Screenshot/MainWindow.xaml.cs` - `#region CaptureScreen Direct` 전체
**문제**: DXGI 전환 후 사용되지 않는 코드 160줄+
- `CaptureScreenDirect()`, `CaptureScreenForRegion()`, `CaptureScreenWithCopyFromScreen()`
- `IsBlackImage()` (static), `IsAlmostBlackImage()` (static)
- GDI P/Invoke 선언 8개 (`GetDesktopWindow`, `GetWindowDC`, `BitBlt` 등)
- `DwmFlush` P/Invoke (미사용)
- `using System.Runtime.InteropServices` (더 이상 불필요)
**수정**: 전체 삭제

---

### 14. CaptureOverlayForm.cs - 데드코드 제거 (P3) [수정 완료]
**위치**: `Screenshot/Views/CaptureOverlayForm.cs` - `CaptureScreen()` static 메서드
**문제**: DXGI 전환 후 사용되지 않는 CopyFromScreen 기반 캡처 + 디버그 이미지 저장 코드
**수정**: 메서드 삭제

---

### 15. ImageEditorWindow.xaml.cs - _originalImage/_editedImage 누수 (P1) [수정 완료]
**위치**: `Screenshot/Views/ImageEditorWindow.xaml.cs` - `OnClosed`
**문제**: `_originalImage`와 `_editedImage`가 OnClosed에서 Dispose되지 않음 (Undo/Redo 스택만 정리)
**수정**: `_originalImage?.Dispose()`, `_editedImage?.Dispose()` 추가

---

### 16. OcrService.cs - SoftwareBitmap 누수 (P2) [수정 완료]
**위치**: `Screenshot/Services/OcrService.cs` - `ExtractTextAsync`
**문제**: `SoftwareBitmap`이 `using` 없이 생성되어 Dispose되지 않음
**수정**: `using var softwareBitmap` 추가

---

### 17. ChromeCaptureService.cs - StitchImages 예외 시 Bitmap 누수 (P2) [수정 완료]
**위치**: `Screenshot/Services/ChromeCaptureService.cs` - 이미지 합성 부분
**문제**: `StitchImages()`가 예외를 던지면 `captures` 리스트의 Bitmap들이 Dispose되지 않음
**수정**: `try/catch`로 감싸서 예외 시 captures 전체 Dispose 후 rethrow

---

## 미수정 버그 (향후 작업)

| 우선순위 | 버그 | 위치 | 설명 |
|---------|------|------|------|
| P2 | Base64 디코딩 예외 | ChromeCaptureService.cs | `Convert.FromBase64String` 예외 처리 없음 |
| P3 | 마우스 위치 복원 | ScrollCaptureService.cs | `SetCursorPos` 반환값 미확인 |
| P3 | 버퍼 오버플로우 | CaptureLogger.cs | `Buffer.ToString().Split('\n')` 비효율 |
| P3 | 모자이크 성능 | ImageEditorWindow.xaml.cs | GetPixel/SetPixel → LockBits 권장 |
| P3 | GC 강제 호출 | DxgiCapture.cs | `FullDispose()`에서 GC.Collect() 3회 |

### 이미 안전한 것으로 확인된 항목

| 항목 | 위치 | 분석 결과 |
|------|------|----------|
| WebSocket 연결 | ChromeCaptureService.cs | `using`으로 감싸져 있어 자동 정리됨 |
| HwndSource 누수 | HotkeyService.cs | null-conditional로 안전하게 처리됨 |
| StringBuilder 할당 | WindowCaptureService.cs | titleLength==0 체크 후 조기 반환 |
| RegistryKey 누수 | SettingsWindow.xaml.cs | `using`으로 감싸져 있어 안전 |
| captures 리스트 | ScrollCaptureService.cs | Count==1이면 caller 책임, Count>1이면 finally에서 정리 (올바름) |

---

## 수정 요약

| 우선순위 | 버그 | 위치 | 상태 |
|---------|------|------|------|
| P0 | 스레드 타임아웃 | WinRtCapture.cs | 수정 완료 |
| P1 | Division by Zero | CaptureOverlayForm.cs | 수정 완료 |
| P1 | 이벤트 구독 누수 | MainWindow.xaml.cs | 수정 완료 |
| P1 | 잘못된 ReleaseFrame | DxgiCapture.cs | 수정 완료 |
| P1 | Deactivate NullRef | CaptureOverlayForm.cs | 수정 완료 |
| P1 | 영역 캡처 자동 취소 | CaptureOverlayForm.cs | 수정 완료 |
| P1 | _originalImage 누수 | ImageEditorWindow.xaml.cs | 수정 완료 |
| P2 | Null 참조 방어 | CaptureManager.cs | 수정 완료 |
| P2 | 모니터 캡처 가드 | MainWindow.xaml.cs | 수정 완료 |
| P2 | 데드코드 제거 | MainWindow.xaml.cs | 수정 완료 |
| P2 | SoftwareBitmap 누수 | OcrService.cs | 수정 완료 |
| P2 | StitchImages 예외 누수 | ChromeCaptureService.cs | 수정 완료 |
| P2 | _isCapturing 경쟁 | MainWindow.xaml.cs | 수정 불필요 |
| P2 | GDI 리소스 정리 | GdiCapture.cs | 이미 처리됨 |
| P3 | 임시 파일 삭제 로깅 | CaptureOverlayForm.cs | 수정 완료 |
| P3 | 파일 존재 검증 | MainWindow.xaml.cs | 수정 완료 |
| P3 | 데드코드 제거 | CaptureOverlayForm.cs | 수정 완료 |

---

**마지막 업데이트**: 2026-02-05
