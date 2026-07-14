using System.Drawing;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>검색 패널 — 스샷 '내용'으로 검색(차별화). macOS 패널의 "🧠 내 스크린샷에서" 대응.</summary>
public sealed class SearchWindow : Form
{
    private readonly TextBox _search;
    private readonly ListView _results;
    private readonly ImageList _thumbs;
    private readonly System.Windows.Forms.Timer _debounce;

    public SearchWindow()
    {
        Text = "Quick — 스크린샷 검색";
        Size = new Size(440, 580);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;

        _search = new TextBox
        {
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 12F),
            PlaceholderText = "스크린샷 내용 검색…  (예: 인보이스, 에러 로그)",
        };

        _thumbs = new ImageList { ImageSize = new Size(80, 56), ColorDepth = ColorDepth.Depth32Bit };
        _results = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None,
            SmallImageList = _thumbs,
        };
        _results.Columns.Add("제목", 290);
        _results.Columns.Add("날짜", 120);

        Controls.Add(_results);
        Controls.Add(_search);

        _debounce = new System.Windows.Forms.Timer { Interval = 150 };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunSearch(); };

        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _results.DoubleClick += (_, _) => OpenSelected();
        _results.ItemDrag += OnItemDrag;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
    }

    public void ToggleVisibility()
    {
        if (Visible)
        {
            Hide();
        }
        else
        {
            Show();
            Activate();
            _search.Focus();
            _search.SelectAll();
        }
    }

    // 트레이 앱: 창 닫기 = 숨기기 (앱 종료 아님)
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

    private void RunSearch()
    {
        var query = _search.Text.Trim();
        _results.BeginUpdate();
        _results.Items.Clear();
        _thumbs.Images.Clear();

        if (query.Length > 0)
        {
            int i = 0;
            foreach (var entry in ScreenshotMemory.Shared.Search(query))
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
        }
        _results.EndUpdate();
    }

    /// <summary>파일 잠금 없이 축소 썸네일 로드(스트림으로 열어 복사 후 닫음).</summary>
    private static Image? LoadThumbnail(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var img = Image.FromStream(fs);
            return new Bitmap(img, new Size(80, 56));
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
