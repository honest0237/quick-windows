using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>영역 캡처 오버레이 — 얼린 전체 화면 위에서 드래그 선택.
/// Windows 기본 캡처도구엔 없는 것들: 실시간 크기 표시 · 십자선 · 픽셀 돋보기(좌표/색상) · ESC/우클릭 취소.</summary>
public sealed class RegionOverlay : Form
{
    private readonly Bitmap _frozen;   // 미리 캡처한 가상 화면 전체(결과 원본)
    private readonly Bitmap _dimmed;   // 어둡게 처리한 배경(성능: 매 페인트마다 재합성 안 함)

    private readonly Font _badgeFont = new("Segoe UI", 9F, FontStyle.Bold);
    private readonly Font _hintFont = new("Segoe UI", 10.5F);
    private readonly Font _readoutFont = new("Consolas", 8.5F);

    private Point _start;
    private Point _cursor;
    private Rectangle _sel;
    private bool _dragging;

    public Bitmap? Result { get; private set; }

    public RegionOverlay(Bitmap frozen)
    {
        _frozen = frozen;

        _dimmed = new Bitmap(frozen.Width, frozen.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(_dimmed))
        {
            g.DrawImageUnscaled(frozen, 0, 0);
            using var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            g.FillRectangle(dim, 0, 0, frozen.Width, frozen.Height);
        }

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_dimmed, 0, 0);

        if (_sel.Width > 0 && _sel.Height > 0)
        {
            g.DrawImage(_frozen, _sel, _sel, GraphicsUnit.Pixel);   // 선택 영역만 원본 밝기
            using var pen = new Pen(Color.DodgerBlue, 2);
            g.DrawRectangle(pen, _sel);
            DrawSizeBadge(g, _sel);
        }
        else
        {
            DrawCrosshair(g, _cursor);
            DrawHint(g, _cursor);
        }

