using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Quick.Core;

namespace Quick.App;

/// <summary>자동 업데이트 다운로드 결과.</summary>
public enum DownloadOutcome
{
    Success,        // 다운로드 + 무결성 검증 통과
    Cancelled,      // 사용자가 취소
    TransferError,  // 네트워크/디스크/검증불가 — 재시도·수동 가능
    IntegrityError, // PE 불량 or 해시 불일치 — 손상/변조 의심, 설치 중단
}

public readonly record struct DownloadResult(DownloadOutcome Outcome, string? Path);

/// <summary>GitHub Releases 기반 업데이트 — 확인 + 다운로드(무결성 검증) + 원클릭 설치(교체·재시작).
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

    /// <summary>재시작 후 실제로 버전이 올랐는지 검증하기 위한 대기중 목표버전 파일.</summary>
    public static string PendingUpdatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quick", "pending-update.txt");

    public string? LatestVersion { get; private set; }
    public string? ReleaseUrl { get; private set; }
    public string? DownloadUrl { get; private set; }   // 최신 릴리스의 Quick.exe 직접 링크
    public string? Sha256Url { get; private set; }      // 있으면 무결성 검증에 사용(필수 취급)

    public bool UpdateAvailable =>
        LatestVersion is not null && VersionCompare.IsNewer(LatestVersion, CurrentVersion);

    private enum Verify { Ok, BadFormat, Mismatch, Unavailable }

    private static HttpClient NewHttp(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Quick-Windows");
        return http;
    }

    public async Task CheckAsync()
    {
        try
        {
            using var http = NewHttp(TimeSpan.FromSeconds(10));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http.GetStringAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=10");
            using var doc = JsonDocument.Parse(json);

            string bestVer = "0";
            string? bestUrl = null, bestExe = null, bestSha = null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("tag_name", out var tagEl)) continue;
                var tag = tagEl.GetString() ?? "";
                var ver = tag.StartsWith("v") ? tag[1..] : tag;
                if (!VersionCompare.IsNewer(ver, bestVer)) continue;

                bestVer = ver;
                bestUrl = el.TryGetProperty("html_url", out var u) ? u.GetString() : null;
                bestExe = null; bestSha = null;
                if (el.TryGetProperty("assets", out var assets))
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        var an = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (an is null) continue;
                        var url = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                        if (an.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) bestSha = url;
                        else if (an.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) bestExe = url;
                    }
                }
            }
            if (bestVer != "0")
            {
                LatestVersion = bestVer;
                ReleaseUrl = bestUrl;
                DownloadUrl = bestExe;
                Sha256Url = bestSha;
            }
        }
        catch { /* 무시 */ }
    }

    /// <summary>브라우저로 새 exe를 바로 받기(직접 링크). 없으면 릴리스 페이지. 자동설치 불가/실패 시 폴백.</summary>
    public void OpenDownload()
    {
        var url = DownloadUrl ?? ReleaseUrl ?? $"https://github.com/{Owner}/{Repo}/releases/latest";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* 무시 */ }
    }

    /// <summary>현재 실행 파일 폴더에 쓸 수 있는지(제자리 교체 가능 여부). Program Files 등이면 false.</summary>
    public static bool CanInstallInPlace()
    {
        var dir = CurrentDir();
        return dir is not null && IsDirWritable(dir);
    }

    private static string? CurrentDir()
    {
        var current = Environment.ProcessPath;
        return string.IsNullOrEmpty(current) ? null : Path.GetDirectoryName(current);
    }

    private static bool IsDirWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".quick-write-{Environment.ProcessId}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>같은 볼륨(현재 exe 폴더)에 스테이징 → move가 원자적 rename. 불가 시 LocalAppData.</summary>
    private static string ChooseStageDir()
    {
        var dir = CurrentDir();
        if (dir is not null && IsDirWritable(dir)) return dir;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quick", "updates");
    }

    /// <summary>새 exe를 스테이징 폴더로 다운로드하고 무결성 검증. 결과와 파일 경로를 반환.</summary>
    public async Task<DownloadResult> DownloadAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (DownloadUrl is null) return new DownloadResult(DownloadOutcome.TransferError, null);

        var stageDir = ChooseStageDir();
        Directory.CreateDirectory(stageDir);
        var dest = Path.Combine(stageDir, $"Quick-{LatestVersion}.exe.new");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(10));
        var token = cts.Token;

        try
        {
            using var http = NewHttp(Timeout.InfiniteTimeSpan);
            using var resp = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1L;

            long read = 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(token))
            await using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buffer, token)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), token);
                    read += n;
                    if (total > 0) progress?.Report((double)read / total);
                }
            }

            // 완전성: 길이가 있으면 정확히 일치해야 함. 길이 불명 + 체크섬 없음 → 무결성 확인 불가라 자동설치 보류.
            if (total > 0 && read != total) return Cleanup(dest, DownloadOutcome.TransferError);
            if (total <= 0 && Sha256Url is null) return Cleanup(dest, DownloadOutcome.TransferError);

            return await VerifyAsync(dest, token) switch
            {
                Verify.Ok => new DownloadResult(DownloadOutcome.Success, dest),
                Verify.Unavailable => Cleanup(dest, DownloadOutcome.TransferError),  // 체크섬 못 받음 → 재시도/수동
                _ => Cleanup(dest, DownloadOutcome.IntegrityError),                  // MZ 불량/해시 불일치 → 손상·변조
            };
        }
        catch (OperationCanceledException)
        {
            // 사용자가 취소(외부 토큰)면 조용히, 10분 타임아웃이면 전송오류로.
            return Cleanup(dest, ct.IsCancellationRequested ? DownloadOutcome.Cancelled : DownloadOutcome.TransferError);
        }
        catch
        {
            return Cleanup(dest, DownloadOutcome.TransferError);
        }
    }

    private static DownloadResult Cleanup(string path, DownloadOutcome outcome)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 무시 */ }
        return new DownloadResult(outcome, null);
    }

    /// <summary>PE 헤더(MZ) + (릴리스에 .sha256 있으면) SHA-256 검증. 체크섬이 있으면 통과 못 하면 설치 안 함.</summary>
    private async Task<Verify> VerifyAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var head = File.OpenRead(path);
            var mz = new byte[2];
            if (await head.ReadAsync(mz.AsMemory(0, 2), ct) < 2 || mz[0] != 0x4D || mz[1] != 0x5A)
                return Verify.BadFormat;   // "MZ"
        }
        catch { return Verify.BadFormat; }

        if (Sha256Url is null) return Verify.Ok;   // 체크섬 자산 없는 릴리스 → 크기·PE 검증만

        string expected;
        try
        {
            using var http = NewHttp(TimeSpan.FromSeconds(30));
            var raw = (await http.GetStringAsync(Sha256Url, ct)).Trim();
            var parts = raw.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !IsHex64(parts[0])) return Verify.Unavailable;   // 못 받았거나 형식 이상
            expected = parts[0];
        }
        catch { return Verify.Unavailable; }   // 체크섬 조회 실패 → 검증 불가(자동설치 보류)

        try
        {
            await using var fs = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) ? Verify.Ok : Verify.Mismatch;
        }
        catch { return Verify.Mismatch; }
    }

    private static bool IsHex64(string s)
    {
        if (s.Length != 64) return false;
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    /// <summary>다운로드한 새 exe로 현재 실행 파일을 교체·재시작하는 헬퍼를 띄운다.
    /// 실행 중 exe는 스스로 덮어쓸 수 없으므로, 앱 종료를 기다렸다 교체·재실행하는 배치를 분리 실행.
    /// 쓰기 권한이 없으면(예: Program Files) false를 반환하여 호출자가 브라우저 다운로드로 폴백하게 한다.
    /// true 반환 시 호출자는 즉시 앱을 종료해야 한다.</summary>
    public bool ApplyUpdateAndRestart(string newExePath)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current) || !File.Exists(newExePath)) return false;

        var curDir = Path.GetDirectoryName(current);
        if (curDir is null || !IsDirWritable(curDir)) return false;   // 관리자 권한 필요 등 → 폴백

        // 재시작 후 검증용: 목표버전 기록(교체 실패 시 옛 버전이 이걸 읽고 실패를 알림)
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PendingUpdatePath)!);
            File.WriteAllText(PendingUpdatePath, LatestVersion ?? "");
        }
        catch { /* 무시 */ }

        var pid = Environment.ProcessId;
        var bat = Path.Combine(Path.GetTempPath(), $"quick-update-{pid}.cmd");

        // cmd 퍼센트 확장은 따옴표 안에서도 일어나므로 경로의 '%'를 '%%'로 이스케이프('&' '^' '!'는 따옴표+지연확장off라 안전).
        var newEsc = newExePath.Replace("%", "%%");
        var curEsc = current.Replace("%", "%%");

        // move 재시도(앱 종료로 잠금 해제되면 성공). 지연은 timeout 대신 ping(콘솔 없는 환경 회피).
        // 교체 성공/실패 여부는 배치가 아니라 재시작한 앱이 버전으로 검증한다(PendingUpdatePath).
        var script =
$@"@echo off
set /a n=0
:loop
move /y ""{newEsc}"" ""{curEsc}"" >nul 2>&1
if not exist ""{newEsc}"" goto ok
set /a n+=1
if %n% geq 120 goto ok
ping -n 2 127.0.0.1 >nul
goto loop
:ok
start """" ""{curEsc}""
del ""%~f0"" >nul 2>&1
";
        try
        {
            File.WriteAllText(bat, script);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{bat}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return true;
        }
        catch { return false; }
    }
}
