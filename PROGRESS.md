# PROGRESS.md (현재 진행: 얇게 유지)

## Dashboard
- Progress: 100%
- Token/Cost 추정: 낮음
- Risk: 낮음

## Today Goal
- 버그 분석 및 수정 ✅

## What changed
- MainWindow.xaml.cs, ChromeCaptureService.cs, ScrollCaptureService.cs, HotkeyService.cs 버그 수정 완료
- CaptureOverlay.xaml.cs DPI 스케일링 버그 수정 완료
- Critical 버그 4개, Medium 버그 2개 모두 수정됨

## Commands & Results
- 빌드 테스트 완료: 경고 0개, 오류 0개

## Fixed issues

### ✅ Critical (모두 수정됨)

1. **중복 Dispose 오류** - MainWindow.xaml.cs
   - ✅ 수정: Dispose를 if/else 밖으로 이동, cropWidth/cropHeight 유효성 검사 추가

2. **null-forgiving 연산자 위험** - ChromeCaptureService.cs
   - ✅ 수정: null 체크 후 Contains 호출하도록 변경

3. **음수 높이 계산** - ScrollCaptureService.cs:StitchImages
   - ✅ 수정: actualOverlap 상한 제한, totalHeight 최소값 보장, currentY 음수 방지

4. **GetPixel 인덱스 초과** - ScrollCaptureService.cs:FindOverlap/CompareRegions
   - ✅ 수정: 모든 인덱스 접근 전 경계 검사 추가

### ✅ Medium (모두 수정됨)

5. **핫키 부분 등록 문제** - HotkeyService.cs
   - ✅ 수정: 등록된 핫키 추적, 예외 시 롤백 로직 추가

6. **null 체크 부재** - MainWindow.xaml.cs:AddThumbnail
   - ✅ 수정: result.Image null 체크 추가, null-forgiving 연산자 제거

### ✅ 추가 수정 (오늘)

7. **DPI 스케일링 영역 캡처 버그** - CaptureOverlay.xaml.cs
   - ✅ 수정: WPF 좌표계와 물리적 픽셀 좌표계 분리
   - 창/이미지/마스크 모두 WPF 좌표계로 통일
   - 이미지 캡처 시 물리적 좌표로 변환

8. **커스텀 십자선 커서** - CaptureOverlay.xaml
   - ✅ 추가: 흰색+검은 테두리 십자선으로 가시성 개선

## Open issues
- 없음

## Next
- 배포 및 테스트
