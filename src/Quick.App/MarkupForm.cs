using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>캡처 후 마크업 편집기 — Windows 캡처도구 능가.
/// 화살표·사각형·펜·형광펜·텍스트·모자이크(가림) + 실행취소 + 복사/저장/텍스트복사(OCR).</summary>
public sealed class MarkupForm : Form
{
    private readonly MarkupCanvas _canvas;
    private readonly Label _status;

    /// <summary>'저장' 시 편집 반영된 결과 비트맵(호출자가 저장·색인·해제).</summary>
    public Bitmap? RenderedResult { get; private set; }

    public MarkupForm(Bitmap baseImage)
    {
        Text = "Quick 편집";
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon.Value;
        KeyPreview = true;

        _canvas = new MarkupCanvas(baseImage);

        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        int w = Math.Min(baseImage.Width + 18, (int)(wa.Width * 0.9));
        int h = Math.Min(baseImage.Height + 96, (int)(wa.Height * 0.9));
        ClientSize = new Size(Math.Max(w, 640), Math.Max(h, 360));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(6, 7, 6, 6),
            WrapContents = false,
            AutoScroll = false,
            BackColor = SystemColors.Control,
        };

        Button ToolBtn(string text, MarkupTool tool)
        {
            var b = new Button { Text = text, AutoSize = false, Width = 64, Height = 30, FlatStyle = FlatStyle.System, Margin = new Padding(2, 0, 2, 0) };
            b.Click += (_, _) => { _canvas.Tool = tool; HighlightTool(tool); };
            b.Tag = tool;
            return b;
        }

        _toolButtons = new[]
        {
            ToolBtn("↗ 화살표", MarkupTool.Arrow),
            ToolBtn("▭ 사각형", MarkupTool.Rect),
            ToolBtn("✎ 펜", MarkupTool.Pen),
            ToolBtn("🖍 형광", MarkupTool.Highlight),
            ToolBtn("T 텍스트", MarkupTool.Text),
            ToolBtn("▦ 모자이크", MarkupTool.Mosaic),
        };
        foreach (var b in _toolButtons) toolbar.Controls.Add(b);

