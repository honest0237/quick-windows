using System.Drawing;
using System.Windows.Forms;
using Quick.Core;

namespace Quick.App;

/// <summary>사이드 패널(선반+검색) — 검색창이 비면 최근 스샷(선반), 타이핑하면 내용 검색.
/// 상단에 버전·설정(⚙). macOS Quick 패널 대응.</summary>
public sealed class SearchWindow : Form
{
    private readonly TextBox _search;
    private readonly Label _header;
    private readonly ListView _results;
    private readonly ImageList _thumbs;
    private readonly System.Windows.Forms.Timer _debounce;
    private readonly ContextMenuStrip _itemMenu;

    public SearchWindow()
    {
        Text = "Quick";
        Width = 360;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        Icon = AppIcon.Value;

        // 타이틀 바: Quick vX.Y.Z  +  ⚙ 설정
        var titleBar = new Panel { Dock = DockStyle.Fill };
        var appName = new Label
        {
            Text = $"Quick   v{UpdateService.CurrentVersion}",
            Font = new Font("Segoe UI Semibold", 10.5F),
            AutoSize = true,
            Location = new Point(8, 6),
        };
        var gear = new Button
        {
            Text = "⚙",
            Dock = DockStyle.Right,
            Width = 36,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 12F),
        };
        gear.FlatAppearance.BorderSize = 0;
        gear.Click += (_, _) => { using var f = new SettingsForm(); f.ShowDialog(); };
        titleBar.Controls.Add(appName);
        titleBar.Controls.Add(gear);

        _search = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12F),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "스크린샷 내용 검색…  (예: 인보이스, 에러)",
        };
        _header = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F),
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
        };
        _thumbs = new ImageList { ImageSize = new Size(72, 48), ColorDepth = ColorDepth.Depth32Bit };
        _results = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None,
            SmallImageList = _thumbs,
            BorderStyle = BorderStyle.None,
        };
        _results.Columns.Add("제목", 240);
        _results.Columns.Add("날짜", 96);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(titleBar, 0, 0);
        layout.Controls.Add(_search, 0, 1);
        layout.Controls.Add(_header, 0, 2);
        layout.Controls.Add(_results, 0, 3);
        Controls.Add(layout);

        _debounce = new System.Windows.Forms.Timer { Interval = 150 };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };

        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _results.DoubleClick += (_, _) => EditSelected();   // 더블클릭 → 편집기(핵심)
        _results.ItemDrag += OnItemDrag;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };

        _itemMenu = new ContextMenuStrip();
        _itemMenu.Items.Add("편집", null, (_, _) => EditSelected());
        _itemMenu.Items.Add("텍스트 복사", null, (_, _) => CopyTextSelected());
        _itemMenu.Items.Add("파일 열기", null, (_, _) => OpenSelected());
        _itemMenu.Items.Add("폴더에서 보기", null, (_, _) => RevealInFolder());
        _results.ContextMenuStrip = _itemMenu;

        PositionLeftEdge();
    }

    // 캡처 시 스스로 뜰 때 다른 창의 포커스를 뺏지 않도록(맥 선반처럼).
    // 단축키로 열 때는 아래 ToggleVisibility 에서 명시적으로 Activate/Focus 한다.
    protected override bool ShowWithoutActivation => true;

    private void PositionLeftEdge()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Height = wa.Height - 20;
        Location = new Point(wa.Left + 12, wa.Top + 10);
    }

    public void ToggleVisibility()
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            PositionLeftEdge();
            Reload();
            Show();
            Activate();          // 사용자가 단축키로 연 경우 → 포커스 가져와 바로 타이핑
            _search.Focus();
            _search.SelectAll();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    private void Reload()
    {
        var query = _search.Text.Trim();
        IReadOnlyList<MemoryEntry> items;
        if (query.Length == 0)
        {
            items = ScreenshotMemory.Shared.Recent(40);
            _header.Text = $"최근 스크린샷 · {items.Count}";
        }
        else
        {
            items = ScreenshotMemory.Shared.Search(query);
            _header.Text = $"내 스크린샷에서 · {items.Count}";
        }

        _results.BeginUpdate();
        _results.Items.Clear();
        _thumbs.Images.Clear();
        int i = 0;
        foreach (var entry in items)
        {
            var item = new ListViewItem(new[] { entry.Title, entry.Date.LocalDateTime.ToString("M/d HH:mm") })
            {
                Tag = entry,   // MemoryEntry (경로 + OCR 텍스트)
            };
            var thumb = LoadThumbnail(entry.Path);
            if (thumb is not null)
            {
                _thumbs.Images.Add(thumb);
                item.ImageIndex = i++;
            }
            _results.Items.Add(item);
        }
        _results.EndUpdate();
    }

    private static Image? LoadThumbnail(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var img = Image.FromStream(fs);
            return new Bitmap(img, new Size(72, 48));
        }
        catch
        {
            return null;
        }
    }

    private MemoryEntry? SelectedEntry() =>
        _results.SelectedItems.Count > 0 ? _results.SelectedItems[0].Tag as MemoryEntry : null;

    private string? SelectedPath() => SelectedEntry()?.Path;

    /// <summary>선반 항목을 마크업 편집기로 연다. 저장 시 편집본을 새 파일로 저장·색인.</summary>
    private void EditSelected()
    {
        var path = SelectedPath();
        if (path is null || !File.Exists(path)) return;

        Bitmap bmp;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var img = Image.FromStream(fs);
            bmp = new Bitmap(img);
        }
        catch { return; }

        using (bmp)
        using (var editor = new MarkupForm(bmp))
        {
            if (editor.ShowDialog(this) == DialogResult.OK && editor.RenderedResult is not null)
            {
                using var res = editor.RenderedResult;
                var s = Settings.Current;
                var saved = CaptureService.Save(res, s.EffectiveSaveDir(), s.Format);
                _ = IndexAndReload(saved);
            }
        }
    }

    private async Task IndexAndReload(string path)
    {
        await ScreenshotMemory.Shared.RecordAsync(path, DateTimeOffset.Now);
        if (InvokeRequired) BeginInvoke(Reload); else Reload();
    }

    /// <summary>선택 항목의 OCR 텍스트를 클립보드로(이미 색인돼 재인식 불필요).</summary>
    private void CopyTextSelected()
    {
        var e = SelectedEntry();
        if (e is null || string.IsNullOrWhiteSpace(e.Text)) return;
        try { Clipboard.SetText(e.Text); } catch { /* 무시 */ }
    }

    private void RevealInFolder()
    {
        var path = SelectedPath();
        if (path is null) return;
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch { /* 무시 */ }
    }

    private void OpenSelected()
    {
        var path = SelectedPath();
        if (path is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* 무시 */ }
    }

    private void OnItemDrag(object? sender, ItemDragEventArgs e)
    {
        var path = SelectedPath();
        if (path is null) return;
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        DoDragDrop(data, DragDropEffects.Copy);
    }

    /// <summary>새 스크린샷이 생기면 선반을 왼쪽에 띄워 보여준다(포커스는 뺏지 않음). 맥 선반 동작 대응.</summary>
    public void NotifyNewScreenshot()
    {
        if (InvokeRequired) { BeginInvoke(NotifyNewScreenshot); return; }

        PositionLeftEdge();
        Reload();
        if (!Visible) Show();          // ShowWithoutActivation=true → 포커스 안 뺏고 등장
        if (_results.Items.Count > 0)  // 방금 캡처(최상단) 강조
        {
            _results.Items[0].Selected = true;
            _results.Items[0].EnsureVisible();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
            _thumbs.Dispose();
            _itemMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