        DrawLoupe(g, _cursor);
    }

    private void DrawCrosshair(Graphics g, Point p)
    {
        using var pen = new Pen(Color.FromArgb(150, 0, 200, 255));
        g.DrawLine(pen, 0, p.Y, Width, p.Y);
        g.DrawLine(pen, p.X, 0, p.X, Height);
    }

    private void DrawSizeBadge(Graphics g, Rectangle rect)
    {
        var text = $"{rect.Width} × {rect.Height}";
        var sz = g.MeasureString(text, _badgeFont);
        float bx = rect.X;
        float by = rect.Y - sz.Height - 8;
        if (by < 2) by = rect.Y + 6;   // 위 공간 없으면 안쪽 위
        var bg = new RectangleF(bx, by, sz.Width + 12, sz.Height + 6);
        using (var b = new SolidBrush(Color.FromArgb(215, 0, 0, 0)))
            g.FillRectangle(b, bg);
        using (var tb = new SolidBrush(Color.White))
            g.DrawString(text, _badgeFont, tb, bx + 6, by + 3);
    }

    private void DrawHint(Graphics g, Point p)
    {
        const string text = "드래그하여 영역 선택   ·   Esc 또는 우클릭으로 취소";
        var sz = g.MeasureString(text, _hintFont);
        var mon = MonitorClientRect(p);
        float x = mon.Left + (mon.Width - sz.Width) / 2f;
        float y = mon.Top + 48;
        using (var b = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
            g.FillRectangle(b, x - 14, y - 8, sz.Width + 28, sz.Height + 16);
        using (var tb = new SolidBrush(Color.White))
            g.DrawString(text, _hintFont, tb, x, y);
    }

    private void DrawLoupe(Graphics g, Point p)
    {
        const int srcSize = 24;   // 샘플 픽셀(24x24)
        const int mag = 6;        // 확대 배율
        const int size = srcSize * mag;   // 돋보기 한 변(144)

        var mon = MonitorClientRect(p);
        int lx = p.X + 22, ly = p.Y + 22;
        if (lx + size > mon.Right - 8) lx = p.X - 22 - size;
        if (ly + size + 26 > mon.Bottom - 8) ly = p.Y - 22 - size - 26;
        if (lx < mon.Left + 8) lx = mon.Left + 8;
        if (ly < mon.Top + 8) ly = mon.Top + 8;

        int sx = Math.Clamp(p.X - srcSize / 2, 0, Math.Max(0, _frozen.Width - srcSize));
        int sy = Math.Clamp(p.Y - srcSize / 2, 0, Math.Max(0, _frozen.Height - srcSize));
        var srcRect = new Rectangle(sx, sy, srcSize, srcSize);
        var destRect = new Rectangle(lx, ly, size, size);

        var oldInterp = g.InterpolationMode;
        var oldOffset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_frozen, destRect, srcRect, GraphicsUnit.Pixel);
        g.InterpolationMode = oldInterp;
        g.PixelOffsetMode = oldOffset;

        using (var outer = new Pen(Color.Black, 1))
            g.DrawRectangle(outer, destRect.X - 1, destRect.Y - 1, destRect.Width + 1, destRect.Height + 1);
        using (var inner = new Pen(Color.White, 1))
            g.DrawRectangle(inner, destRect);

        // 돋보기 십자 = 실제 커서 픽셀 위치(가장자리에서 srcRect가 클램프돼도 정확히 표시)
        int cx = lx + (p.X - sx) * mag + mag / 2;
        int cy = ly + (p.Y - sy) * mag + mag / 2;
        using (var cross = new Pen(Color.FromArgb(220, 30, 144, 255)))
        {
            g.DrawLine(cross, lx, cy, lx + size, cy);
            g.DrawLine(cross, cx, ly, cx, ly + size);
        }

        // 좌표 + 색상 판독
        var col = ColorAt(p);
        var readout = $"{p.X}, {p.Y}  #{col.R:X2}{col.G:X2}{col.B:X2}";
        var rsz = g.MeasureString(readout, _readoutFont);
        var rbg = new RectangleF(lx, ly + size + 3, Math.Max(size, rsz.Width + 20), rsz.Height + 4);
        using (var b = new SolidBrush(Color.FromArgb(215, 0, 0, 0)))
            g.FillRectangle(b, rbg);
        using (var sw = new SolidBrush(col))
            g.FillRectangle(sw, rbg.X + 3, rbg.Y + 3, 10, rsz.Height - 2);
        using (var swb = new Pen(Color.White))
            g.DrawRectangle(swb, rbg.X + 3, rbg.Y + 3, 10, rsz.Height - 2);
        using (var tb = new SolidBrush(Color.White))
            g.DrawString(readout, _readoutFont, tb, rbg.X + 18, rbg.Y + 2);
    }

    private Color ColorAt(Point p)
    {
        int x = Math.Clamp(p.X, 0, _frozen.Width - 1);
        int y = Math.Clamp(p.Y, 0, _frozen.Height - 1);
        return _frozen.GetPixel(x, y);
    }

    /// <summary>커서가 있는 모니터의 영역(클라이언트 좌표계).</summary>
    private Rectangle MonitorClientRect(Point clientPt)
    {
        var screenPt = new Point(clientPt.X + Bounds.Left, clientPt.Y + Bounds.Top);
        var b = Screen.FromPoint(screenPt).Bounds;
        return new Rectangle(b.Left - Bounds.Left, b.Top - Bounds.Top, b.Width, b.Height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        _start = e.Location;
        _dragging = true;
        Invalidate();   // 유휴 시 그린 십자선·힌트를 한 번에 지움(드래그 중엔 부분 갱신)
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var prevCursor = _cursor;
        var prevSel = _sel;
        _cursor = e.Location;

        if (_dragging)
        {
            _sel = Rectangle.FromLTRB(
                Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y),
                Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y));

            // 드래그 중(십자선 없음): 돋보기 박스 + 선택영역 변화만 무효화 → 전체 재블릿 방지
            Invalidate(CursorBox(prevCursor));
            Invalidate(CursorBox(_cursor));
            var selDirty = prevSel.IsEmpty ? _sel : Rectangle.Union(prevSel, _sel);
            Invalidate(Rectangle.Inflate(selDirty, 60, 60));
        }
        else
        {
            // 유휴(십자선이 화면 가로지름): 얇은 십자 스트립 + 돋보기 박스 + 힌트 밴드만
            InvalidateCross(prevCursor);
            InvalidateCross(_cursor);
            Invalidate(CursorBox(prevCursor));
            Invalidate(CursorBox(_cursor));
            Invalidate(HintBand(prevCursor));
            Invalidate(HintBand(_cursor));
        }
    }

    // 돋보기+판독이 어느 방향으로 뒤집혀도 덮는 넉넉한 커서 주변 박스
    private static Rectangle CursorBox(Point p) => new(p.X - 210, p.Y - 210, 420, 470);

    private void InvalidateCross(Point p)
    {
        Invalidate(new Rectangle(0, p.Y - 2, Width, 5));
        Invalidate(new Rectangle(p.X - 2, 0, 5, Height));
    }

    private Rectangle HintBand(Point p)
    {
        var m = MonitorClientRect(p);
        return new Rectangle(m.Left, m.Top + 24, m.Width, 72);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        if (_sel.Width >= 4 && _sel.Height >= 4)
        {
            var crop = _sel;
            crop.Intersect(new Rectangle(0, 0, _frozen.Width, _frozen.Height));
            if (crop.Width >= 4 && crop.Height >= 4)
            {
                Result = _frozen.Clone(crop, _frozen.PixelFormat);
                DialogResult = DialogResult.OK;
            }
        }
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dimmed?.Dispose();
            _badgeFont.Dispose();
            _hintFont.Dispose();
            _readoutFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
