using System.Text.Json;
using Quick.Core;

namespace Quick.App;

/// <summary>사용자 설정 (JSON 영속화). macOS AppSettings 대응.</summary>
public sealed class Settings
{
    public bool AutoCopy { get; set; } = true;         // 새 캡처 자동 복사
    public bool SoundOnCapture { get; set; } = true;   // 캡처 시 소리
    public string Format { get; set; } = "png";        // png | jpeg
    public string SaveDirectory { get; set; } = "";    // 빈값 = 기본(Pictures\Screenshots)

    // 사용자 지정 전역 단축키 (기본값 = 기존 조합). VK: Q=0x51, 4=0x34, 3=0x33
    public Hotkey SearchHotkey { get; set; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x51);
    public Hotkey CaptureRegionHotkey { get; set; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x34);
    public Hotkey CaptureFullHotkey { get; set; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x33);

    /// <summary>단축키 기본값(초기화용).</summary>
    public static Hotkey DefaultSearch => new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x51);
    public static Hotkey DefaultCaptureRegion => new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x34);
    public static Hotkey DefaultCaptureFull => new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x33);

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
        // 원자적 쓰기: 임시 파일에 쓴 뒤 교체 → 도중 크래시로 파일이 깨져 전체 설정이 초기화되는 것 방지
        try
        {
            var path = FilePath();
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* 무시 */ }
    }

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath()))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath()));
                if (s is not null)
                {
                    // 구버전 파일/손상 대비: 단축키 누락·무효 시 기본값으로
                    s.SearchHotkey = Normalize(s.SearchHotkey, DefaultSearch);
                    s.CaptureRegionHotkey = Normalize(s.CaptureRegionHotkey, DefaultCaptureRegion);
                    s.CaptureFullHotkey = Normalize(s.CaptureFullHotkey, DefaultCaptureFull);
                    return s;
                }
            }
        }
        catch { /* 무시 */ }
        return new Settings();
    }

    private static Hotkey Normalize(Hotkey? hk, Hotkey fallback) =>
        hk is not null && hk.IsValid ? hk : fallback;
}
