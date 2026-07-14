namespace Quick.Core;

/// <summary>스크린샷 파일 판별(로케일 무관). Swift FileWatcherService.matchesScreenshot 이식.</summary>
public static class ScreenshotName
{
    private static readonly string[] Extensions = { "png", "jpg", "jpeg", "tiff", "heic", "bmp" };

    private static readonly string[] Patterns =
    {
        "스크린샷", "화면 기록",                          // 한국어
        "screenshot", "screen shot", "cleanshot",        // 영어
        "スクリーンショット", "スクリーン",                 // 일본어
        "截屏", "截图", "屏幕快照", "螢幕截圖", "截圖",      // 중국어
        "bildschirmfoto",                                // 독일어
        "capture", "capture d",                          // 프랑스어/일반
        "captura",                                       // 스페인어/포르투갈어
        "schermata",                                     // 이탈리아어
        "снимок экрана",                                 // 러시아어
    };

    /// <summary>dedicated=전용 폴더면 확장자만으로 충분. 아니면 파일명 패턴도 요구.</summary>
    public static bool Matches(string name, bool dedicated)
    {
        var lower = name.ToLowerInvariant();
        if (!Extensions.Any(e => lower.EndsWith("." + e))) return false;
        if (dedicated) return true;
        return Patterns.Any(p => lower.Contains(p));
    }
}
