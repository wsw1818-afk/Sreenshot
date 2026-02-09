# PROGRESS.md - SmartCapture 개발 진행 상황

## 개요
- **프로젝트**: SmartCapture (스크린샷 캡처 도구)
- **플랫폼**: Windows 10/11 (.NET 8.0)
- **상태**: 코드 품질 개선 완료 + 성능 최적화
- **마지막 업데이트**: 2026-02-09

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
| #60(기존) | HotkeyService.cs, MainWindow.xaml.cs | `RegisterHotkeys(AppSettings?)` 오버로드 + `ResolveHotkey()` + `KeyNameToVk()` 추가, 사용자 설정 단축키 반영 | 현재 |
| #61(기존) | CaptureLogger.cs, CaptureManager.cs | `CleanupOldLogs(retentionDays=7)` 메서드 추가, 앱 시작 시 자동 호출 | 현재 |
| #66 | CaptureResult.cs | `IDisposable` 정식 구현: `_disposed` 플래그 + `GC.SuppressFinalize(this)` + 이중 Dispose 방지 | 현재 |
| - | ImageEditorWindow.xaml.cs | `ApplyMosaic` 성능 최적화: `GetPixel/SetPixel` → `LockBits` unsafe 포인터 접근 (~10-100x 속도 향상) | 현재 |
| - | ICaptureEngine.cs, CaptureManager.cs, DxgiCapture.cs | `IsBlackImage` 중복 코드 제거: `CaptureEngineBase.IsBlackImage` public static으로 통합 | 현재 |

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
| #66 | CaptureResult.cs | **수정됨** | `IDisposable` 정식 구현 완료: `_disposed` 플래그 + `GC.SuppressFinalize` |
| #67 | MainWindow.xaml.cs | 안전 | `ToUpper()` 통일 후 비교, 미지원 포맷은 `_ => ImageFormat.Png` 기본값으로 안전 저장 |
| #68 | ChromeCaptureService.cs | 안전 | `StitchImages`는 `captures.Count >= 2`일 때만 호출, 빈 목록 경로 없음 |

### 설계 개선 사항 (P2~P3)

| # | 파일 | 우선순위 | 내용 | 상태 |
|---|------|---------|------|------|
| #60(기존) | HotkeyService.cs | P2 설계 | `RegisterHotkeys(AppSettings?)` 오버로드로 사용자 설정 반영 | **완료** |
| #61(기존) | CaptureLogger.cs | P3 설계 | `CleanupOldLogs(7)` 메서드 추가, 앱 시작 시 자동 정리 | **완료** |

---

## 검증 통계 요약

