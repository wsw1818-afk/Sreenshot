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
| CopyFromScreen | ❌ | 영역 선택에서 제거 (검은 화면 문제) |
| 캡처 엔진 캐싱 | ✅ | 30초 캐시, 세션 재사용 |

### 캡처 모드
| 모드 | 상태 | 비고 |
|------|------|------|
| 전체 화면 캡처 | ✅ | DXGI 사용, 정상 작동 |
| 영역 선택 캡처 | ✅ | DXGI 엔진 + WinForms 오버레이 |
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

## 🚨 2026-02-04 - 듀얼 모니터 영역 캡처 검은 화면 문제 분석

### 문제 증상
- **환경**: 듀얼 모니터 (Monitor 0: 1920x1200, Monitor 1: 2400x1350 @ 2400,0)
- **증상**: 영역 캡처 버튼 클릭 시 캡처 화면이 검게 나옴
- **빈도**: 지속적 발생

### 로그 분석
```
[16:46:07.796] 창 숨기기
[16:46:08.320] 창 숨김 완료  ← 524ms (약 500ms 딜레이 적용됨)
[16:46:08.349] 캡처 성공: 4800x1350  ← 비트맵 생성은 됨
```

### 적용된 수정사항
```csharp
// 500ms 딜레이 + DwmFlush() 추가
await Task.Delay(500);
DwmFlush(); // DWM 동기화
```

### 분석 결과
| 항목 | 상태 | 비고 |
|------|------|------|
| 딜레이 500ms | ✅ 적용됨 | 로그에서 524ms 확인 |
| DwmFlush() | ✅ 적용됨 | 코드상 존재 |
| 검은 화면 | ❌ 여전히 발생 | 실제 캡처 결과 검음 |

### 원인 추정
1. **CopyFromScreen 한계**: `Graphics.CopyFromScreen()`은 DWM이 완전히 화면을 갱신하기 전에 호출되면 검은 화면을 반환
2. **DwmFlush() 타이밍**: DwmFlush는 DWM 출력이 완료될 때까지 기다리지만, 창 숨김 후 DWM이 실제 화면을 갱신하는 데는 더 많은 시간이 필요
3. **검은 화면 검사 누락**: `CaptureScreenForRegion()`에서는 검은 화면 감사를 건 너뜀 (주석: "검은 화면 검사 없음")
4. **듀얼 모니터 복합 복잡도**: 두 모니터의 화면 복합(Composition)이 동시에 처리되어 타이밍 문제 발생 가능

### 해결 방안 (후보)

#### 방안 1: 딜레이 추가 증가 (즉시 시도 가능)
```csharp
await Task.Delay(800); // 500ms → 800ms
DwmFlush();
await Task.Delay(100); // 추가 버퍼
```

#### 방안 2: 검은 화면 감지 + 재시도 (권장)
```csharp
// CopyFromScreen 후 검은 화면 체크
if (IsBlackImage(bitmap))
{
    await Task.Delay(200);
    bitmap = CaptureScreenWithCopyFromScreen(); // 재시도
}
```

#### 방안 3: DXGI 멀티모니터 캡처 (근본적 해결)
- 각 모니터별로 DXGI Desktop Duplication 사용
- 두 비트맵을 가로로 합쳐서 가상화면 생성
- 단, 구현 복잡도 증가

#### 방안 4: 창 숨김 방식 변경
- `Hide()` 대신 `Opacity = 0.01` 유지 + 화면 밖 이동
- 2026-02-03 수정사항에서 이미 검증된 방식
- 사용자 눈에는 보이지 않으면서 DWM은 정상 동작

### 최종 해결: 방안 3 (DXGI) 적용 ✅
- **적용일**: 2026-02-05
- **방법**: `CopyFromScreen` 완전 제거, `CaptureManager.CaptureFullScreenRawAsync()` (DXGI 엔진) 사용
- **결과**: 3회 연속 테스트 성공, 검은 화면 없음

---

## 🚨 2026-02-05 - 영역 캡처 검은 화면 문제 최종 해결

### 문제 요약
영역 캡처 시 오버레이 배경이 검은 화면으로 표시되는 문제.
2세션에 걸쳐 총 10가지 이상의 접근법을 시도한 후 최종 해결.

### 근본 원인
**`Graphics.CopyFromScreen()`이 Windows 11 24H2 (Build 26200) 환경에서 항상 검은 화면(Alpha=0 포함)을 반환.**

```
캡처픽셀(480,300): R=0 G=0 B=0 A=0
캡처픽셀(960,600): R=0 G=0 B=0 A=0
BlackPixels=5/5
```

- `Hide()` + 200ms~800ms 딜레이: 효과 없음
- `Opacity=0.01` + 화면 밖 이동 + DwmFlush(): 효과 없음
- 3회 재시도: 효과 없음 (모든 시도에서 동일하게 검은 화면)
- **CopyFromScreen 자체가 이 환경에서 작동하지 않음**

### 시도한 접근법 (시간순)

| # | 접근법 | 결과 |
|---|--------|------|
| 1 | WPF Image + BitmapSource | ❌ 검은 화면 |
| 2 | WPF WriteableBitmap | ❌ 검은 화면 |
| 3 | WPF DrawingVisual | ❌ 검은 화면 |
| 4 | WinForms BackgroundImage | ❌ 회색 화면 |
| 5 | WinForms Paint + DrawImage | ❌ 회색 화면 |
| 6 | WinForms Paint + DrawImageUnscaled + SetResolution(96) | ❌ 회색 화면 |
| 7 | WinForms PictureBox + 투명 SelectionPanel | ❌ 검은 화면 |
| 8 | WinForms 단일 Form Paint + 임시파일 로드 | ❌ 검은 화면 |
| 9 | Opacity=0.01 + 화면 밖 이동 + DwmFlush + 3회 재시도 | ❌ 검은 화면 |
| **10** | **DXGI Desktop Duplication API** | **✅ 성공** |

### 핵심 진단 과정

1. **픽셀 샘플링 추가** → CopyFromScreen이 R=0 G=0 B=0 A=0 반환 확인
2. **Alpha=0** 발견 → 일반적 CopyFromScreen은 A=255 반환해야 함 → DWM 컴포지션 문제 확정
3. **전체 화면 캡처(DXGI)는 정상** → DXGI 엔진을 영역 캡처에도 적용

### 최종 해결책

```csharp
// Before: CopyFromScreen (검은 화면)
var capturedScreen = CaptureOverlayForm.CaptureScreen(); // GDI CopyFromScreen

// After: DXGI Desktop Duplication API (정상)
var rawResult = await _captureManager.CaptureFullScreenRawAsync(); // DXGI 엔진
var capturedScreen = rawResult.Image;
```

**변경 파일:**
- `MainWindow.xaml.cs` → `CaptureRegionAsync()`: CopyFromScreen → DXGI 엔진
- `CaptureOverlayForm.cs` → 단일 Form Paint 방식으로 간소화 (임시파일 로드 + 더블버퍼링)

### 교훈
1. **CopyFromScreen은 Windows 11 24H2+에서 신뢰할 수 없음** → DXGI 사용 권장
2. **증상(오버레이 안 보임)과 원인(캡처 자체가 검은 화면)이 다를 수 있음** → 픽셀 샘플링으로 진단 필수
3. **Alpha=0은 DWM 컴포지션 문제의 강한 신호** → 타이밍 조정으로 해결 불가, API 교체 필요

---

**마지막 업데이트**: 2026-02-05
