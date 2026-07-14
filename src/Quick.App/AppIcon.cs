using System.Drawing;

namespace Quick.App;

/// <summary>앱 아이콘(번개) — 임베드된 quick.ico를 트레이·창에 사용.</summary>
public static class AppIcon
{
    private static Icon? _cached;

    public static Icon Value => _cached ??= Load();

    private static Icon Load()
    {
        try
        {
            var asm = typeof(AppIcon).Assembly;
            using var stream = asm.GetManifestResourceStream("Quick.App.quick.ico");
            if (stream is not null) return new Icon(stream);
        }
        catch { /* 무시 */ }
        return SystemIcons.Application;
    }
}
