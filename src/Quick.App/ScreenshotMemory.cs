using System.Text.Json;
using Quick.Core;

namespace Quick.App;

/// <summary>검색되는 스크린샷 메모리 (Windows). Swift ScreenshotMemory 대응 — 코어 검색 로직 재사용.</summary>
public sealed class ScreenshotMemory
{
    public static readonly ScreenshotMemory Shared = new();

    private readonly List<MemoryEntry> _entries = new();
    private readonly object _lock = new();
    private const int MaxEntries = 5000;

    private static string IndexPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quick");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "index.json");
        }
    }

    private ScreenshotMemory() => Load();

    /// <summary>최근 스샷 N개(선반용) — 파일이 존재하는 것만, 최신순.</summary>
    public IReadOnlyList<MemoryEntry> Recent(int n = 40)
    {
        lock (_lock)
            return _entries.Where(e => File.Exists(e.Path)).Take(n).ToList();
    }

    public IReadOnlyList<MemoryEntry> Search(string query, int limit = 40)
    {
        lock (_lock)
        {
            return ScreenshotSearch.Filter(_entries, query)
                .Where(e => File.Exists(e.Path))
                .Take(limit)
                .ToList();
        }
    }

    public async Task RecordAsync(string path, DateTimeOffset date)
    {
        lock (_lock) { if (_entries.Any(e => e.Path == path)) return; }

        var text = await OcrService.RecognizeAsync(path);
        var title = ScreenshotSearch.MakeTitle(text, Path.GetFileName(path));

        lock (_lock)
        {
            if (_entries.Any(e => e.Path == path)) return;
            _entries.Insert(0, new MemoryEntry(path, date, title, text));
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }
        Persist();
    }

    /// <summary>기존 스샷 백필 — 최신순, 직렬 OCR, 5개마다 저장.</summary>
    public async Task BackfillAsync(string directory, bool dedicated, int limit = 300)
    {
        HashSet<string> indexed;
        lock (_lock) indexed = _entries.Select(e => e.Path).ToHashSet();

        List<(string path, DateTime mod)> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(directory)
                .Where(f => ScreenshotName.Matches(Path.GetFileName(f), dedicated))
                .Where(f => !indexed.Contains(f))
                .Select(f => (path: f, mod: File.GetLastWriteTimeUtc(f)))
                .OrderByDescending(x => x.mod)
                .Take(limit)
                .ToList();
        }
        catch { return; }

        int done = 0;
        foreach (var (path, mod) in candidates)
        {
            await RecordAsync(path, new DateTimeOffset(mod, TimeSpan.Zero));
            if (++done % 5 == 0) Persist();
        }
        lock (_lock) _entries.Sort((a, b) => b.Date.CompareTo(a.Date));
        Persist();
    }

    private void Persist()
    {
        List<MemoryEntry> snapshot;
        lock (_lock) snapshot = new(_entries);
        try
        {
            var tmp = IndexPath + ".tmp";   // 원자적: 임시 파일 → 교체(크래시로 기록 전체 소실 방지)
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot));
            File.Move(tmp, IndexPath, overwrite: true);
        }
        catch { /* 무시 */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(IndexPath)) return;
            var data = JsonSerializer.Deserialize<List<MemoryEntry>>(File.ReadAllText(IndexPath));
            if (data is not null) _entries.AddRange(data);
        }
        catch { /* 무시 */ }
    }
}