| 구분 | 건수 |
|------|------|
| **수정 완료** | 34건 (#1~#75 중 실제 버그 + 설계 개선 + 성능 최적화) |
| **허위 (버그 아님)** | 26건 |
| **안전 (방어 코드 존재)** | 18건 |
| **P3 설계/성능** | 5건 (미수정, 영향 극미) |
| **총 검증** | 68건 + 4건(기존 수정 #57~#59) + 2건(추가 개선) |

---

## 다음 할 일
- [x] (#60 기존) HotkeyService에서 사용자 설정 단축키 반영 → **완료**
- [x] (#61 기존) 로그 파일 자동 정리 (7일 이상 된 로그 삭제) → **완료**
- [x] (#66) CaptureResult에 IDisposable 정식 구현 → **완료**
- [x] ImageEditorWindow 모자이크 LockBits 성능 최적화 → **완료**
- [x] IsBlackImage 중복 코드 CaptureEngineBase로 통합 → **완료**
- [ ] Deactivate 포커스 복구 수정 후 실제 환경 테스트 (3회 연속 영역 캡처)
- [ ] 스크롤 캡처 (일반 + Chrome CDP) 실제 환경 테스트

---

## 추가 코드리뷰 (2026-02-09, 인수인계용 상세)

### 분석 범위
- `Screenshot/MainWindow.xaml.cs`
- `Screenshot/Services/Capture/*.cs` (CaptureManager, DxgiCapture, GdiCapture, WinRtCapture, CaptureLogger)
- `Screenshot/Services/*.cs` (HotkeyService, ChromeCaptureService, ScrollCaptureService, NotificationService)
- `Screenshot/Views/SettingsWindow.xaml.cs`

### 실행/검증 결과
- `dotnet build Screenshot.sln -v minimal` 성공 (경고 0, 오류 0)
- `dotnet list Screenshot/Screenshot.csproj package --vulnerable` 취약 패키지 없음
- `dotnet list Screenshot/Screenshot.csproj package --outdated` 업데이트 가능 패키지 확인
  - `Hardcodet.NotifyIcon.Wpf` `1.1.0 -> 2.0.1`
  - `Microsoft.Windows.CsWinRT` `2.0.8 -> 2.2.0`
  - `System.Text.Json` `8.0.5 -> 10.0.2`

### 신규 확정 이슈 (#69~#75)

| # | 우선순위 | 파일 | 핵심 문제 | 상태 |
|---|---|---|---|---|
| #69 | P1 | `Screenshot/Services/Capture/CaptureManager.cs` | Window Capture 경로가 실제로는 Full Screen Capture를 먼저 반환함 | **수정됨** - WindowCaptureService(PrintWindow) 우선 사용 |
| #70 | P1 | `Screenshot/Services/HotkeyService.cs` | 단축키 일부만 등록되어도 실패로 표시되고, 이미 등록된 일부 단축키는 남아 상태 불일치 발생 | **수정됨** - all-or-nothing 롤백 패턴 |
| #71 | P2 | `Screenshot/Services/HotkeyService.cs` | UI에서 입력 가능한 키와 실제 등록 가능한 키 매핑 불일치(설정 무시/기본값 fallback) | **수정됨** - NumPad/OEM/방향키/특수키 전체 매핑 |
| #72 | P2 | `Screenshot/Services/Capture/DxgiCapture.cs` | 다중 모니터(음수 VirtualScreen 좌표)에서 DXGI 영역 캡처 좌표계 불일치 | **수정됨** - VirtualScreen 기준 좌표 정규화 |
| #73 | P2 | `Screenshot/MainWindow.xaml.cs` | AutoSave 파일명 충돌 처리 누락(초 단위 파일명)으로 덮어쓰기 가능 | **수정됨** - while(File.Exists) 카운터 추가 |
| #74 | P2 | `Screenshot/Services/ChromeCaptureService.cs` | URL 지정 캡처 시 탭 선택 로직이 느슨해 다른 탭 캡처 가능성 | **수정됨** - Uri 기반 정확 매칭 + targetUrl 전달 |
| #75 | P3 | `Screenshot/Services/Capture/CaptureManager.cs`, `Screenshot/MainWindow.xaml.cs` | 설정값 `webp`일 때 확장자는 `.webp`인데 실제 저장 포맷은 PNG | **수정됨** - webp→PNG 대체 + 확장자 일치 |

### #69 Window Capture가 Full Screen으로 동작하는 문제
- 근거 코드
- `Screenshot/Services/Capture/CaptureManager.cs:107` `ExecuteCapture(e => e.CaptureActiveWindow(), "Window")`
- `Screenshot/Services/Capture/DxgiCapture.cs:239` `CaptureActiveWindow() => CaptureFullScreen()`
- `Screenshot/Services/Capture/GdiCapture.cs:218` `CaptureActiveWindow() => CaptureFullScreen()`
- 영향
- "창 캡처" 버튼이 사용자 기대(전경 창 영역)와 다르게 전체 화면을 저장할 수 있음
- `WindowCaptureService` fallback 로직이 성공 경로에서 실행되지 않을 수 있음
- 재현
1. 앱 실행 후 작은 창 하나만 전경으로 둠
2. "창 캡처" 실행
3. 결과 이미지가 창만이 아니라 전체 화면인지 확인
- 수정 가이드
1. `CaptureManager.CaptureWindowAsync`에서 `ExecuteCapture` 경로를 제거하거나, 실제 `hWnd` 기반 캡처 인터페이스를 추가
2. `ICaptureEngine`에 `CaptureWindow(IntPtr hWnd)`를 도입하거나 `WindowCaptureService`를 우선 경로로 사용
3. 성공 기준에서 캡처 영역이 전경 창 bounds와 일치하는지 검증 로직 추가

### #70 단축키 부분 등록 상태 불일치
- 근거 코드
- `Screenshot/Services/HotkeyService.cs:71-96` 일부 등록 성공 후 `registeredKeys.Count == 4`가 아니면 `false` 반환
- 예외 케이스에서만 rollback 수행(`catch` 블록)
- 영향
- UI/상태는 "등록 실패"인데 특정 단축키는 실제로 동작
- 재설정/해제 시 사용자 혼란 및 디버깅 난이도 상승
- 재현
1. 하나의 단축키를 OS/다른 앱과 충돌 나도록 설정
2. 등록 결과가 실패로 표시되는지 확인
3. 충돌 없는 나머지 단축키가 여전히 동작하는지 확인
- 수정 가이드
1. `registeredKeys.Count != 4`이면 즉시 rollback(`UnregisterHotKey` all registered)
2. 실패 원인(어떤 ID 등록 실패인지) 로그/UI 노출
3. 등록 API를 "all-or-nothing"으로 고정

### #71 설정 UI-엔진 키매핑 불일치
- 근거 코드
- `Screenshot/Views/SettingsWindow.xaml.cs:326-337` Numpad/OEM 키 문자열 생성
- `Screenshot/Services/HotkeyService.cs:131-156` `KeyNameToVk`는 A-Z/F1-F12/0-9/PrintScreen만 지원
- 영향
- 사용자 입력 단축키가 저장되지만 재등록 시 silently fallback(기본값 사용)
- "설정 저장했는데 반영 안 됨" 형태의 장애로 보임
- 수정 가이드
1. `KeyNameToVk`에 `NumPad0~9`, `Oem*`, 방향키 등 UI 허용 키 전부 매핑
2. 매핑 실패 시 fallback 대신 저장 차단 + 경고 메시지 제공
3. 저장 시점에 hotkey validation 수행

### #72 DXGI 다중 모니터 음수 좌표 처리 오류
- 근거 코드
- `Screenshot/Services/Capture/DxgiCapture.cs:247-253` VirtualScreen 전체 요청 시 합성 비트맵 생성
- `Screenshot/Services/Capture/DxgiCapture.cs:273-274` `region.X < 0 || region.Y < 0`이면 실패 처리
- 영향
- 왼쪽/위쪽 보조 모니터가 있는 환경(virtual origin 음수)에서 DXGI raw 영역 캡처 실패
- 결과적으로 GDI fallback 비중 증가(성능/품질 저하)
- 재현
1. 보조 모니터를 주 모니터 왼쪽에 배치(virtual X < 0)
2. 영역 캡처 진입(내부적으로 `CaptureFullScreenRawAsync` 사용)
3. 로그에서 DXGI region out-of-range 확인 후 GDI fallback 여부 확인
- 수정 가이드
1. `CaptureVirtualScreen` 결과를 사용할 때 `region`을 virtual origin 기준으로 normalize
2. 예: `normalizedX = region.X - virtualScreen.X`, `normalizedY = region.Y - virtualScreen.Y`
3. bounds check를 normalized 좌표 기준으로 변경

### #73 AutoSave 파일 덮어쓰기 가능
- 근거 코드
- `Screenshot/MainWindow.xaml.cs:453-470` 파일명 `capture_yyyyMMdd_HHmmss` 생성 후 `Save` 수행
- 동일 파일 존재 여부/카운터 증가 로직 없음
- 영향
- 같은 초에 연속 캡처 시 이전 파일이 덮어써질 수 있음
- 특히 `CaptureRegionAsync`, `CaptureScrollAsync`, `CaptureWithChromeCdpAsync` 경로는 MainWindow에서 직접 저장
- 수정 가이드
1. `CaptureManager.SaveToFile`와 동일하게 collision-safe naming 적용
2. 최소 `while (File.Exists(path))` + suffix 증가 로직 추가
3. 저장 경로 생성을 공용 유틸로 통합하여 중복 제거

### #74 URL 캡처 탭 선택 오탐 가능성 (코드 추론 기반)
- 근거 코드
- `Screenshot/Services/ChromeCaptureService.cs:192` `GetWebSocketDebuggerUrlAsync(int, string? targetUrl = null)`
- `Screenshot/Services/ChromeCaptureService.cs:212` `tabUrlString.Contains(targetUrlBase)`로 느슨한 매칭
- `Screenshot/Services/ChromeCaptureService.cs:423` `CaptureFullScrollablePageAsync(debugPort)` 호출 시 targetUrl 미전달
- 영향
- 다중 탭 환경에서 의도한 URL이 아닌 탭을 캡처할 수 있음
- 수정 가이드
1. URL 매칭을 `Uri` 기반 host/path strict match로 변경
2. `CaptureUrlAsync` 이후 후속 호출에도 targetUrl을 끝까지 전달
3. 선택된 탭의 URL을 상태 로그에 출력해 검증 가능성 강화

### #75 `webp` 확장자/실제 포맷 불일치
- 근거 코드
- `Screenshot/Services/Capture/CaptureManager.cs:379-383` 확장자 `.webp` 허용
- `Screenshot/Services/Capture/CaptureManager.cs:397-401` 저장 포맷은 PNG/JPEG/BMP만 지원, 나머지는 PNG
- 영향
- `.webp` 파일 확장자지만 실제 파일 헤더는 PNG일 수 있음
- 수정 가이드
1. 실제 WebP 인코더 도입 전까지 `webp` 옵션 제거/차단
2. `AppSettings.Load()` 시 unsupported format sanitize
3. 저장 후 magic number 검사(선택)

### 후속 작업 우선순위 제안 (다른 AI 작업 순서)
1. #69 수정 (기능 정합성 핵심)
2. #70 + #71 묶음 수정 (단축키 안정화)
3. #73 수정 (데이터 유실 방지)
4. #72 수정 (다중 모니터 안정성)
5. #74, #75 수정 (정확도/설정 일관성)

### 다음 세션 체크리스트 (인수인계용)
- [x] #69 수정 완료 - WindowCaptureService(PrintWindow) 우선 사용, 실패 시 엔진 fallback
- [x] #70 수정 완료 - all-or-nothing 롤백 패턴 적용
- [x] #71 수정 완료 - NumPad/OEM/방향키/특수키 전체 매핑 추가
- [x] #72 수정 완료 - VirtualScreen.X/Y 기준 좌표 정규화
- [x] #73 수정 완료 - while(File.Exists) 카운터 추가
- [x] #74 수정 완료 - Uri.GetLeftPart(UriPartial.Path) 정확 매칭 + targetUrl 전달
- [x] #75 수정 완료 - webp 선택 시 PNG로 대체 + 확장자도 .png
- [ ] 실제 환경 검증: 창 캡처, 단축키 등록, 다중 모니터, 연속 캡처, URL 캡처
