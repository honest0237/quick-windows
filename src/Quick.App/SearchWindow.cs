using System.Drawing;
using System.Windows.Forms;
using Quick.Core;

namespace Quick.App;

/// <summary>사이드 패널(선반+검색) — 검색창이 비면 최근 스샷이 쌓여 보이고(선반),
/// 타이핑하면 '내용' 검색. macOS Quick 패널의 Windows 대응.</summary>
public sealed class SearchWindow : Form
{
    private readonly TextBox _search;
    private readonly Label _header;
    private readonly ListView _results;
    private readonly ImageList _thumbs;
    private readonly System.Windows.Forms.Timer _debounce;

    public SearchWindow()
    {
        Text = "Quick";
        Width = 360;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;

        _search = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI", 12F),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "스크린샷 내용 검색…  (예: 인보이스, 에러)",
        };
        _header = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            Font = new Font("Segoe UI", 9F),
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
        };
        var top = new Panel { Dock = DockStyle.Top, Height = 64 };
        top.Controls.Add(_search);
        top.Controls.Add(_header);

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

        Controls.Add(_results);
        Controls.Add(top);

        _debounce = new System.Windows.Forms.Timer { Interval = 150 };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Reload(); };

        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _results.DoubleClick += (_, _) => OpenSelected();
        _results.ItemDrag += OnItemDrag;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };

        PositionRightEdge();
    }

    private void PositionRightEdge()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Height = wa.Height - 20;
        Location = new Point(wa.Right - Width - 12, wa.Top + 10);
    }

    public void ToggleVisibility()
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            Reload();          // 열 때 최근 스샷(선반) 즉시 표시
            Show();
            Activate();
            _search.Focus();
            _search.SelectAll();
        }
    }

    // 트레이 앱: 창 닫기 = 숨기기
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
                Tag = entry.Path,
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

    /// <summary>파일 잠금 없이 축소 썸네일 로드.</summary>
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

    private string? SelectedPath() =>
        _results.SelectedItems.Count > 0 ? _results.SelectedItems[0].Tag as string : null;

    private void OpenSelected()
    {
        var path = SelectedPath();
        if (path is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* 무시 */ }
    }

    private void OnItemDrag(object? sender, ItemDragEventArgs e)
    {
        var path = SelectedPath();
        if (path is null) return;
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        DoDragDrop(data, DragDropEffects.Copy);
    }

    /// <summary>새 스샷이 색인되면 열려있는 선반을 갱신(외부에서 호출).</summary>
    public void NotifyNewScreenshot()
    {
        if (!Visible) return;
        if (InvokeRequired) { BeginInvoke(Reload); return; }
        Reload();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
            _thumbs.Dispose();
        }
        base.Dispose(disposing);
    }
}
