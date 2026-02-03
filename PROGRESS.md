# PROGRESS.md (현재 진행: 얇게 유지)

## Dashboard
- Progress: 95%
- Token/Cost 추정: 낮음
- Risk: 낮음

## Today Goal
- 캡처 엔진 안정화 및 코드 정리 완료

## Current Status

### ✅ 완료된 작업

1. **DxgiCapture 캐싱 시스템 개선**
   - IsAvailable 30초 캐시 구현
   - Desktop Duplication 세션 유효성 체크
   - 세션 만료시 자동 재초기화
   - ~~unused `_retryCount` 필드~~ → 제거 완료

2. **GDI Capture 강화**
   - BitBlt + CAPTUREBLT 플래그 구현
   - CopyFromScreen 폴백 추가
   - 검은 화면 자동 재시도

3. **CaptureManager 통합**
   - 모든 캡처 모드(FullScreen, Region, Monitor, ActiveWindow, Window) 통합
   - _lastSuccessfulEngine 로깅 강화
   - CaptureWindowAsync 추가 (DXGI → GDI → PrintWindow 순)

4. **영역 캡처 (CaptureOverlay) - DPI 스케일링 수정**
   - WPF 좌표계와 물리적 좌표계 분리
   - `_wpfScreenWidth`, `_wpfScreenHeight`로 WPF 좌표 크기 계산
   - WPF→물리적 좌표 변환으로 정확한 영역 캡처
   - 십자선 커서 추가 (가시성 향상)

5. **코드 품질 개선 (Critical + Medium 버그 수정)**
   - MainWindow.xaml.cs: Dispose 중복 호출 방지
   - ChromeCaptureService.cs: null-forgiving 연산자 제거
   - ScrollCaptureService.cs: 음수 height 방지, GetPixel 경계 검사
   - HotkeyService.cs: 핫키 등록 실패 시 롤백
   - MainWindow.xaml.cs:AddThumbnail: null 체크 추가

6. **코드 정리 완료**
   - ~~MainWindow.CaptureScreenDirect()~~ → 제거, CaptureOverlay.CaptureScreen() 사용
   - ~~DxgiCapture._retryCount~~ → 제거 (unused warning 해결)
   - 중복 코드 제거 완료

### 📋 남은 작업

7. **전체 기능 테스트 (선택)**
   - [ ] 전체 화면 캡처 (DXGI 캐싱 확인)
   - [ ] 영역 선택 캡처 (DPI 스케일링 검증)
   - [ ] 창 캡처 (PrintWindow 폴백)
   - [ ] 모니터 캡처
   - [ ] 스크롤 캡처

## Known Issues

| Issue | Status | Description |
|-------|--------|-------------|
| ~~CaptureOverlay.CaptureScreen 미작동~~ | ✅ 해결 | 중복 코드 제거 후 정상 작동 |
| ~~_retryCount unused warning~~ | ✅ 해결 | 필드 제거 완료 |

## Build Status
- Debug: ✅ 성공 (경고 0개)
- Release: ✅ 성공 (경고 0개)

## Files Modified (이번 세션)
- Screenshot/Services/Capture/DxgiCapture.cs (_retryCount 제거)
- Screenshot/MainWindow.xaml.cs (CaptureScreenDirect 중복 코드 제거)

## Next Steps
1. 배포 빌드 및 결과물 폴더 복사
2. Git 커밋
