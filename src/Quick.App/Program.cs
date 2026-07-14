using System.Drawing;
using System.Windows.Forms;
using Quick.Core;

namespace Quick.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new QuickTrayContext());
    }
}

/// <summary>메뉴바(트레이) 상주 앱 — macOS 메뉴바 앱의 Windows 대응.
/// 스샷 폴더 감시 → OCR 색인, 시작 시 기존 스샷 백필. (검색 패널 UI는 다음 단계)</summary>
internal sealed class QuickTrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly SearchWindow _search;
    private readonly HotkeyManager _hotkey;
    private FileSystemWatcher? _watcher;

    public QuickTrayContext()
    {
        _search = new SearchWindow();
        _hotkey = new HotkeyManager();
        _hotkey.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, HotkeyManager.VkQ, () => _search.ToggleVisibility());
        _hotkey.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, HotkeyManager.Vk4, CaptureRegion);
        _hotkey.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, HotkeyManager.Vk3, CaptureFull);

        _tray = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Quick — 스크린샷 메모리",
            ContextMenuStrip = BuildMenu(),
        };

        StartWatching();
        _ = ScreenshotMemory.Shared.BackfillAsync(ScreenshotDir(), IsDedicated(ScreenshotDir()));
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("영역 캡처  (Ctrl+Shift+4)", null, (_, _) => CaptureRegion());
        menu.Items.Add("전체 캡처  (Ctrl+Shift+3)", null, (_, _) => CaptureFull());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("검색 열기  (Ctrl+Shift+Q)", null, (_, _) => _search.ToggleVisibility());
        menu.Items.Add("스크린샷 폴더 열기", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", ScreenshotDir()); } catch { }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => ExitThread());
        return menu;
    }

    /// <summary>Windows 기본 스크린샷 폴더: Pictures\Screenshots (Win+PrtScn). 없으면 Desktop.</summary>
    private static string ScreenshotDir()
    {
        var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var shots = Path.Combine(pics, "Screenshots");
        return Directory.Exists(shots)
            ? shots
            : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    private static bool IsDedicated(string dir)
    {
        var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return dir.StartsWith(Path.Combine(pics, "Screenshots"), StringComparison.OrdinalIgnoreCase);
    }

    private void StartWatching()
    {
        var dir = ScreenshotDir();
        _watcher = new FileSystemWatcher(dir)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Created += OnCreated;
    }

    private async void OnCreated(object sender, FileSystemEventArgs e)
    {
        var dir = ScreenshotDir();
        if (!ScreenshotName.Matches(Path.GetFileName(e.FullPath), IsDedicated(dir))) return;
        await Task.Delay(400);   // 파일 쓰기 완료 대기
        await ScreenshotMemory.Shared.RecordAsync(e.FullPath, DateTimeOffset.Now);
    }

    // MARK: 캡처 (Windows엔 스샷→폴더 흐름이 약해서 직접 제공)

    private void CaptureRegion()
    {
        var frozen = CaptureService.CaptureFullScreen();
        using var overlay = new RegionOverlay(frozen);
        var result = overlay.ShowDialog() == DialogResult.OK ? overlay.Result : null;
        frozen.Dispose();
        if (result is not null)
        {
            SaveAndIndex(result);
            result.Dispose();
        }
    }

    private void CaptureFull()
    {
        using var bmp = CaptureService.CaptureFullScreen();
        SaveAndIndex(bmp);
    }

    private void SaveAndIndex(Bitmap bmp)
    {
        var path = CaptureService.Save(bmp, ScreenshotDir());
        try { Clipboard.SetImage(bmp); } catch { /* 무시 */ }
        _ = ScreenshotMemory.Shared.RecordAsync(path, DateTimeOffset.Now);   // OCR 색인(비동기)
        _tray.ShowBalloonTip(1500, "Quick", "캡처 저장·색인됨", ToolTipIcon.None);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _watcher?.Dispose();
            _hotkey.Dispose();
            _search.Dispose();
        }
        base.Dispose(disposing);
    }
}
