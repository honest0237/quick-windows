using System.Text.Json;
using Quick.Core;

namespace Quick.App;

/// <summary>GitHub Releases 기반 업데이트 확인 (인앱 알림 + 직접 다운로드).
/// macOS UpdateService 대응. 코어 VersionCompare 재사용.</summary>
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
    public string? DownloadUrl { get; private set; }   // 최신 릴리스의 Quick.exe 직접 링크

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
            string? bestUrl = null, bestDownload = null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("tag_name", out var tagEl)) continue;
                var tag = tagEl.GetString() ?? "";
                var ver = tag.StartsWith("v") ? tag[1..] : tag;
                if (!VersionCompare.IsNewer(ver, bestVer)) continue;

                bestVer = ver;
                bestUrl = el.TryGetProperty("html_url", out var u) ? u.GetString() : null;
                bestDownload = null;
                if (el.TryGetProperty("assets", out var assets))
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        var an = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (an is not null && an.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            bestDownload = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                    }
                }
            }
            if (bestVer != "0")
            {
                LatestVersion = bestVer;
                ReleaseUrl = bestUrl;
                DownloadUrl = bestDownload;
            }
        }
        catch { /* 무시 */ }
    }

    /// <summary>새 exe를 바로 다운로드(직접 링크). 없으면 릴리스 페이지.</summary>
    public void OpenDownload()
    {
        var url = DownloadUrl ?? ReleaseUrl ?? $"https://github.com/{Owner}/{Repo}/releases/latest";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* 무시 */ }
    }
}
