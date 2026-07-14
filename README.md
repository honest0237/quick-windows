# Quick for Windows

macOS 네이티브 Quick의 Windows 버전 (C# / .NET). 맥 앱과 **별개 코드베이스**이되, 순수 로직은 공유 구조로 이식.

## 구조

```
windows/
  Quick.slnx
  src/Quick.Core/    # 크로스플랫폼 순수 로직 (검색·버전비교·스샷감지) — 어디서나 빌드/테스트
  src/Quick.App/     # Windows 트레이 앱 (WinForms + Windows.Media.Ocr) — Windows 전용
  tests/Quick.Core.Tests/  # 코어 xUnit 테스트 (24개)
```

## 개발/검증

- **코어(어느 OS에서나):** `dotnet test tests/Quick.Core.Tests`
- **Windows 앱(Windows 필요):** `dotnet build Quick.slnx -c Release` → `dotnet run --project src/Quick.App`
- CI: `core-tests`(ubuntu) + `windows-build`(windows-latest)

## 현재 상태

- ✅ `Quick.Core` — 검색되는 스크린샷 메모리의 순수 로직 이식 완료, 테스트 24개 통과(macOS에서 검증)
- ✅ `Quick.App` — 트레이 상주 + 스샷 폴더 감시 + OCR 색인 + 백필 + 직접 캡처(영역/전체) + 사이드 검색패널 + 설정창 + 첫실행 안내 + **원클릭 자동 업데이트**

## 자동 업데이트 (v0.3.2+)

트레이 알림/메뉴의 **"⬆ 업데이트 설치"** 클릭 한 번으로 새 버전을 받아 스스로 교체·재시작한다.

1. 시작 시 GitHub Releases를 조회해 더 높은 버전이 있으면 트레이 알림
2. 클릭 → 다운로드(진행률·취소) → **무결성 검증**(PE `MZ` 헤더 + `Quick.exe.sha256` SHA-256)
3. 실행 중 exe는 자기 자신을 덮어쓸 수 없으므로, 앱 종료를 기다렸다 교체·재실행하는 `.cmd` 헬퍼를 분리 실행
4. 재시작 후 버전이 실제로 올랐는지 확인 → 실패 시 사용자에게 알림(조용한 실패 방지)

설치 폴더에 쓰기 권한이 없으면(예: `Program Files`) 자동 교체 대신 브라우저 다운로드로 폴백한다.
자산이 서명(Authenticode)되어 있지 않아 SmartScreen 경고가 뜰 수 있다(추가 정보 → 실행).

## 릴리스

CI(`windows-build`)가 만든 단일 `Quick.exe`를 SHA-256과 함께 배포한다.

```bash
# csproj <Version>을 올리고 push → CI 통과 후:
scripts/release.sh <version> [run_id] [--stable]
#   예) scripts/release.sh 0.3.3
#   run_id 생략 시 main의 최신 성공 CI에서 아티팩트를 받음
#   자동 업데이트가 무결성을 검증하도록 Quick.exe 와 Quick.exe.sha256 을 함께 업로드
```

## macOS 대비 매핑

| 기능 | macOS (Swift) | Windows (C#) |
|------|------|------|
| OCR | Vision | `Windows.Media.Ocr` |
| 스샷 폴더 | screencapture.plist·Desktop | `Pictures\Screenshots` |
| 트레이 | NSStatusItem | WinForms `NotifyIcon` |
| 검색 로직 | Swift(공유 개념) | `Quick.Core`(공유 코드) |
