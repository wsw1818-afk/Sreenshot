# PROGRESS.md - SmartCapture 개발 진행 상황

## 개요
- **프로젝트**: SmartCapture (스크린샷 캡처 도구)
- **플랫폼**: Windows 10/11 (.NET 8.0)
- **상태**: 버그 수정 완료
- **마지막 업데이트**: 2026-02-05

---

## 버그 #1~#40 검증 및 수정 결과

### 수정 완료 (이전 세션 + 현재 세션)

| # | 파일 | 수정 내용 | 세션 |
|---|------|----------|------|
| #1 | WinRtCapture.cs | `IsBackground = true` 설정으로 스레드 자동 정리 | 이전 |
| #2 | CaptureOverlayForm.cs | `Math.Max(1, ...)` 추가로 DivisionByZero 방지 | 이전 |
| #3 | MainWindow.xaml.cs | 로컬 함수 + try/finally로 이벤트 구독 누수 수정 | 이전 |
| #4 | DxgiCapture.cs | `_duplication.Description`으로 세션 유효성 테스트 변경 | 이전 |
| #5 | GdiCapture.cs | fallback 실패 시 `result.Image?.Dispose()` 추가 | 이전 |
| #7 | CaptureManager.cs | `result?.Success == true` null 방어 | 이전 |
| #8 | CaptureOverlayForm.cs | 임시 파일 삭제 실패 로깅 추가 | 이전 |
| #9 | MainWindow.xaml.cs | `File.Exists` 체크 + 재저장 로직 | 이전 |
| #11 | ChromeCaptureService.cs | StitchImages 예외 시 captures Dispose try/catch | 이전 |
| #13 | ImageEditorWindow.xaml.cs | `_originalImage?.Dispose()` OnClosed에 추가 | 이전 |
| #14 | OcrService.cs | `using var softwareBitmap` 추가 | 이전 |
| - | MainWindow.xaml.cs | CaptureMonitorAsync `_isCapturing` 가드 추가 | 이전 |
| - | MainWindow.xaml.cs | CopyFromScreen 데드코드 160줄 제거 | 이전 |
| - | CaptureOverlayForm.cs | CaptureScreen() 데드코드 제거 | 이전 |
| #25 | NotificationService.cs | `Application.Current?.Dispatcher == null` 체크 추가 (3곳) | 현재 |
| #31 | ImageEditorWindow.xaml.cs | Undo 스택 MaxUndoCount=20 제한 + Redo 스택 Dispose | 현재 |

### 허위/안전으로 확인된 버그

| # | 파일 | 판정 | 사유 |
|---|------|------|------|
| #6 | MainWindow.xaml.cs | 허위 | `_isCapturing`는 UI 스레드 전용, 경쟁 조건 불가 |
| #10 | ChromeCaptureService.cs | 안전 | WebSocket은 `using`으로 처리됨 |
| #12 | ScrollCaptureService.cs | 안전 | `captures.Count == 1`일 때 호출자가 소유권 보유 |
| #15 | HotkeyService.cs | 안전 | `_source?.RemoveHook`은 null일 때 무시됨 |
| #16 | WindowCaptureService.cs | 안전 | `titleLength=0`일 때 빈 StringBuilder는 무해 |
| #17 | MainWindow.xaml.cs | 안전 | Dispose 순서 올바름 (서비스 → TrayIcon) |
| #18 | ChromeCaptureService.cs | P2 무해 | 상위 catch(Exception)가 처리 |
| #19 | ScrollCaptureService.cs | P3 허위 | SetCursorPos 실패 가능성 극히 낮음 |
| #20 | CaptureLogger.cs | P3 안전 | StringBuilder 메모리 문제 없음 |
| #21 | SettingsWindow.xaml.cs | P3 안전 | RegistryKey `using`으로 처리 |
| #22 | ImageEditorWindow.xaml.cs | P3 기능정상 | GetPixel/SetPixel 느리지만 동작에 문제 없음 |
| #23 | DxgiCapture.cs | P3 유지 | COM 객체 정리에 GC.Collect 필요 |
| #24 | NotificationService.cs | 허위 | WPF Window.Close()는 리소스 해제됨, DispatcherTimer.Stop()으로 충분 |
| #26 | CaptureOverlay.xaml.cs | P3 낮음 | MouseUp에서 width/height < 10 체크 있어 도달 가능성 낮음 |
| #27 | CaptureOverlay.xaml.cs | 허위 | MainWindow에서 `overlay.CapturedScreen.Dispose()` 호출됨 |
| #28 | CaptureOverlay.xaml.cs | 안전 | try/finally로 DeleteObject 보장됨 |
| #29 | HotkeyService.cs | 허위 | catch에서 이미 등록된 키 모두 해제, 정상 동작 |
| #30 | UrlInputDialog.xaml.cs | P3 낮음 | Chrome이 잘못된 URL 처리 가능 |
| #32 | ImageEditorWindow.xaml.cs | 허위 | 모든 Pen이 `using`으로 처리됨 |
| #33 | ChromeCaptureService.cs | 허위 | HttpClient는 재사용 권장 패턴 (Dispose하면 오히려 문제) |
| #34 | CaptureOverlayForm.cs | 수정됨 | Dispose(bool)에서 Pen/Brush 모두 Dispose |
| #35 | ScrollCaptureService.cs | 안전 | StitchImages에 `images.Count == 0` 체크 + FindOverlap 경계 검사 있음 |
| #36 | DxgiCapture.cs | 허위 | TestInitialize()는 새 객체 생성, 데드락 불가 |
| #37 | MainWindow.xaml.cs | 안전 | `oldest.Dispose()` 호출됨, CaptureResult.Dispose가 Image 정리 |
| #38 | WinRtCapture.cs | 안전 | interop null 체크 Line 117-119에 있음 |
| #39 | CaptureManager.cs | 허위 | `_engines`는 초기화 후 불변, UI 스레드 전용 |
| #40 | GdiCapture.cs | 안전 | finally에서 hBitmap DeleteObject 보장됨 |

---

## 다음 할 일
- 없음 (모든 P0/P1 버그 수정 완료)
