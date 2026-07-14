using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>캡처를 화면 위에 '핀'으로 고정 — 항상 위 떠 있는 작은 창(Snipaste식).
/// 드래그로 이동, 우클릭 메뉴(복사·저장·닫기), Esc 닫기. Windows 캡처도구엔 없는 기능.</summary>
public sealed class PinWindow : Form
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    private readonly Bitmap _img;   // 소유(닫을 때 해제)
    private readonly ContextMenuStrip _menu;

    public PinWindow(Bitmap img)
    {
        _img = img;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        KeyPreview = true;
        Icon = AppIcon.Value;
        Text = "Quick 핀";

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        double scale = Math.Min(1.0, Math.Min((double)(wa.Width - 120) / img.Width, (double)(wa.Height - 120) / img.Height));
        int w = Math.Max(60, (int)Math.Round(img.Width * scale));
        int h = Math.Max(40, (int)Math.Round(img.Height * scale));
        ClientSize = new Size(w, h);
        Location = new Point(wa.Left + 60, wa.Top + 60);

        _menu = new ContextMenuStrip();
        _menu.Items.Add("복사", null, (_, _) => { try { Clipboard.SetImage(_img); } catch { } });
        _menu.Items.Add("저장", null, (_, _) => SaveImage());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("닫기", null, (_, _) => Close());
        ContextMenuStrip = _menu;

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)   // 테두리 없는 창을 드래그로 이동
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(_img, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
        using var pen = new Pen(Theme.Accent, 2);
        g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    private void SaveImage()
    {
        var s = Settings.Current;
        try
        {
            var path = CaptureService.Save(_img, s.EffectiveSaveDir(), s.Format);
            _ = ScreenshotMemory.Shared.RecordAsync(path, DateTimeOffset.Now);
        }
        catch { /* 무시 */ }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _img.Dispose(); _menu.Dispose(); }
        base.Dispose(disposing);
    }
}
