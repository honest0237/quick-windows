using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Quick.Core;

namespace Quick.App;

/// <summary>사이드 선반 — 어두운 헤더 + 큰 썸네일 카드 갤러리(제목·OCR 미리보기·시간).
/// 검색창이 비면 최근 스샷, 타이핑하면 내용 검색. macOS Quick 패널 대응.</summary>
public sealed class SearchWindow : Form
{
    private readonly TextBox _search;
    private readonly Label _count;
    private readonly Panel _list;
    private readonly Label _empty;
    private readonly System.Windows.Forms.Timer _debounce;
    private readonly ContextMenuStrip _itemMenu;
    private readonly List<ShelfCard> _cards = new();
    private ShelfCard? _selected;

    public SearchWindow()
    {
        Text = "Quick";
        Width = 376;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Theme.Bg;
        Icon = AppIcon.Value;

        // ── 어두운 헤더 ──
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.HeaderBg };
        var brand = new Label { Text = "Quick", Font = Theme.TitleBig, ForeColor = Color.White, AutoSize = true, Location = new Point(14, 11), BackColor = Color.Transparent };
        var ver = new Label { Text = $"v{UpdateService.CurrentVersion}", Font = Theme.Small, ForeColor = Theme.HeaderSub, AutoSize = true, Location = new Point(74, 18), BackColor = Color.Transparent };
        var gear = new Button { Text = "⚙", ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 13F), Dock = DockStyle.Right, Width = 46, BackColor = Theme.HeaderBg };
        gear.FlatAppearance.BorderSize = 0;
        gear.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 48, 58);
        gear.Click += (_, _) => { using var f = new SettingsForm(); f.ShowDialog(this); };
        header.Controls.Add(brand);
        header.Controls.Add(ver);
        header.Controls.Add(gear);

        // ── 검색 ──
        var searchWrap = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(12, 10, 12, 6) };
        _search = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11F),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "🔍  스크린샷 내용 검색…",
        };
        searchWrap.Controls.Add(_search);

        _count = new Label { Dock = DockStyle.Fill, Font = Theme.Small, ForeColor = Theme.SubText, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(15, 0, 0, 0), BackColor = Theme.Bg };

        // ── 카드 목록 ──
        _list = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
        _list.Resize += (_, _) => LayoutCards();
        _empty = new Label
        {
            Text = "아직 스크린샷이 없어요.\n\n캡처(Ctrl+Shift+4)하면 여기에 쌓이고,\n내용(글자)으로 검색할 수 있어요.",
            Font = Theme.Body,
            ForeColor = Theme.SubText,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Visible = false,
        };
        _list.Controls.Add(_empty);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(searchWrap, 0, 1);
        root.Controls.Add(_count, 0, 2);
        root.Controls.Add(_list, 0, 3);
        Controls.Add(root);

        _debounce = new System.Windows.Forms.Timer { Interval = 150 };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };

        _itemMenu = new ContextMenuStrip();
        _itemMenu.Items.Add("편집", null, (_, _) => EditSelected());
        _itemMenu.Items.Add("텍스트 복사", null, (_, _) => CopyTextSelected());
        _itemMenu.Items.Add("파일 열기", null, (_, _) => OpenSelected());
        _itemMenu.Items.Add("폴더에서 보기", null, (_, _) => RevealInFolder());

        PositionLeftEdge();
    }

    // 캡처 시 스스로 뜰 때 포커스를 뺏지 않도록(단축키로 열 땐 ToggleVisibility 에서 Activate).
    protected override bool ShowWithoutActivation => true;

    private void PositionLeftEdge()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Height = wa.Height - 20;
        Location = new Point(wa.Left + 12, wa.Top + 10);
    }

    public void ToggleVisibility()
    {
        if (Visible) { Hide(); return; }
        PositionLeftEdge();
        Reload();
        Show();
        Activate();
        _search.Focus();
        _search.SelectAll();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    private void Reload()
    {
        var query = _search.Text.Trim();
        IReadOnlyList<MemoryEntry> items;
        if (query.Length == 0) { items = ScreenshotMemory.Shared.Recent(40); _count.Text = $"최근 스크린샷 · {items.Count}"; }
        else { items = ScreenshotMemory.Shared.Search(query); _count.Text = $"검색 결과 · {items.Count}"; }

        _list.SuspendLayout();
        foreach (var c in _cards) { _list.Controls.Remove(c); c.Dispose(); }   // 썸네일까지 해제(GDI 누수 방지)
        _cards.Clear();
        _selected = null;

        foreach (var entry in items)
        {
            var card = new ShelfCard(entry, LoadCoverThumb(entry.Path, 100, 64)) { ContextMenuStrip = _itemMenu };
            card.Clicked += Select;
            card.Activated += c => EditEntry(c.Entry);
            _cards.Add(card);
            _list.Controls.Add(card);
        }
        _empty.Visible = _cards.Count == 0;
        LayoutCards();
        _list.ResumeLayout();
    }

    private void LayoutCards()
    {
        int width = _list.ClientSize.Width - 12;
        int y = 6;
        foreach (var c in _cards)
        {
            c.SetBounds(6, y, Math.Max(120, width), ShelfCard.CardHeight);
            y += ShelfCard.CardHeight + 6;
        }
    }

    private void Select(ShelfCard card)
    {
        if (_selected == card) return;
        if (_selected is not null) _selected.Selected = false;
        _selected = card;
        card.Selected = true;
    }

    private static Image? LoadCoverThumb(string path, int w, int h)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var img = Image.FromStream(fs);
            var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                double sr = (double)img.Width / img.Height, dr = (double)w / h;
                Rectangle src;
                if (sr > dr) { int sw = Math.Max(1, (int)(img.Height * dr)); src = new Rectangle((img.Width - sw) / 2, 0, sw, img.Height); }
                else { int sh = Math.Max(1, (int)(img.Width / dr)); src = new Rectangle(0, (img.Height - sh) / 2, img.Width, sh); }
                g.DrawImage(img, new Rectangle(0, 0, w, h), src, GraphicsUnit.Pixel);
            }
            return dst;
        }
        catch { return null; }
    }

    // ── 항목 동작 ──
    private void EditSelected() { if (_selected is not null) EditEntry(_selected.Entry); }

    private void EditEntry(MemoryEntry entry)
    {
        if (!File.Exists(entry.Path)) return;
        Bitmap bmp;
        try
        {
            using var fs = new FileStream(entry.Path, FileMode.Open, FileAccess.Read);
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

    private void CopyTextSelected()
    {
        var e = _selected?.Entry;
        if (e is null || string.IsNullOrWhiteSpace(e.Text)) return;
        try { Clipboard.SetText(e.Text); } catch { /* 무시 */ }
    }

    private void RevealInFolder()
    {
        var path = _selected?.Entry.Path;
        if (path is null) return;
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch { /* 무시 */ }
    }

    private void OpenSelected()
    {
        var path = _selected?.Entry.Path;
        if (path is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* 무시 */ }
    }

    /// <summary>새 스크린샷이 생기면 선반을 왼쪽에 띄워 보여준다(포커스는 뺏지 않음). 맥 선반 동작.</summary>
    public void NotifyNewScreenshot()
    {
        if (InvokeRequired) { BeginInvoke(NotifyNewScreenshot); return; }
        PositionLeftEdge();
        Reload();
        if (!Visible) Show();
        if (_cards.Count > 0) Select(_cards[0]);
        _list.AutoScrollPosition = new Point(0, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
            _itemMenu.Dispose();
            // 카드(및 썸네일)는 _list 자식이라 폼 Dispose 시 함께 해제됨
        }
        base.Dispose(disposing);
    }
}

/// <summary>선반의 스샷 카드 — 큰 썸네일 + 제목 + OCR 미리보기 + 시간, 호버/선택 하이라이트, 드래그아웃.</summary>
internal sealed class ShelfCard : Panel
{
    public const int CardHeight = 84;

    public MemoryEntry Entry { get; }
    private readonly Image? _thumb;
    private readonly string _snippet;
    private readonly string _time;
    private bool _hover;
    private bool _selected;
    private bool _mouseDown;
    private Point _downPt;

    public event Action<ShelfCard>? Clicked;
    public event Action<ShelfCard>? Activated;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => _selected; set { if (_selected != value) { _selected = value; Invalidate(); } } }

    public ShelfCard(MemoryEntry entry, Image? thumb)
    {
        Entry = entry;
        _thumb = thumb;
        _snippet = Snippet(entry.Text);
        _time = entry.Date.LocalDateTime.ToString("M/d  HH:mm");
        Height = CardHeight;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
    }

    private static string Snippet(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length > 0) return line.Length > 80 ? line[..80] : line;
        }
        return "";
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Clicked?.Invoke(this);   // 좌/우 클릭 모두 선택(우클릭 메뉴가 이 카드에 적용되도록)
        if (e.Button == MouseButtons.Left) { _mouseDown = true; _downPt = e.Location; }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_mouseDown && (Math.Abs(e.X - _downPt.X) > 6 || Math.Abs(e.Y - _downPt.Y) > 6))
        {
            _mouseDown = false;
            if (File.Exists(Entry.Path))
                DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { Entry.Path }), DragDropEffects.Copy);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { _mouseDown = false; base.OnMouseUp(e); }
    protected override void OnDoubleClick(EventArgs e) { Activated?.Invoke(this); base.OnDoubleClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new Rectangle(2, 2, Width - 4, Height - 6);
        using (var path = Theme.RoundRect(r, 10))
        {
            using var bg = new SolidBrush(_selected ? Theme.CardSelected : _hover ? Theme.CardHover : Theme.CardBg);
            g.FillPath(bg, path);
            using var pen = new Pen(_selected ? Theme.Accent : Theme.Border, _selected ? 1.6f : 1f);
            g.DrawPath(pen, path);
        }

        var tr = new Rectangle(r.X + 10, r.Y + 10, 100, r.Height - 20);
        using (var clip = Theme.RoundRect(tr, 6))
        {
            if (_thumb is not null)
            {
                var saved = g.Clip;
                g.SetClip(clip, CombineMode.Replace);
                g.DrawImage(_thumb, tr);
                g.Clip = saved;
                saved.Dispose();
            }
            else
            {
                using var ph = new SolidBrush(Color.FromArgb(236, 238, 241));
                g.FillPath(ph, clip);
            }
            using var tp = new Pen(Theme.Border);
            g.DrawPath(tp, clip);
        }

        int tx = tr.Right + 12;
        int tw = r.Right - tx - 10;
        const TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var title = string.IsNullOrEmpty(Entry.Title) ? "(제목 없음)" : Entry.Title;
        TextRenderer.DrawText(g, title, Theme.Title, new Rectangle(tx, r.Y + 12, tw, 20), Theme.Text, flags);
        if (_snippet.Length > 0)
            TextRenderer.DrawText(g, _snippet, Theme.Body, new Rectangle(tx, r.Y + 34, tw, 18), Theme.Snippet, flags);
        TextRenderer.DrawText(g, _time, Theme.Small, new Rectangle(tx, r.Y + 56, tw, 16), Theme.SubText, flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _thumb?.Dispose();
        base.Dispose(disposing);
    }
}
