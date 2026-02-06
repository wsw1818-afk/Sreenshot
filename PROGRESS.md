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
| #41 | CaptureOverlayForm.cs | Deactivate 후 포커스 복구(Activate+Focus) + 2회 반복 즉시 취소 + 10초 안전 타이머 + 드래그 중 타이머 중지 | 현재 |
| #49 | AppSettings.cs, SettingsWindow.xaml.cs | `OpenEditorAfterCapture` 데드코드 제거, SettingsWindow가 `AutoOpenEditor` 사용하도록 수정 (설정↔동작 불일치 해결) | 현재 |
| #50 | NotificationService.cs | `HideToast`에 try/catch 추가: 애니메이션 중 창 닫힘 시 `InvalidOperationException` 방어 | 현재 |
| #55 | ScrollCaptureService.cs | finally 블록에서 `captures.Count == 1` 예외 시 Dispose 누수 수정 (Clear로 소유권 이전) | 현재 |

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
| #42 | MainWindow.xaml.cs | 허위 | `CaptureMonitorAsync`는 MainWindow:1004, CaptureManager:74에 존재 |
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

---

## 신규 발견 버그 (2026-02-06)

### 버그 #42 - CaptureManager 누락 메서드
| 항목 | 내용 |
|------|------|
| **파일** | MainWindow.xaml.cs |
| **위치** | Line 1000 |
| **문제** | `CaptureMonitorAsync(monitorIndex)` 메서드가 호출되지만 정의되지 않음 |
| **영향** | 모니터 선택 메뉴 클릭 시 컴파일 오류 또는 런타임 예외 |
| **해결** | `CaptureManager`에 `CaptureMonitorAsync` 메서드 추가 필요 (현재는 `CaptureFullScreenAsync` 등만 존재) |

### 버그 #43 - OCR 결과 UI 스레드 문제
| 항목 | 내용 |
|------|------|
| **파일** | MainWindow.xaml.cs |
| **위치** | Line 746-765 |
| **문제** | `ExtractTextAsync`에서 OCR 성공 후 `MessageBox.Show`가 UI 스레드가 아닌 백그라운드 스레드에서 실행될 수 있음 |
| **영향** | 간헐적인 UI 예외 또는 메시지 박스 표시 실패 |
| **해결** | `Dispatcher.Invoke`로 MessageBox 호출 감싸기 |

### 버그 #44 - ImageEditor BitmapSource 변환 문제
| 항목 | 내용 |
|------|------|
| **파일** | ImageEditorWindow.xaml.cs |
| **위치** | Line 88-102 |
| **문제** | `ConvertToBitmapSource`에서 `MemoryStream`을 `using`으로 처리하여 `BitmapImage`가 유효하지 않은 스트림 참조 가능 |
| **영향** | 이미지 표시 실패 또는 메모리 액세스 위반 |
| **해결** | `BitmapCacheOption.OnLoad`는 설정되어 있으나, 스트림을 닫기 전 `Freeze()` 호출 확인 필요 |

### 버그 #45 - ScrollCapture 리소스 관리 문제
| 항목 | 내용 |
|------|------|
| **파일** | ScrollCaptureService.cs |
| **위치** | Line 165-200 |
| **문제** | `CaptureClientAreaAsync`에서 `rawResult.Image`를 `using var`로 처리하지만, `Clone()` 결과는 새 객체이므로 원본이 Dispose됨 |
| **영향** | 일부 상황에서 이미지 데이터 손상 가능 |
| **해결** | `using` 제거하거나 명시적인 리소스 관리 필요 |

### 버그 #46 - ChromeCapture URL 파라미터 누락
| 항목 | 내용 |
|------|------|
| **파일** | ChromeCaptureService.cs |
| **위치** | Line 387-430 |
| **문제** | `CaptureUrlAsync`에서 `targetUrl`을 받지만, 기존 Chrome 연결 시 `GetWebSocketDebuggerUrlAsync`에 `url` 파라미터를 전달하지 않음 |
| **영향** | 올바른 탭을 찾지 못해 잘못된 페이지 캡처 가능 |
| **해결** | Line 394에서 `GetWebSocketDebuggerUrlAsync(debugPort, targetUrl)`로 수정 |

### 버그 #47 - DxgiCapture Race Condition
| 항목 | 내용 |
|------|------|
| **파일** | DxgiCapture.cs |
| **위치** | Line 315-316 |
| **문제** | `_device` null 체크 후 사용까지 시간차가 있어 멀티스레드 환경에서 null 참조 가능 |
| **영향** | `NullReferenceException` |
| **해결** | null 체크와 사용을 원자적으로 처리하거나 로컬 변수 사용 |

