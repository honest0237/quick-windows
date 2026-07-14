using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>앱 전체 디자인 토큰 — 기본 WinForms 룩 대신 세련된 통일감(강조색·색·폰트·둥근 카드).</summary>
internal static class Theme
{
    // 색
    public static readonly Color Accent = Color.FromArgb(47, 129, 247);    // #2F81F7 (아이콘 번개색)
    public static readonly Color AccentHover = Color.FromArgb(31, 111, 235);
    public static readonly Color HeaderBg = Color.FromArgb(22, 25, 32);    // 어두운 헤더
    public static readonly Color HeaderSub = Color.FromArgb(150, 158, 172);
    public static readonly Color Bg = Color.FromArgb(248, 249, 251);       // 패널 배경
    public static readonly Color CardBg = Color.White;
    public static readonly Color CardHover = Color.FromArgb(240, 245, 254);
    public static readonly Color CardSelected = Color.FromArgb(224, 236, 254);
    public static readonly Color Border = Color.FromArgb(228, 230, 235);
    public static readonly Color Text = Color.FromArgb(24, 27, 34);
    public static readonly Color SubText = Color.FromArgb(122, 128, 140);
    public static readonly Color Snippet = Color.FromArgb(150, 156, 166);

    // 폰트 (앱 수명 동안 유지)
    public static readonly Font Title = new("Segoe UI Semibold", 10.5F);
    public static readonly Font TitleBig = new("Segoe UI Semibold", 12F);
    public static readonly Font Body = new("Segoe UI", 9.5F);
    public static readonly Font Small = new("Segoe UI", 8.25F);
    public static readonly Font Icon = new("Segoe UI", 13F);     // 톱니 등 아이콘 글리프
    public static readonly Font Field = new("Segoe UI", 11F);    // 검색 입력

    /// <summary>둥근 사각형 경로.</summary>
    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        if (d <= 0 || r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
        d = Math.Min(d, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>납작한 강조/보조 버튼 스타일.</summary>
    public static void FlatButton(Button b, bool accent = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = accent ? 0 : 1;
        b.FlatAppearance.BorderColor = Border;
        b.UseVisualStyleBackColor = false;
        b.BackColor = accent ? Accent : Color.White;
        b.ForeColor = accent ? Color.White : Text;
        b.Font = Body;
        b.FlatAppearance.MouseOverBackColor = accent ? AccentHover : CardHover;
        b.Cursor = Cursors.Hand;
    }
}