        toolbar.Controls.Add(new Label { Text = "", Width = 8 });
        foreach (var c in new[] { Color.Red, Color.Yellow, Color.LimeGreen, Color.DodgerBlue, Color.Black, Color.White })
        {
            var sw = new Button { BackColor = c, Width = 24, Height = 24, FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 3, 2, 3) };
            sw.FlatAppearance.BorderColor = Color.Gray;
            sw.Click += (_, _) => { _canvas.StrokeColor = c; };
            toolbar.Controls.Add(sw);
        }

        var thick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 74, Margin = new Padding(6, 2, 2, 2) };
        thick.Items.AddRange(new object[] { "얇게", "보통", "굵게" });
        thick.SelectedIndex = 1;
        thick.SelectedIndexChanged += (_, _) => _canvas.Thickness = thick.SelectedIndex switch { 0 => 2, 2 => 8, _ => 4 };
        toolbar.Controls.Add(thick);

        var undo = new Button { Text = "실행취소", Width = 68, Height = 30, FlatStyle = FlatStyle.System, Margin = new Padding(8, 0, 2, 0) };
        undo.Click += (_, _) => _canvas.Undo();
        toolbar.Controls.Add(undo);

        var ocr = new Button { Text = "텍스트 복사", Width = 88, Height = 30, FlatStyle = FlatStyle.System, Margin = new Padding(12, 0, 2, 0) };
        ocr.Click += async (_, _) => await CopyTextAsync();
        toolbar.Controls.Add(ocr);

        var copy = new Button { Text = "복사", Width = 60, Height = 30, FlatStyle = FlatStyle.System, Margin = new Padding(2, 0, 2, 0) };
        copy.Click += (_, _) => CopyImage();
        toolbar.Controls.Add(copy);

        var save = new Button { Text = "저장", Width = 60, Height = 30, FlatStyle = FlatStyle.System, Margin = new Padding(2, 0, 2, 0) };
        save.Click += (_, _) => SaveAndClose();
        toolbar.Controls.Add(save);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(40, 40, 40) };
        scroll.Controls.Add(_canvas);

        _status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), ForeColor = SystemColors.GrayText, Text = "도구를 고르고 이미지 위에 그리세요 · Ctrl+Z 실행취소" };

        Controls.Add(scroll);
        Controls.Add(_status);
        Controls.Add(toolbar);

        AcceptButton = save;
        HighlightTool(MarkupTool.Arrow);
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) { _canvas.Undo(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
    }

    private readonly Button[] _toolButtons;

    private void HighlightTool(MarkupTool tool)
    {
        foreach (var b in _toolButtons)
            b.BackColor = (MarkupTool)b.Tag! == tool ? Color.FromArgb(210, 228, 255) : SystemColors.Control;
    }

    private async Task CopyTextAsync()
    {
        _status.Text = "텍스트 인식 중…";
        using var render = _canvas.Render();
        var text = await OcrService.RecognizeAsync(render);
        if (string.IsNullOrWhiteSpace(text))
        {
            _status.Text = "인식된 텍스트가 없어요.";
            return;
        }
        try { Clipboard.SetText(text); _status.Text = "텍스트를 클립보드에 복사했어요."; }
        catch { _status.Text = "복사 실패."; }
    }

    private void CopyImage()
    {
        try
        {
            using var render = _canvas.Render();
            Clipboard.SetImage(render);
            _status.Text = "이미지를 클립보드에 복사했어요.";
        }
        catch { _status.Text = "복사 실패."; }
    }

    private void SaveAndClose()
    {
        RenderedResult = _canvas.Render();   // 호출자가 저장·색인·해제
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal enum MarkupTool { Arrow, Rect, Pen, Highlight, Text, Mosaic }

/// <summary>1:1 드로잉 캔버스(스크롤 패널 안). 마우스로 주석 생성, 페인트로 렌더.</summary>
internal sealed class MarkupCanvas : Control
{
    private readonly Bitmap _base;   // 호출자 소유(여기선 해제 안 함)
    private readonly List<Annotation> _annos = new();
    private Annotation? _drawing;

    public MarkupTool Tool = MarkupTool.Arrow;
    public Color StrokeColor = Color.Red;
    public int Thickness = 4;

    public MarkupCanvas(Bitmap baseImage)
    {
        _base = baseImage;
        Size = _base.Size;
        DoubleBuffered = true;
        TabStop = true;
    }

    public void Undo()
    {
        if (_annos.Count > 0) { _annos.RemoveAt(_annos.Count - 1); Invalidate(); }
    }

    /// <summary>배경 + 확정된 주석을 합성한 새 비트맵(호출자 해제).</summary>
    public Bitmap Render()
    {
        var bmp = new Bitmap(_base.Width, _base.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImageUnscaled(_base, 0, 0);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var a in _annos) a.Draw(g, _base);
        return bmp;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_base, 0, 0);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var a in _annos) a.Draw(g, _base);
        _drawing?.Draw(g, _base);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Focus();
        switch (Tool)
        {
            case MarkupTool.Arrow: _drawing = new ArrowAnno { Color = StrokeColor, Thickness = Thickness, A = e.Location, B = e.Location }; break;
            case MarkupTool.Rect: _drawing = new RectAnno { Color = StrokeColor, Thickness = Thickness, A = e.Location, B = e.Location }; break;
            case MarkupTool.Mosaic: _drawing = new MosaicAnno { Color = StrokeColor, Thickness = Thickness, A = e.Location, B = e.Location }; break;
            case MarkupTool.Pen: { var p = new PenAnno { Color = StrokeColor, Thickness = Thickness }; p.Pts.Add(e.Location); _drawing = p; break; }
            case MarkupTool.Highlight: { var h = new HighlightAnno { Color = StrokeColor, Thickness = Thickness }; h.Pts.Add(e.Location); _drawing = h; break; }
            case MarkupTool.Text:
                var txt = PromptForm.Ask(FindForm(), "텍스트 입력");
                if (!string.IsNullOrEmpty(txt))
                {
                    _annos.Add(new TextAnno { Color = StrokeColor, Thickness = Thickness, Loc = e.Location, Text = txt });
                    Invalidate();
                }
                _drawing = null;
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drawing is null) return;
        switch (_drawing)
        {
            case ArrowAnno a: a.B = e.Location; break;
            case RectAnno r: r.B = e.Location; break;
            case MosaicAnno m: m.B = e.Location; break;
            case PenAnno p: p.Pts.Add(e.Location); break;
            case HighlightAnno h: h.Pts.Add(e.Location); break;
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_drawing is null) return;

        // 클릭만 하고 안 끈 도형은 버림(자잘한 점 방지)
        bool keep = _drawing switch
        {
            ArrowAnno a => Dist(a.A, a.B) >= 3,
            RectAnno r => Math.Abs(r.A.X - r.B.X) >= 3 && Math.Abs(r.A.Y - r.B.Y) >= 3,
            MosaicAnno m => Math.Abs(m.A.X - m.B.X) >= 4 && Math.Abs(m.A.Y - m.B.Y) >= 4,
            PenAnno p => p.Pts.Count >= 2,
            HighlightAnno h => h.Pts.Count >= 2,
            _ => true,
        };
        if (keep) _annos.Add(_drawing);
        _drawing = null;
        Invalidate();
    }

    private static double Dist(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    // ── 주석 타입 ──────────────────────────────────────────
    private abstract class Annotation
    {
        public Color Color;
        public int Thickness;
        public abstract void Draw(Graphics g, Bitmap baseImg);
    }

    private sealed class ArrowAnno : Annotation
    {
        public Point A, B;
        public override void Draw(Graphics g, Bitmap baseImg)
        {
            using var pen = new Pen(Color, Thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, A, B);
            double ang = Math.Atan2(B.Y - A.Y, B.X - A.X) + Math.PI;   // B→A 방향
            double len = 10 + Thickness * 2.0, spread = 0.5;
            g.DrawLine(pen, B.X, B.Y, (float)(B.X + len * Math.Cos(ang - spread)), (float)(B.Y + len * Math.Sin(ang - spread)));
            g.DrawLine(pen, B.X, B.Y, (float)(B.X + len * Math.Cos(ang + spread)), (float)(B.Y + len * Math.Sin(ang + spread)));
        }
    }

    private sealed class RectAnno : Annotation
    {
        public Point A, B;
        public override void Draw(Graphics g, Bitmap baseImg)
        {
            using var pen = new Pen(Color, Thickness);
            g.DrawRectangle(pen, Rectangle.FromLTRB(Math.Min(A.X, B.X), Math.Min(A.Y, B.Y), Math.Max(A.X, B.X), Math.Max(A.Y, B.Y)));
        }
    }

    private sealed class PenAnno : Annotation
    {
        public readonly List<Point> Pts = new();
        public override void Draw(Graphics g, Bitmap baseImg)
        {
            using var pen = new Pen(Color, Thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            if (Pts.Count >= 2) g.DrawLines(pen, Pts.ToArray());
            else if (Pts.Count == 1) { using var b = new SolidBrush(Color); g.FillEllipse(b, Pts[0].X - Thickness / 2f, Pts[0].Y - Thickness / 2f, Thickness, Thickness); }
        }
    }

    private sealed class HighlightAnno : Annotation
    {
        public readonly List<Point> Pts = new();
        public override void Draw(Graphics g, Bitmap baseImg)
        {
            if (Pts.Count < 2) return;
            // 이 클래스엔 'Color' 필드가 있어 타입 Color를 가리므로 정규화 필요
            var c = System.Drawing.Color.FromArgb(110, Color);
            using var pen = new Pen(c, Thickness * 4) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, Pts.ToArray());
        }
    }

    private sealed class TextAnno : Annotation
    {
        public Point Loc;
        public string Text = "";
        public override void Draw(Graphics g, Bitmap baseImg)
        {
            using var f = new Font("Segoe UI", 14 + Thickness * 2, FontStyle.Bold, GraphicsUnit.Pixel);
            using var b = new SolidBrush(Color);
            g.DrawString(Text, f, b, Loc);
        }
    }

    private sealed class MosaicAnno : Annotation
    {
        public Point A, B;
        public override void Draw(Graphics g, Bitmap baseImg)
        {
            var r = Rectangle.FromLTRB(Math.Min(A.X, B.X), Math.Min(A.Y, B.Y), Math.Max(A.X, B.X), Math.Max(A.Y, B.Y));
            r.Intersect(new Rectangle(0, 0, baseImg.Width, baseImg.Height));
            if (r.Width < 2 || r.Height < 2) return;

            int block = Math.Max(6, Math.Min(r.Width, r.Height) / 12);
            int sw = Math.Max(1, r.Width / block), sh = Math.Max(1, r.Height / block);
            using var small = new Bitmap(sw, sh);
            using (var sg = Graphics.FromImage(small))
            {
                sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                sg.DrawImage(baseImg, new Rectangle(0, 0, sw, sh), r, GraphicsUnit.Pixel);
            }
            var oi = g.InterpolationMode; var op = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(small, r);
            g.InterpolationMode = oi; g.PixelOffsetMode = op;
        }
    }
}

/// <summary>텍스트 한 줄 입력용 소형 모달.</summary>
internal static class PromptForm
{
    public static string? Ask(IWin32Window? owner, string title)
    {
        using var f = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(324, 96),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
        };
        var tb = new TextBox { Location = new Point(12, 14), Width = 300 };
        var ok = new Button { Text = "확인", DialogResult = DialogResult.OK, Location = new Point(160, 52), Size = new Size(72, 28) };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Location = new Point(240, 52), Size = new Size(72, 28) };
        f.Controls.AddRange(new Control[] { tb, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog(owner) == DialogResult.OK && tb.Text.Length > 0 ? tb.Text : null;
    }
}
