# Quick for Windows

macOS 네이티브 Quick의 Windows 버전 (C# / .NET). 맥 앱과 **별개 코드베이스**이되, 순수 로직은 공유 구조로 이식.

## 구조

```
windows/
  Quick.sln
  src/Quick.Core/    # 크로스플랫폼 순수 로직 (검색·버전비교·스샷감지) — 어디서나 빌드/테스트
  src/Quick.App/     # Windows 트레이 앱 (WinForms + Windows.Media.Ocr) — Windows 전용
  tests/Quick.Core.Tests/  # 코어 xUnit 테스트 (24개)
```

## 개발/검증

- **코어(어느 OS에서나):** `dotnet test tests/Quick.Core.Tests`
- **Windows 앱(Windows 필요):** `dotnet build Quick.sln -c Release` → `dotnet run --project src/Quick.App`
- CI: `core-tests`(ubuntu) + `windows-build`(windows-latest)

## 현재 상태

- ✅ `Quick.Core` — 검색되는 스크린샷 메모리의 순수 로직 이식 완료, 테스트 24개 통과(macOS에서 검증)
- 🚧 `Quick.App` — 트레이 상주 + 스샷 폴더 감시 + OCR 색인 + 백필 (차별화 기능의 Windows 구현). 검색 패널 UI·전역 단축키·드래그아웃은 다음 단계.

## macOS 대비 매핑

| 기능 | macOS (Swift) | Windows (C#) |
|------|------|------|
| OCR | Vision | `Windows.Media.Ocr` |
| 스샷 폴더 | screencapture.plist·Desktop | `Pictures\Screenshots` |
| 트레이 | NSStatusItem | WinForms `NotifyIcon` |
| 검색 로직 | Swift(공유 개념) | `Quick.Core`(공유 코드) |
