# PROGRESS.md - SmartCapture 개발 진행 상황

## 📊 개요
- **프로젝트**: SmartCapture (스크린샷 캡처 도구)
- **플랫폼**: Windows 10/11 (.NET 8.0)
- **상태**: 테스트/디버깅 중

---

## ✅ 완료된 기능

### 캡처 엔진
| 기능 | 상태 | 비고 |
|------|------|------|
| DXGI Hardware 캡처 | ✅ | Desktop Duplication API 사용 |
| GDI Capture (BitBlt) | ✅ | 폭백용 |
| CopyFromScreen | ✅ | 영역 선택용 |
| 캡처 엔진 캐싱 | ✅ | 30초 캐시, 세션 재사용 |

### 캡처 모드
| 모드 | 상태 | 비고 |
|------|------|------|
| 전체 화면 캡처 | ✅ | DXGI 사용, 정상 작동 |
| 영역 선택 캡처 | ✅ | Opacity 0.01 + 화면 밖 이동 |
| 창 캡처 | ✅ | PrintWindow 폭백 |
| 모니터 캡처 | ✅ | |

### 저장 기능
| 기능 | 상태 | 비고 |
|------|------|------|
| 자동 저장 | ✅ | 설정 가능 |
| 클립보드 복사 | ✅ | |
| 날짜별 폭더 정리 | ✅ | |
| PNG/JPG/BMP 지원 | ✅ | |

---

## 🔧 주요 수정사항 (최근)

### 2026-02-03

#### 1. 영역 선택 캡처 검은 화면 문제 해결
**문제**: 영역 선택 캡처 시 검은 화면이 간헐적으로 발생

**원인**: 
- DWM이 창을 완전히 숨기기 전에 캡처 시도
- `Opacity = 0.01`만으로는 DWM 캐시에 잔여 화면이 남아있을 수 있음

**해결책**:
```csharp
// Before: 투명도 + 위치 이동만 사용
Opacity = 0.01;
Left = -5000;
Top = -5000;
await Task.Delay(500);

// After: Hide() + Minimized + 더 긴 대기 시간
Hide();
WindowState = WindowState.Minimized;
Opacity = 0;
Left = -10000;
Top = -10000;
await Task.Delay(800);  // DWM 완전 적용 시간 확보
```

**추가 개선**: `CaptureScreenDirect()`에 검은 화면 감지 및 3회 재시도 로직 추가

**코드 위치**: `MainWindow.xaml.cs` - `CaptureRegionAsync()`, `CaptureScreenDirect()`

#### 2. 이전 수정사항
- `Visibility = Collapsed` 상태에서 `CopyFromScreen`이 검은 화면 반환 → `Opacity + 위치 이동`으로 해결
- DXGI 세션 충돌 방지 → 영역 선택에서는 `CopyFromScreen`만 사용
- 저장 로깅 강화

---

## ⚠️ 알려진 이슈

### 1. DXGI 세션 만료
- 증상: 연속 캡처 시 `IsAvailable=False`, 이후 GDI로 폭백
- 원인: Desktop Duplication 세션의 수명 제한
- 우회: GDI로 자동 폭백

### 2. CopyFromScreen 제한
- `Visibility=Collapsed` 또는 `Opacity=0` 상태에서 검은 화면
- `Opacity=0.01` + 화면 밖 이동으로 해결

### 3. DRM 콘텐츠
- Netflix, Disney+ 등: HDCP로 인해 검은 화면
- 이는 우회 불가능 (하드웨어 레벨 보호)

---

## 📁 파일 위치

### 실행 파일
- **Debug**: `Screenshot\bin\Debug\net8.0-windows10.0.19041.0\win-x64\`
- **Release**: `publish_final\` 또는 `D:\Onedrive\코드작업\결과물\SmartCapture\`

### 로그 파일
- 위치: `%LOCALAPPDATA%\SmartCapture\Logs\`
- 파일명: `capture_YYYYMMDD_HHmmss.log`

### 설정 파일
- 위치: `%APPDATA%\SmartCapture\settings.json`

---

## 🔜 다음 작업

1. **Release 빌드 최적화**
   - 디버그 로그 제거 또는 조걸 컴파일
   - 단일 파일 게시 최적화

2. **설치 프로그램**
   - .NET 8.0 런타임 포함 여부 결정
   - 자동 업데이트 기능

3. **추가 기능**
   - 스크롤 캡처 안정화
   - OCR 기능 개선
   - 편집 기능 강화

---

## 📝 디버깅 팁

### 로그 확인
```powershell
# 최신 로그 보기
Get-Content "$env:LOCALAPPDATA\SmartCapture\Logs\$(Get-ChildItem $env:LOCALAPPDATA\SmartCapture\Logs | Sort-Object LastWriteTime -Descending | Select-Object -First 1).Name" -Tail 50
```

### 설정 초기화
```powershell
Remove-Item "$env:APPDATA\SmartCapture\settings.json"
```

---

**마지막 업데이트**: 2026-02-03
