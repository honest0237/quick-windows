namespace Quick.Core;

/// <summary>스크린샷 메모리 검색 — 순수 로직. Swift ScreenshotMemory의 tokens/matches/makeTitle 이식.</summary>
public static class ScreenshotSearch
{
    public static string[] Tokens(string query) =>
        query.ToLowerInvariant().Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>모든 토큰이 제목/OCR본문/파일명 중 어딘가에 포함되면 매칭.</summary>
    public static bool Matches(MemoryEntry entry, string[] tokens)
    {
        var blob = (entry.Title + "\n" + entry.Text + "\n" + Path.GetFileName(entry.Path)).ToLowerInvariant();
        foreach (var t in tokens)
            if (!blob.Contains(t)) return false;
        return true;
    }

    /// <summary>매칭되는 항목만(순수 — 파일 존재 확인은 앱 계층에서).</summary>
    public static IEnumerable<MemoryEntry> Filter(IEnumerable<MemoryEntry> entries, string query)
    {
        var tokens = Tokens(query);
        if (tokens.Length == 0) return Enumerable.Empty<MemoryEntry>();
        return entries.Where(e => Matches(e, tokens));
    }

    /// <summary>OCR 전문에서 짧은 제목 추출 — 첫 유의미 줄(2~60자), 40자 초과면 자름, 없으면 fallback.</summary>
    public static string MakeTitle(string text, string fallback)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length is >= 2 and <= 60)
                return line.Length > 40 ? line[..40] + "…" : line;
        }
        return fallback;
    }
}
