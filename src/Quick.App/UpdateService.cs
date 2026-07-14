using System.Text.Json;
using Quick.Core;

namespace Quick.App;

/// <summary>GitHub Releases 기반 업데이트 확인 (인앱 알림). macOS UpdateService 대응.
/// 코어의 VersionCompare 재사용. 프리뷰(prerelease) 포함하려 releases 목록을 조회.</summary>
public sealed class UpdateService
{
    private const string Owner = "honest0237";
    private const string Repo = "quick-windows";

    public static string CurrentVersion
    {
        get
        {
            var v = typeof(UpdateService).Assembly.GetName().Version;
            return v is null ? "0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public string? LatestVersion { get; private set; }
    public string? ReleaseUrl { get; private set; }

    public bool UpdateAvailable =>
        LatestVersion is not null && VersionCompare.IsNewer(LatestVersion, CurrentVersion);

    public async Task CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Quick-Windows");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http.GetStringAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=10");
            using var doc = JsonDocument.Parse(json);

            string bestVer = "0";
            string? bestUrl = null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("tag_name", out var tagEl)) continue;
                var tag = tagEl.GetString() ?? "";
                var ver = tag.StartsWith("v") ? tag[1..] : tag;
                var url = el.TryGetProperty("html_url", out var u) ? u.GetString() : null;
                if (VersionCompare.IsNewer(ver, bestVer)) { bestVer = ver; bestUrl = url; }
            }
            if (bestVer != "0")
            {
                LatestVersion = bestVer;
                ReleaseUrl = bestUrl;
            }
        }
        catch { /* 네트워크 실패 등 무시 */ }
    }

    public void OpenReleasePage()
    {
        var url = ReleaseUrl ?? $"https://github.com/{Owner}/{Repo}/releases/latest";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* 무시 */ }
    }
}
