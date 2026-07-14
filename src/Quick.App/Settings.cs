using System.Text.Json;

namespace Quick.App;

/// <summary>사용자 설정 (JSON 영속화). macOS AppSettings 대응.</summary>
public sealed class Settings
{
    public bool AutoCopy { get; set; } = true;         // 새 캡처 자동 복사
    public bool SoundOnCapture { get; set; } = true;   // 캡처 시 소리
    public string Format { get; set; } = "png";        // png | jpeg
    public string SaveDirectory { get; set; } = "";    // 빈값 = 기본(Pictures\Screenshots)

    public static Settings Current { get; private set; } = Load();

    private static string FilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quick");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static string DefaultSaveDir()
    {
        var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var shots = Path.Combine(pics, "Screenshots");
        return Directory.Exists(shots) ? shots : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    public string EffectiveSaveDir() =>
        string.IsNullOrWhiteSpace(SaveDirectory) ? DefaultSaveDir() : SaveDirectory;

    public void Save()
    {
        try { File.WriteAllText(FilePath(), JsonSerializer.Serialize(this)); }
        catch { /* 무시 */ }
    }

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath()))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath()));
                if (s is not null) return s;
            }
        }
        catch { /* 무시 */ }
        return new Settings();
    }
}
