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
    private bool _installing;              // InstallUpdate 재진입 가드
    private bool _capturing;               // 캡처 재진입 가드(오버레이 중 핫키 재진입 방지)
    private System.Windows.Forms.Timer? _updateTimer;   // 실행 중 주기적 업데이트 확인
    private string? _notifiedVersion;      // 같은 버전 중복 풍선 방지
    private bool _searchHkOk = true, _regionHkOk = true, _fullHkOk = true;   // 단축키 등록 성공 여부(메뉴 표기용)

    public QuickTrayContext()
    {
        _search = new SearchWindow();          // Form 생성 → WinForms 동기화 컨텍스트 설치
        _ = _search.Handle;                    // UI 스레드에서 핸들 강제 생성 → 워처 스레드의 InvokeRequired가 확실히 true
        _ui = SynchronizationContext.Current;

        _hotkey = new HotkeyManager();

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Value,
            Visible = true,
            Text = "Quick",
        };
        _tray.BalloonTipClicked += (_, _) => { if (_updater.UpdateAvailable) InstallUpdate(); };

        RegisterHotkeys();                          // 설정값으로 전역 단축키 등록(+ 메뉴 갱신)
        SettingsForm.HotkeysChanged += OnHotkeysChanged;   // 설정에서 바꾸면 즉시 재등록

        ShowWelcomeIfFirstRun();
        CheckPendingUpdate();
        StartWatching();
        _ = ScreenshotMemory.Shared.BackfillAsync(ScreenshotDir(), IsDedicated(ScreenshotDir()));
        _ = CheckUpdateAsync();
        StartUpdateTimer();    // 실행 중에도 주기적으로 확인(재시작 없이 알림)
        ShowRunningNotice();   // 트레이에 떠 있음을 알림(초보자용)
    }

    private void RefreshMenu()
    {
        var menu = new ContextMenuStrip();
        if (_updater.UpdateAvailable)
        {
            menu.Items.Add($"⬆ 업데이트 설치: v{_updater.LatestVersion}", null, (_, _) => InstallUpdate());
            menu.Items.Add(new ToolStripSeparator());
        }
        var s = Settings.Current;
        static string Lbl(string name, Hotkey hk, bool ok) => ok ? $"{name}  ({hk.Format()})" : $"{name}  (단축키 미등록)";
        menu.Items.Add(Lbl("영역 캡처", s.CaptureRegionHotkey, _regionHkOk), null, (_, _) => CaptureRegion());
        menu.Items.Add(Lbl("전체 캡처", s.CaptureFullHotkey, _fullHkOk), null, (_, _) => CaptureFull());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Lbl("검색 열기", s.SearchHotkey, _searchHkOk), null, (_, _) => _search.ToggleVisibility());
        menu.Items.Add("설정…", null, (_, _) => OpenSettings());
        menu.Items.Add("스크린샷 폴더 열기", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", ScreenshotDir()); } catch { }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => ExitThread());

        var old = _tray.ContextMenuStrip;   // 이전 메뉴 핸들 누수 방지
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    /// <summary>설정값으로 전역 단축키를 (재)등록. 내부 중복·외부 충돌을 구분해 안내.</summary>
    private void RegisterHotkeys()
    {
        _hotkey.UnregisterAll();
        var s = Settings.Current;

        // 내부 중복(같은 조합을 둘 이상 액션에 지정) 먼저 감지 — RegisterHotKey 는 같은 창에 같은 조합 2번 등록 불가
        var valid = new[] { s.SearchHotkey, s.CaptureRegionHotkey, s.CaptureFullHotkey }
            .Where(h => h is not null && h.IsValid);
        bool internalDup = valid.GroupBy(h => h).Any(g => g.Count() > 1);

        _searchHkOk = TryReg(s.SearchHotkey, () => _search.ToggleVisibility());
        _regionHkOk = TryReg(s.CaptureRegionHotkey, CaptureRegion);
        _fullHkOk = TryReg(s.CaptureFullHotkey, CaptureFull);

        RefreshMenu();   // 메뉴의 단축키 표기 갱신(미등록 포함)

        bool allOk = _searchHkOk && _regionHkOk && _fullHkOk;
        if (internalDup)
            _tray.ShowBalloonTip(4000, "단축키 중복",
                "같은 조합이 여러 기능에 지정돼 하나만 동작해요. 설정에서 바꿔주세요.", ToolTipIcon.Warning);
        else if (!allOk)
            _tray.ShowBalloonTip(4000, "단축키 등록 실패",
                "다른 프로그램이 쓰는 조합일 수 있어요. 설정에서 바꿔주세요.", ToolTipIcon.Warning);
    }

    private bool TryReg(Hotkey? hk, Action act) =>
        hk is not null && hk.IsValid && _hotkey.Register(hk, act);

    private void OnHotkeysChanged()
    {
        if (_ui is not null) _ui.Post(_ => RegisterHotkeys(), null);
        else RegisterHotkeys();
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
            var v = _updater.LatestVersion;
            SetTrayText($"Quick — 업데이트 v{v} 있음");   // 마우스만 올려도 보임
            if (_notifiedVersion != v)   // 새 버전일 때만 풍선(주기적 확인이 도배하지 않게)
            {
                _notifiedVersion = v;
                _tray.ShowBalloonTip(6000, "Quick 업데이트", $"새 버전 v{v} — 눌러서 설치", ToolTipIcon.Info);
            }
        }
        if (_ui is not null) _ui.Post(_ => OnUi(), null);
        else OnUi();
    }

    /// <summary>실행 중에도 30분마다 업데이트를 확인 → 새 버전이 나오면 재시작 없이 알림.</summary>
    private void StartUpdateTimer()
    {
        _updateTimer = new System.Windows.Forms.Timer { Interval = 30 * 60 * 1000 };
        _updateTimer.Tick += (_, _) => _ = CheckUpdateAsync();
        _updateTimer.Start();
    }

    /// <summary>앱이 트레이에 상주 중임을 알림(창이 없어 실행 여부를 모르는 사용자용).</summary>
    private void ShowRunningNotice()
    {
        var s = Settings.Current;
        _tray.ShowBalloonTip(4000, "Quick 실행 중",
            $"트레이(작업 표시줄 오른쪽 ▲)에 있어요.  캡처 {s.CaptureRegionHotkey.Format()} · 검색 {s.SearchHotkey.Format()}",
            ToolTipIcon.Info);
    }

    private void SetTrayText(string text) =>
        _tray.Text = text.Length > 63 ? text[..63] : text;   // NotifyIcon.Text 63자 제한

    /// <summary>원클릭 자동 설치: 확인 → (권한 확인) → 다운로드(진행률·취소) → 검증 → 교체·재시작.
    /// 각 실패 유형을 구분해 안내하고, 자동설치 불가 시 브라우저 다운로드로 폴백.</summary>
    private async void InstallUpdate()
    {
        if (_installing || !_updater.UpdateAvailable) return;
        _installing = true;
        try
        {
            var confirm = MessageBox.Show(
                $"새 버전 v{_updater.LatestVersion} 을(를) 지금 설치하고 재시작할까요?",
                "Quick 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            // 제자리 교체가 불가한 위치(Program Files 등)면 다운로드 없이 바로 브라우저 안내
            if (!UpdateService.CanInstallInPlace())
            {
                var r = MessageBox.Show(
                    "설치 폴더에 쓰기 권한이 없어요(예: Program Files).\n브라우저에서 새 버전을 받아 실행해 주세요.",
                    "Quick 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) _updater.OpenDownload();
                return;
            }

            DownloadResult result;
            using (var cts = new CancellationTokenSource())
            using (var prog = new UpdateProgressForm(_updater.LatestVersion!))
            {
                prog.CancelRequested += () => cts.Cancel();
                prog.Show();
                var progress = new Progress<double>(p => prog.SetProgress(p));
                try { result = await _updater.DownloadAsync(progress, cts.Token); }
                catch { result = new DownloadResult(DownloadOutcome.TransferError, null); }
                prog.ForceClose();
            }

            switch (result.Outcome)
            {
                case DownloadOutcome.Cancelled:
                    return;   // 사용자 취소 — 조용히 종료

                case DownloadOutcome.IntegrityError:
                    MessageBox.Show(
                        "받은 파일의 무결성 검증에 실패했어요.\n파일이 손상되었거나 변조되었을 수 있어 설치를 중단합니다.",
                        "Quick 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;

                case DownloadOutcome.TransferError:
                {
                    var r = MessageBox.Show(
                        "업데이트를 완료하지 못했어요.\n브라우저에서 직접 받으시겠어요?",
                        "Quick 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r == DialogResult.Yes) _updater.OpenDownload();
                    return;
                }

                case DownloadOutcome.Success:
                    if (!_updater.ApplyUpdateAndRestart(result.Path!))
                    {
                        var r = MessageBox.Show(
                            "업데이트 적용에 실패했어요(권한 등).\n브라우저에서 직접 받으시겠어요?",
                            "Quick 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (r == DialogResult.Yes) _updater.OpenDownload();
                        return;
                    }
                    ExitThread();   // 앱 종료 → 헬퍼가 교체 후 새 버전 실행
                    return;
            }
        }
        finally
        {
            _installing = false;   // 성공(ExitThread) 경로에선 실행 안 되지만 무해
        }
    }

    /// <summary>이전 실행에서 자동설치를 시도했다면, 재시작 후 실제로 버전이 올랐는지 검증.
    /// 오르지 않았으면(교체 실패) 사용자에게 알림 → 조용한 실패 방지.</summary>
    private void CheckPendingUpdate()
    {
        var path = UpdateService.PendingUpdatePath;
        if (!File.Exists(path)) return;

        string target;
        try { target = File.ReadAllText(path).Trim(); }
        catch { return; }
        try { File.Delete(path); } catch { /* 무시 */ }
        if (string.IsNullOrEmpty(target)) return;

        if (VersionCompare.IsNewer(target, UpdateService.CurrentVersion))
            _tray.ShowBalloonTip(6000, "Quick 업데이트",
                $"v{target} 설치가 완료되지 않았어요. 트레이 메뉴에서 다시 시도하거나 브라우저에서 받아주세요.",
                ToolTipIcon.Warning);
        else
            _tray.ShowBalloonTip(4000, "Quick",
                $"v{UpdateService.CurrentVersion} 로 업데이트되었습니다 🎉", ToolTipIcon.Info);
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

    private async void CaptureRegion()
    {
        if (_capturing) return;   // 오버레이가 떠 있는 동안 핫키 재진입 → 오버레이 중첩 방지
        _capturing = true;
        try
        {
            await DelayIfNeeded();
            var frozen = CaptureService.CaptureFullScreen();
            using var overlay = new RegionOverlay(frozen);
            var result = overlay.ShowDialog() == DialogResult.OK ? overlay.Result : null;
            frozen.Dispose();
            if (result is not null)
            {
                HandleCapture(result);
                result.Dispose();
            }
        }
        finally { _capturing = false; }
    }

    private async void CaptureFull()
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            await DelayIfNeeded();
            using var bmp = CaptureService.CaptureFullScreen();
            HandleCapture(bmp);
        }
        finally { _capturing = false; }
    }

    private static async Task DelayIfNeeded()
    {
        var d = Settings.Current.CaptureDelaySeconds;
        if (d > 0) await Task.Delay(d * 1000);   // 메뉴·툴팁 캡처용 지연
    }

    /// <summary>설정에 따라 편집기를 열거나(저장 시 색인) 바로 저장·색인.</summary>
    private void HandleCapture(Bitmap bmp)
    {
        if (Settings.Current.OpenEditorAfterCapture)
        {
            using var editor = new MarkupForm(bmp);
            if (editor.ShowDialog() == DialogResult.OK && editor.RenderedResult is not null)
            {
                SaveAndIndex(editor.RenderedResult);
                editor.RenderedResult.Dispose();
            }
            else if (Settings.Current.AutoCopy)
            {
                // 저장 안 하고 닫아도(Esc/X) 최소한 원본은 클립보드에(조용한 소실 방지)
                try { Clipboard.SetImage(bmp); } catch { /* 무시 */ }
            }
        }
        else
        {
            SaveAndIndex(bmp);
        }
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
            SettingsForm.HotkeysChanged -= OnHotkeysChanged;
            _updateTimer?.Dispose();
            _tray.ContextMenuStrip?.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _watcher?.Dispose();
            _hotkey.Dispose();
            _search.Dispose();
        }
        base.Dispose(disposing);
    }
}
