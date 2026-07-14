using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>영역 캡처 오버레이 — 미리 잡은 전체 화면을 배경으로 깔고 드래그 선택.
/// (실시간 캡처 대신 '얼린 화면'을 써서 오버레이 자체가 결과에 안 찍힘)</summary>
public sealed class RegionOverlay : Form
{
    private readonly Bitmap _frozen;   // 미리 캡처한 가상 화면 전체
    private Point _start;
    private Rectangle _sel;
    private bool _dragging;

    public Bitmap? Result { get; private set; }

    public RegionOverlay(Bitmap frozen)
    {
        _frozen = frozen;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImage(_frozen, 0, 0);
        using (var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            g.FillRectangle(dim, ClientRectangle);

        if (_sel.Width > 0 && _sel.Height > 0)
        {
            g.DrawImage(_frozen, _sel, _sel, GraphicsUnit.Pixel);   // 선택 영역만 원본 밝기
            using var pen = new Pen(Color.DodgerBlue, 2);
            g.DrawRectangle(pen, _sel);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _start = e.Location;
        _dragging = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        _sel = Rectangle.FromLTRB(
            Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y),
            Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        if (_sel.Width >= 4 && _sel.Height >= 4)
        {
            var crop = new Rectangle(_sel.X, _sel.Y, _sel.Width, _sel.Height);
            crop.Intersect(new Rectangle(0, 0, _frozen.Width, _frozen.Height));
            if (crop.Width >= 4 && crop.Height >= 4)
            {
                Result = _frozen.Clone(crop, _frozen.PixelFormat);
                DialogResult = DialogResult.OK;
            }
        }
        Close();
    }
}
