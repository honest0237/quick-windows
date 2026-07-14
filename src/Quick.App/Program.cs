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
    private readonly UpdateService _updater = new();
    private readonly SynchronizationContext? _ui;
    private FileSystemWatcher? _watcher;

    public QuickTrayContext()
    {
        _search = new SearchWindow();          // Form 생성 → WinForms 동기화 컨텍스트 설치
        _ui = SynchronizationContext.Current;

        _hotkey = new HotkeyManager();
        _hotkey.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, HotkeyManager.VkQ, () => _search.ToggleVisibility());
        _hotkey.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, HotkeyManager.Vk4, CaptureRegion);
        _hotkey.Register(HotkeyManager.ModControl | HotkeyManager.ModShift, HotkeyManager.Vk3, CaptureFull);

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Value,
            Visible = true,
            Text = "Quick",
        };
        _tray.BalloonTipClicked += (_, _) => { if (_updater.UpdateAvailable) _updater.OpenDownload(); };
        RefreshMenu();

        ShowWelcomeIfFirstRun();
        StartWatching();
        _ = ScreenshotMemory.Shared.BackfillAsync(ScreenshotDir(), IsDedicated(ScreenshotDir()));
        _ = CheckUpdateAsync();
    }

    private void RefreshMenu()
    {
        var menu = new ContextMenuStrip();
        if (_updater.UpdateAvailable)
        {
            menu.Items.Add($"⬆ 업데이트 있음: v{_updater.LatestVersion}", null, (_, _) => _updater.OpenDownload());
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add("영역 캡처  (Ctrl+Shift+4)", null, (_, _) => CaptureRegion());
        menu.Items.Add("전체 캡처  (Ctrl+Shift+3)", null, (_, _) => CaptureFull());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("검색 열기  (Ctrl+Shift+Q)", null, (_, _) => _search.ToggleVisibility());
        menu.Items.Add("설정…", null, (_, _) => OpenSettings());
        menu.Items.Add("스크린샷 폴더 열기", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", ScreenshotDir()); } catch { }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;
    }

    private static string AppDataDir()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quick");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void ShowWelcomeIfFirstRun()
    {
        var marker = Path.Combine(AppDataDir(), ".welcomed");
        if (File.Exists(marker)) return;
        try { File.WriteAllText(marker, DateTime.Now.ToString("o")); } catch { }
        using var welcome = new WelcomeForm();
        welcome.ShowDialog();
    }

    private async Task CheckUpdateAsync()
    {
        await _updater.CheckAsync();
        if (!_updater.UpdateAvailable) return;
        void OnUi()
        {
            RefreshMenu();
            _tray.ShowBalloonTip(5000, "Quick 업데이트", $"새 버전 v{_updater.LatestVersion} 이(가) 있습니다. 눌러서 받기", ToolTipIcon.Info);
        }
        if (_ui is not null) _ui.Post(_ => OnUi(), null);
        else OnUi();
    }

    /// <summary>저장/감시 폴더 — 설정값(없으면 Pictures\Screenshots, 없으면 Desktop).</summary>
    private static string ScreenshotDir() => Settings.Current.EffectiveSaveDir();

    /// <summary>전용 폴더(일반 폴더=Desktop/Documents/홈 이면 이름 패턴도 요구).</summary>
    private static bool IsDedicated(string dir)
    {
        static string Norm(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
        var common = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        var d = Norm(dir);
        return !common.Any(c => string.Equals(Norm(c), d, StringComparison.OrdinalIgnoreCase));
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
        _search.NotifyNewScreenshot();   // 선반 갱신
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
        var s = Settings.Current;
        var path = CaptureService.Save(bmp, s.EffectiveSaveDir(), s.Format);
        if (s.AutoCopy) { try { Clipboard.SetImage(bmp); } catch { /* 무시 */ } }
        if (s.SoundOnCapture) { try { System.Media.SystemSounds.Asterisk.Play(); } catch { /* 무시 */ } }
        _tray.ShowBalloonTip(1500, "Quick", "캡처 저장·색인됨", ToolTipIcon.None);
        _ = IndexAndNotify(path);
    }

    private void OpenSettings()
    {
        var before = Settings.Current.EffectiveSaveDir();
        using var form = new SettingsForm();
        form.ShowDialog();
        if (!string.Equals(Settings.Current.EffectiveSaveDir(), before, StringComparison.OrdinalIgnoreCase))
        {
            _watcher?.Dispose();
            _watcher = null;
            StartWatching();   // 새 폴더로 감시 재시작
            _ = ScreenshotMemory.Shared.BackfillAsync(ScreenshotDir(), IsDedicated(ScreenshotDir()));
        }
    }

    private async Task IndexAndNotify(string path)
    {
        await ScreenshotMemory.Shared.RecordAsync(path, DateTimeOffset.Now);   // OCR 색인
        _search.NotifyNewScreenshot();                                         // 선반 갱신
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
