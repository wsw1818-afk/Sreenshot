# PROGRESS.md - SmartCapture 개발 진행 상황

## 개요
- **프로젝트**: SmartCapture (스크린샷 캡처 도구)
- **플랫폼**: Windows 10/11 (.NET 8.0)
- **상태**: 버그 수정 완료

---

## 버그 수정 이력

### 1. WinRtCapture.cs - 스레드 타임아웃 처리 (P0) [수정 완료]
**위치**: `Screenshot/Services/Capture/WinRtCapture.cs` (Line 54-62)
**문제**: `Thread.Interrupt()`는 스레드를 종료하지 않음
**수정**: `IsBackground = true`를 `Start()` 전에 설정하여 프로세스 종료 시 자동 정리

---

### 2. CaptureOverlayForm.cs - Division by Zero (P1) [수정 완료]
**위치**: `Screenshot/Views/CaptureOverlayForm.cs` (Line 360-363)
**문제**: `GetWindowRect` 실패 시 크기가 0이 되어 나누기 예외 발생 가능
**수정**: `Math.Max(1, ...)` 적용

---

### 3. MainWindow.xaml.cs - 이벤트 구독 누수 (P1) [수정 완료]
**위치**: `Screenshot/MainWindow.xaml.cs` - `CaptureScrollAsync`, `CaptureWithChromeCdpAsync`
**문제**: 람다로 이벤트 구독 후 해제 없음 → 반복 호출 시 핸들러 중복 등록
**수정**: 로컬 함수로 분리 후 `try/finally`에서 구독 해제 (`ProgressChanged`, `StatusChanged`)

---

### 4. DxgiCapture.cs - 잘못된 ReleaseFrame (P1) [수정 완료]
**위치**: `Screenshot/Services/Capture/DxgiCapture.cs` (Line 48-64)
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
**위치**: `Screenshot/MainWindow.xaml.cs` (Line 129, 159 등)
**분석 결과**: 모든 호출이 UI 스레드(Dispatcher)에서만 발생하므로 실질적 경쟁 조건 없음
**결론**: SemaphoreSlim 도입은 과잉 설계 → 현재 코드 유지

---

### 7. GdiCapture.cs - 리소스 정리 (P2) [이미 처리됨]
**위치**: `Screenshot/Services/Capture/GdiCapture.cs` (Line 110, 121)
**분석 결과**: PROGRESS.md 분석과 달리 이미 `result.Image?.Dispose()` 호출 존재
- Line 97: BitBlt 실패 시 `result.Image?.Dispose()` 호출
- Line 110: 재시도 전 `result.Image?.Dispose()` 호출
- Line 121: 최종 실패 시 `result.Image?.Dispose()` 호출
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

## 수정 요약

| 우선순위 | 버그 | 위치 | 상태 |
|---------|------|------|------|
| P0 | 스레드 타임아웃 | WinRtCapture.cs | 수정 완료 |
| P1 | Division by Zero | CaptureOverlayForm.cs | 수정 완료 |
| P1 | 이벤트 구독 누수 | MainWindow.xaml.cs | 수정 완료 |
| P1 | 잘못된 ReleaseFrame | DxgiCapture.cs | 수정 완료 |
| P2 | Null 참조 방어 | CaptureManager.cs | 수정 완료 |
| P2 | _isCapturing 경쟁 조건 | MainWindow.xaml.cs | 수정 불필요 (UI 스레드 단일) |
| P2 | GDI 리소스 정리 | GdiCapture.cs | 이미 처리됨 |
| P3 | 임시 파일 삭제 로깅 | CaptureOverlayForm.cs | 수정 완료 |
| P3 | 파일 존재 검증 | MainWindow.xaml.cs | 수정 완료 |

---

**마지막 업데이트**: 2026-02-05