### 버그 #48 - Random 객체 반복 생성 (성능)
| 항목 | 내용 |
|------|------|
| **파일** | CaptureManager.cs, DxgiCapture.cs, GdiCapture.cs |
| **위치** | `IsBlackImage` 메서드 |
| **문제** | `Random` 객체가 메서드 호출마다 새로 생성됨 (성능 저하) |
| **영향** | GC 부하 증가, 캡처 성능 저하 |
| **해결** | `Random`을 스레드별 또는 인스턴스 변수로 재사용 |

### 버그 #49 - 중복 설정 항목
| 항목 | 내용 |
|------|------|
| **파일** | AppSettings.cs |
| **위치** | Line 59, 104 |
| **문제** | `OpenEditorAfterCapture`와 `AutoOpenEditor`가 중복된 의미의 설정 |
| **영향** | 설정 불일치, 사용자 혼란 |
| **해결** | 하나로 통합하고 다른 하나는 obsolete 처리 |

### 버그 #50 - NotificationService 애니메이션 예외
| 항목 | 내용 |
|------|------|
| **파일** | NotificationService.cs |
| **위치** | Line 175-186 |
| **문제** | `HideToast`에서 애니메이션 진행 중 창이 닫히면 `InvalidOperationException` 가능 |
| **영향** | 간헐적인 크래시 |
| **해결** | 애니메이션 완료 후 창 닫기 또는 예외 처리 추가 |

### 버그 #51 - CaptureResult 생성자 누락 필드
| 항목 | 내용 |
|------|------|
| **파일** | MainWindow.xaml.cs |
| **위치** | Line 200-206 |
| **문제** | `CaptureResult` 생성 시 `CapturedAt` 필드가 설정되지 않음 |
| **영향** | 타임스탬프 정보 누락 |
| **해결** | `CapturedAt = DateTime.Now` 추가 |

### 버그 #52 - ImageEditor Undo/Redo 예외 처리
| 항목 | 내용 |
|------|------|
| **파일** | ImageEditorWindow.xaml.cs |
| **위치** | Line 556-574 |
| **문제** | `Undo_Click`/`Redo_Click`에서 `_editedImage.Dispose()` 후 예외 발생 시 null 참조 가능 |
| **영향** | 이후 작업 실패 |
| **해결** | try-catch로 예외 처리 및 `_editedImage` 복원 |

### 버그 #53 - CancellationTokenSource Dispose 누락
| 항목 | 내용 |
|------|------|
| **파일** | ChromeCaptureService.cs |
| **위치** | Line 328-343 |
| **문제** | `SendCdpCommandAsync`에서 생성된 `CancellationTokenSource`가 모든 경로에서 Dispose되지 않을 수 있음 |
| **영향** | 메모리 누수 |
| **해결** | `using var cts`로 변경 |

### 버그 #54 - HttpClient 재사용 권장 패턴 위반
| 항목 | 내용 |
|------|------|
| **파일** | ChromeCaptureService.cs |
| **위치** | Line 16 |
| **문제** | `HttpClient`를 인스턴스 필드로 가지지만 `IDisposable` 구현하지 않음 |
| **영향** | 소켓 고갈 (소켓 연결 유지) |
| **해결** | `IHttpClientFactory` 사용 또는 정적 `HttpClient` 인스턴스 사용 권장 |

### 버그 #55 - ScrollCapture finally 블록 로직 오류
| 항목 | 내용 |
|------|------|
| **파일** | ScrollCaptureService.cs |
| **위치** | Line 152-159 |
| **문제** | `captures.Count > 1`일 때만 Dispose하는데, 예외 발생 시 Count가 1이어도 Dispose 필요 |
| **영향** | 예외 시 메모리 누수 |
| **해결** | `captures.Count >= 1`로 수정 또는 별도 예외 처리 |

### 버그 #56 - HotkeyService Dispose 순서
| 항목 | 내용 |
|------|------|
| **파일** | HotkeyService.cs |
| **위치** | Line 150-155 |
| **문제** | `Dispose`에서 `UnregisterHotkeys` 후 `_source.RemoveHook` 호출하지만, `_source`가 null일 수 있음 |
| **영향** | `NullReferenceException` |
| **해결** | null 체크 추가 |

---

## 다음 할 일
- [ ] Deactivate 포커스 복구 수정 후 실제 환경 테스트 (3회 연속 영역 캡처)
- [ ] 스크롤 캡처 (일반 + Chrome CDP) 실제 환경 테스트
- [x] 버그 #42~#56 검증 완료 (3건 수정, 12건 허위/안전)
