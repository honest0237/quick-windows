using System.Drawing;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>설정 창. macOS 설정 팝오버 대응 — 자동복사·소리·저장형식·저장폴더 + 버전/단축키.</summary>
public sealed class SettingsForm : Form
{
    public SettingsForm()
    {
        var s = Settings.Current;

        Text = "Quick 설정";
        ClientSize = new Size(390, 356);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon.Value;

        int y = 16;

        var version = new Label
        {
            Text = $"Quick   v{UpdateService.CurrentVersion}",
            Font = new Font("Segoe UI Semibold", 12F),
            AutoSize = true,
            Location = new Point(20, y),
        };
        y += 44;

        var autoCopy = new CheckBox { Text = "새 캡처 자동 복사", Checked = s.AutoCopy, AutoSize = true, Location = new Point(20, y) };
        autoCopy.CheckedChanged += (_, _) => { s.AutoCopy = autoCopy.Checked; s.Save(); };
        y += 28;

        var sound = new CheckBox { Text = "캡처 시 소리", Checked = s.SoundOnCapture, AutoSize = true, Location = new Point(20, y) };
        sound.CheckedChanged += (_, _) => { s.SoundOnCapture = sound.Checked; s.Save(); };
        y += 38;

        var fmtLabel = new Label { Text = "저장 형식", ForeColor = SystemColors.GrayText, AutoSize = true, Location = new Point(20, y + 4) };
        var fmt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(120, y), Width = 110 };
        fmt.Items.AddRange(new object[] { "PNG", "JPEG" });
        fmt.SelectedIndex = s.Format.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        fmt.SelectedIndexChanged += (_, _) => { s.Format = fmt.SelectedIndex == 1 ? "jpeg" : "png"; s.Save(); };
        y += 40;

        var dirLabel = new Label { Text = "저장 폴더", ForeColor = SystemColors.GrayText, AutoSize = true, Location = new Point(20, y + 4) };
        var dirVal = new Label
        {
            Text = Shorten(s.EffectiveSaveDir()),
            AutoEllipsis = true,
            Size = new Size(170, 20),
            Location = new Point(120, y + 4),
        };
        var dirBtn = new Button { Text = "변경", Location = new Point(300, y), Size = new Size(70, 26) };
        dirBtn.Click += (_, _) =>
        {
            using var fb = new FolderBrowserDialog { SelectedPath = s.EffectiveSaveDir() };
            if (fb.ShowDialog() == DialogResult.OK)
            {
                s.SaveDirectory = fb.SelectedPath;
                s.Save();
                dirVal.Text = Shorten(fb.SelectedPath);
            }
        };
        y += 44;

        var hotkeys = new Label
        {
            ForeColor = SystemColors.GrayText,
            Font = new Font("Segoe UI", 9F),
            Size = new Size(350, 62),
            Location = new Point(20, y),
            Text = "단축키\r\n" +
                   "Ctrl+Shift+4  영역 캡처    ·    Ctrl+Shift+3  전체 캡처\r\n" +
                   "Ctrl+Shift+Q  패널 열기    ·    Esc  닫기",
        };
        y += 66;

        var close = new Button { Text = "닫기", Location = new Point(290, y), Size = new Size(80, 30), DialogResult = DialogResult.OK };
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { version, autoCopy, sound, fmtLabel, fmt, dirLabel, dirVal, dirBtn, hotkeys, close });
        AcceptButton = close;
    }

    private static string Shorten(string path) => path.Length > 26 ? "…" + path[^25..] : path;
}
