# MEMORY.md (SSOT: 규칙/기술 스택/제약)

## 1) Goal / Scope (정적)
- 목표: SmartCapture - Windows 화면 캡처 도구
- 범위: 전체화면/영역/창/모니터 캡처, 자동저장, 클립보드
- Non-goals: 동영상 캡처, 리눅스/맥 지원

## 2) Tech Stack (정적, 캐시 최적화)
- Framework: .NET 8.0, WPF + WinForms 하이브리드
- Language: C# 12
- Build/CI: dotnet publish -c Release -o publish
- Target platforms: Windows 10 19041+ / Windows 11, win-x64

## 3) Constraints (가끔 변함)
- OS: Windows 11 24H2 (Build 26200) - 사용자 환경
- DPI: 125% (120 DPI), PerMonitorV2
- **CopyFromScreen 사용 금지**: Win11 24H2에서 항상 검은 화면(A=0) 반환 → DXGI 사용 필수
- 배포: `D:\OneDrive\코드작업\결과물\Screenshot\SmartCapture.exe`

## 4) Coding Rules (정적)
- 최소 diff 원칙
- 테스트/수정 루프(최대 3회): lint/typecheck/test 우선
- 비밀정보 금지: 값 금지(변수명/위치만)
- 큰 변경(프레임워크/DB/상태관리 교체)은 사용자 1회 확인 후 진행

## 5) Architecture Notes (가끔 변함)
- 캡처 엔진: DXGI Hardware(P1) → GDI Capture(P10) 순서
- 영역 캡처: DXGI로 전체화면 캡처 → WinForms 오버레이(CaptureOverlayForm)에서 영역 선택 → Crop
- WPF 오버레이(CaptureOverlay.xaml)는 더 이상 사용 안 함
- 주요 파일:
  - `MainWindow.xaml.cs` - UI + 캡처 흐름 제어
  - `CaptureOverlayForm.cs` - WinForms 영역 선택 오버레이
  - `Services/Capture/CaptureManager.cs` - 캡처 엔진 관리
- 로그: `%LOCALAPPDATA%\SmartCapture\Logs\`

## 6) Testing / Release Rules (정적)
- dotnet build 성공 확인
- 영역 캡처/전체 캡처 수동 테스트
- 릴리즈: dotnet publish → 배포 폴더 복사
