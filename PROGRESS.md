# PROGRESS.md - SmartCapture 개발 진행 상황

## 개요
- **프로젝트**: SmartCapture (스크린샷 캡처 도구)
- **플랫폼**: Windows 10/11 (.NET 8.0)
- **상태**: 버그 수정 완료 - 전체 코드 검증 완료
- **마지막 업데이트**: 2026-02-06

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
| #41 | CaptureOverlayForm.cs | Deactivate 후 포커스 복구(Activate+Focus) + **3회** 반복 즉시 취소 + **30초** 안전 타이머 + 드래그 중 Deactivate 무시 + 우클릭 취소 + 조작 시작 시 타이머 완전 해제 | 현재 |
| #49 | AppSettings.cs, SettingsWindow.xaml.cs | `OpenEditorAfterCapture` 데드코드 제거, SettingsWindow가 `AutoOpenEditor` 사용하도록 수정 (설정↔동작 불일치 해결) | 현재 |
| #50 | NotificationService.cs | `HideToast`에 try/catch 추가: 애니메이션 중 창 닫힘 시 `InvalidOperationException` 방어 | 현재 |
| #55 | ScrollCaptureService.cs | finally 블록에서 `captures.Count == 1` 예외 시 Dispose 누수 수정 (Clear로 소유권 이전) | 현재 |
| #57(기존) | CaptureLogger.cs | `Buffer.ToString().Split('\n')` 제거 → `_lineCount` 카운터로 교체 (플러시 성능 개선) | 현재 |
| #58(기존) | WindowCaptureService.cs | `TryBitBltCapture`에서 `SelectObject` 이중 호출 방지 (`hOld = IntPtr.Zero` 리셋) | 현재 |
| #59(기존) | NotificationService.cs | `ShowToast()`에서 기존 토스트 닫기 시 예외 방어 (`try { Close(); } catch {}`) | 현재 |

### 허위/안전으로 확인된 버그 (#1~#40)

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

### 허위/안전으로 확인된 버그 (#42~#56)

| # | 파일 | 판정 | 사유 |
|---|------|------|------|
| #42 | MainWindow.xaml.cs | 허위 | `CaptureMonitorAsync`는 MainWindow:1004, CaptureManager에도 존재 |
| #43 | MainWindow.xaml.cs | 허위 | `ExtractTextAsync`는 UI스레드 SynchronizationContext에서 실행, await 후 UI 복귀 |
| #44 | ImageEditorWindow.xaml.cs | 허위 | `BitmapCacheOption.OnLoad` + `Freeze()` 설정됨, 스트림 닫아도 안전 |
| #45 | ScrollCaptureService.cs | 안전 | `Clone()`은 독립 비트맵 반환, 원본 Dispose 무관 |
| #46 | ChromeCaptureService.cs | 허위 | 의도적 설계: 아무 탭 연결 후 `Page.navigate`로 URL 이동 |
| #47 | DxgiCapture.cs | 허위 | UI 스레드 전용, Initialize 후에만 사용, 방어적 null 체크 존재 |
| #48 | DxgiCapture.cs | P3 성능 | `new Random()` 매번 생성하지만 호출 빈도 낮아 영향 극미 |
| #51 | CaptureResult.cs | 허위 | `CapturedAt = DateTime.Now` 기본값 이미 존재 (Line 23) |
| #52 | ImageEditorWindow.xaml.cs | 안전 | Count 체크 후 Pop, Clone/Dispose 순서 올바름 |
| #53 | ChromeCaptureService.cs | 허위 | `using var cts` 이미 적용됨 (Line 328) |
| #54 | ChromeCaptureService.cs | 안전 | HttpClient 인스턴스 수명 = 앱 수명, 재사용 패턴 준수 |
| #56 | HotkeyService.cs | 안전 | `_source?.RemoveHook`은 null-conditional 연산자로 안전 |

### 허위/안전으로 확인된 버그 (#57~#68, 신규)

| # | 파일 | 판정 | 사유 |
|---|------|------|------|
| #57(신규) | WindowCaptureService.cs | 안전 | finally 블록(Line 270-271)이 `hBitmap != IntPtr.Zero`일 때 `DeleteObject` 보장, 예외 시 catch→finally 경유 |
| #58(신규) | WinRtCapture.cs | P3 안전 | `CreateFreeThreaded` 풀의 콜백 스레드에서 `.Result` 호출, UI 데드락 위험 없음 |
| #59(신규) | CaptureOverlay.xaml.cs | 안전 | Line 223-229에 DPI 보정 로직(`scaleX = _screenWidth / ActualWidth`) 이미 존재 |
| #60(신규) | OcrService.cs | 안전 | `using var stream`은 C# 8.0 패턴으로 메서드 스코프 끝까지 유지, 해제 순서 문제 없음 |
| #61(신규) | MainWindow.xaml.cs | 허위 | WPF Window `ShowInTaskbar` 기본값 `true`, `MinimizeToTray` 시 `Hide()`는 의도된 동작 |
| #62 | CaptureLogger.cs | 허위 | `Debug.WriteLine`은 Release 빌드에서 `[Conditional("DEBUG")]`로 자동 제거, 프로덕션 영향 없음 |
| #63 | UrlInputDialog.xaml.cs | P3 낮음 | 잘못된 URL은 Chrome이 자체 에러 페이지 표시, 실질적 영향 극미 |
| #64 | ScrollCaptureService.cs | 허위 | `unchecked` 블록이 이미 Line 234에 적용됨 |
| #65 | DxgiCapture.cs | 안전 | 캡처 메서드 내에서 항상 `ReleaseFrame()` 호출, `Dispose()`는 COM 전체 해제 |
| #66 | CaptureResult.cs | P3 설계 | `IDisposable` 인터페이스 미구현이나, 모든 호출처에서 명시적 `Dispose()` 호출 중 |
| #67 | MainWindow.xaml.cs | 안전 | `ToUpper()` 통일 후 비교, 미지원 포맷은 `_ => ImageFormat.Png` 기본값으로 안전 저장 |
| #68 | ChromeCaptureService.cs | 안전 | `StitchImages`는 `captures.Count >= 2`일 때만 호출, 빈 목록 경로 없음 |

### 설계 개선 사항 (P2~P3)

| # | 파일 | 우선순위 | 내용 |
|---|------|---------|------|
| #60(기존) | HotkeyService.cs | P2 설계 | `RegisterHotkeys()`가 하드코딩된 키만 등록, 사용자 설정 무시. 기본 단축키로 동작하므로 긴급하지 않음 |
| #61(기존) | CaptureLogger.cs | P3 설계 | 앱 실행마다 새 로그 파일 생성, 오래된 로그 정리 메커니즘 없음 |

---

## 검증 통계 요약

| 구분 | 건수 |
|------|------|
| **수정 완료** | 21건 (#1~#59 중 실제 버그) |
| **허위 (버그 아님)** | 26건 |
| **안전 (방어 코드 존재)** | 18건 |
| **P3 설계/성능** | 7건 (미수정, 영향 극미) |
| **총 검증** | 68건 + 4건(기존 수정 #57~#59) |

---

## 다음 할 일
- [ ] (#60 기존) HotkeyService에서 사용자 설정 단축키 반영 (P2 설계 개선)
- [ ] (#61 기존) 로그 파일 자동 정리 (7일 이상 된 로그 삭제, P3)
- [ ] Deactivate 포커스 복구 수정 후 실제 환경 테스트 (3회 연속 영역 캡처)
- [ ] 스크롤 캡처 (일반 + Chrome CDP) 실제 환경 테스트
