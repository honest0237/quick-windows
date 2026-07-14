using Quick.Core;
using Xunit;

namespace Quick.Core.Tests;

public class ScreenshotSearchTests
{
    private static MemoryEntry Entry(string path, string title, string text) =>
        new(path, DateTimeOffset.Now, title, text);

    [Fact]
    public void Matches_AllTokensRequired()
    {
        var e = Entry("/x/스크린샷 1.png", "인보이스 4021", "고객 인보이스 4021\n합계 55,000원");
        Assert.True(ScreenshotSearch.Matches(e, ScreenshotSearch.Tokens("인보이스")));
        Assert.True(ScreenshotSearch.Matches(e, ScreenshotSearch.Tokens("인보이스 4021")));
        Assert.True(ScreenshotSearch.Matches(e, ScreenshotSearch.Tokens("합계 55")));
        Assert.False(ScreenshotSearch.Matches(e, ScreenshotSearch.Tokens("인보이스 9999")));
    }

    [Fact]
    public void Matches_SearchesOcrText_NotJustFilename()
    {
        // 파일명엔 없지만 OCR 본문에 있는 단어로 찾을 수 있어야 함 (핵심 차별화)
        var e = Entry("/x/Screenshot 2024-01-01.png", "Error: connection refused", "Error: connection refused at line 42");
        Assert.True(ScreenshotSearch.Matches(e, ScreenshotSearch.Tokens("connection refused")));
        Assert.True(ScreenshotSearch.Matches(e, ScreenshotSearch.Tokens("line 42")));
    }

    [Fact]
    public void Matches_CaseInsensitive_Multilingual()
    {
        var e = Entry("/x/a.png", "Rechnung", "Bildschirmfoto Rechnung 2024");
        Assert.True(ScreenshotSearch.Matches(e, ScreenshotSearch.Tokens("RECHNUNG")));
    }

    [Fact]
    public void MakeTitle_FirstMeaningfulLine() =>
        Assert.Equal("인보이스 4021", ScreenshotSearch.MakeTitle("인보이스 4021\n합계", "x.png"));

    [Fact]
    public void MakeTitle_FallbackWhenNoText()
    {
        Assert.Equal("Screenshot.png", ScreenshotSearch.MakeTitle("", "Screenshot.png"));
        Assert.Equal("S.png", ScreenshotSearch.MakeTitle("\n\n", "S.png"));
    }

    [Fact]
    public void MakeTitle_TruncatesLong() =>
        Assert.Equal("x", ScreenshotSearch.MakeTitle(new string('가', 80), "x"));  // 60자 초과 → fallback

    [Fact]
    public void Tokens_SplitsOnSpaceAndNewline()
    {
        Assert.Equal(new[] { "a", "b", "c" }, ScreenshotSearch.Tokens("  a  b\nc "));
        Assert.Empty(ScreenshotSearch.Tokens("   "));
    }
}

public class VersionCompareTests
{
    [Theory]
    [InlineData("1.2.4", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.0.9", "1.1.0", false)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("2.0", "1.9.9", true)]
    [InlineData("1.2.0-beta", "1.2.0", false)]   // pre-release는 정식보다 최신 아님
    [InlineData("1.2.0", "1.2.0-beta", false)]
    [InlineData("1.3.0-rc1", "1.2.0", true)]
    public void IsNewer(string a, string b, bool expected) =>
        Assert.Equal(expected, VersionCompare.IsNewer(a, b));
}

public class ScreenshotNameTests
{
    [Theory]
    [InlineData("Bildschirmfoto 2024.png", true, true)]    // 전용 폴더 → 이름 무관
    [InlineData("anything.heic", true, true)]
    [InlineData("스크린샷 2024.png", false, true)]           // KR
    [InlineData("Screenshot 2024.png", false, true)]        // EN
    [InlineData("Bildschirmfoto.png", false, true)]         // DE
    [InlineData("スクリーンショット.png", false, true)]        // JA
    [InlineData("截屏2024.png", false, true)]               // ZH
    [InlineData("vacation_photo.png", false, false)]        // 스샷 아님
    [InlineData("notes.txt", true, false)]                  // 이미지 아님
    public void Matches(string name, bool dedicated, bool expected) =>
        Assert.Equal(expected, ScreenshotName.Matches(name, dedicated));
}
