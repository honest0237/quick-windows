using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Quick.Core;

namespace Quick.App;

/// <summary>설정 창. 자동복사·소리·저장형식·저장폴더 + 버전 + 사용자 지정 단축키.</summary>
public sealed class SettingsForm : Form
{
    /// <summary>사용자가 단축키를 바꾸면 발생 → Program 이 전역 단축키를 재등록.</summary>
    public static event Action? HotkeysChanged;

    public SettingsForm()
    {
        var s = Settings.Current;

        Text = "Quick 설정";
        ClientSize = new Size(410, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon.Value;
        BackColor = Theme.Bg;

        var version = new Label
        {
            Text = $"Quick   v{UpdateService.CurrentVersion}",
            Font = new Font("Segoe UI Semibold", 12F),
            AutoSize = true,
            Location = new Point(20, 16),
        };

        var autoCopy = new CheckBox { Text = "새 캡처 자동 복사", Checked = s.AutoCopy, AutoSize = true, Location = new Point(20, 58) };
        autoCopy.CheckedChanged += (_, _) => { s.AutoCopy = autoCopy.Checked; s.Save(); };

        var sound = new CheckBox { Text = "캡처 시 소리", Checked = s.SoundOnCapture, AutoSize = true, Location = new Point(20, 86) };
        sound.CheckedChanged += (_, _) => { s.SoundOnCapture = sound.Checked; s.Save(); };

        var editorChk = new CheckBox { Text = "캡처 후 편집기 열기", Checked = s.OpenEditorAfterCapture, AutoSize = true, Location = new Point(200, 58) };
        editorChk.CheckedChanged += (_, _) => { s.OpenEditorAfterCapture = editorChk.Checked; s.Save(); };

        var delayLbl = new Label { Text = "지연 캡처", ForeColor = SystemColors.GrayText, AutoSize = true, Location = new Point(200, 90) };
        var delay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(268, 86), Width = 116 };
        delay.Items.AddRange(new object[] { "없음", "3초", "5초" });
        delay.SelectedIndex = s.CaptureDelaySeconds switch { 3 => 1, 5 => 2, _ => 0 };
        delay.SelectedIndexChanged += (_, _) => { s.CaptureDelaySeconds = delay.SelectedIndex switch { 1 => 3, 2 => 5, _ => 0 }; s.Save(); };

        var fmtLabel = new Label { Text = "저장 형식", ForeColor = SystemColors.GrayText, AutoSize = true, Location = new Point(20, 122) };
        var fmt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(120, 118), Width = 120 };
        fmt.Items.AddRange(new object[] { "PNG", "JPEG" });
        fmt.SelectedIndex = s.Format.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        fmt.SelectedIndexChanged += (_, _) => { s.Format = fmt.SelectedIndex == 1 ? "jpeg" : "png"; s.Save(); };

        var dirLabel = new Label { Text = "저장 폴더", ForeColor = SystemColors.GrayText, AutoSize = true, Location = new Point(20, 162) };
        var dirVal = new Label { Text = Shorten(s.EffectiveSaveDir()), AutoEllipsis = true, Size = new Size(170, 20), Location = new Point(120, 162) };
        var dirBtn = new Button { Text = "변경", Location = new Point(300, 158), Size = new Size(80, 26) };
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

        // ── 단축키 ────────────────────────────────────────────────
        var hkHeader = new Label
        {
            Text = "단축키  (칸을 클릭한 뒤 원하는 조합을 누르세요)",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Location = new Point(20, 200),
        };

        var searchBox = new HotkeyBox { Location = new Point(120, 226), Size = new Size(200, 24), Value = s.SearchHotkey };
        var regionBox = new HotkeyBox { Location = new Point(120, 258), Size = new Size(200, 24), Value = s.CaptureRegionHotkey };
        var fullBox = new HotkeyBox { Location = new Point(120, 290), Size = new Size(200, 24), Value = s.CaptureFullHotkey };

        var searchLbl = new Label { Text = "패널 열기", AutoSize = true, Location = new Point(20, 230) };
        var regionLbl = new Label { Text = "영역 캡처", AutoSize = true, Location = new Point(20, 262) };
        var fullLbl = new Label { Text = "전체 캡처", AutoSize = true, Location = new Point(20, 294) };

        var conflict = new Label
        {
            ForeColor = Color.Firebrick,
            AutoSize = false,
            Size = new Size(370, 20),
            Location = new Point(20, 322),
            Visible = false,
        };

        void ApplyHotkeys()
        {
            s.SearchHotkey = searchBox.Value;
            s.CaptureRegionHotkey = regionBox.Value;
            s.CaptureFullHotkey = fullBox.Value;
            s.Save();

            var all = new[] { searchBox.Value, regionBox.Value, fullBox.Value };
            bool dup = all.GroupBy(h => h).Any(g => g.Count() > 1);
            conflict.Text = dup ? "⚠ 같은 조합이 중복됐어요 — 하나만 동작할 수 있어요" : "";
            conflict.Visible = dup;

            HotkeysChanged?.Invoke();   // Program 이 즉시 재등록
        }

        searchBox.Changed += ApplyHotkeys;
        regionBox.Changed += ApplyHotkeys;
        fullBox.Changed += ApplyHotkeys;

        var reset = new Button { Text = "단축키 기본값", Location = new Point(20, 356), Size = new Size(130, 30) };
        Theme.FlatButton(reset);
        reset.Click += (_, _) =>
        {
            searchBox.Value = Settings.DefaultSearch;
            regionBox.Value = Settings.DefaultCaptureRegion;
            fullBox.Value = Settings.DefaultCaptureFull;
            ApplyHotkeys();
        };

        var close = new Button { Text = "닫기", Location = new Point(300, 356), Size = new Size(80, 30), DialogResult = DialogResult.OK };
        Theme.FlatButton(close, accent: true);
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            version, autoCopy, sound, editorChk, delayLbl, delay, fmtLabel, fmt, dirLabel, dirVal, dirBtn,
            hkHeader, searchLbl, regionLbl, fullLbl, searchBox, regionBox, fullBox,
            conflict, reset, close,
        });
        AcceptButton = close;
        CancelButton = close;   // Esc 로도 닫힘
    }

    private static string Shorten(string path) => path.Length > 26 ? "…" + path[^25..] : path;
}

/// <summary>단축키 입력 칸 — 포커스 상태에서 누른 조합(수정자+키)을 기록. 직접 타이핑 불가.</summary>
internal sealed class HotkeyBox : TextBox
{
    private Hotkey _value = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x51);

    public event Action? Changed;

    // 코드로만 UI를 구성하므로 디자이너 직렬화 불필요(WinForms WFO1000 해소).
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Hotkey Value
    {
        get => _value;
        set { _value = value; Text = value.Format(); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);
    private static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        ImeMode = ImeMode.Disable;   // 한글 IME가 키를 가로채(VK_PROCESSKEY) 조합을 놓치지 않게
        TextAlign = HorizontalAlignment.Center;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9.5F);
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        BackColor = Color.FromArgb(225, 238, 255);   // 입력 대기 시각 표시
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        BackColor = SystemColors.Window;
        Text = _value.Format();   // 안내 문구가 남아있어도 값으로 복원
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Focused) return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;

        // 수정자 단독 → 진짜 키를 기다림
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None)
            return true;

        // Tab/Esc/Enter 는 폼 이동·닫기로 넘김(칸이 키를 삼켜 다이얼로그가 멈춘 듯 보이지 않게)
        if (key is Keys.Tab or Keys.Escape or Keys.Return)
            return base.ProcessCmdKey(ref msg, keyData);

        var mods = HotkeyModifiers.None;
        if ((keyData & Keys.Control) == Keys.Control) mods |= HotkeyModifiers.Control;
        if ((keyData & Keys.Alt) == Keys.Alt) mods |= HotkeyModifiers.Alt;
        if ((keyData & Keys.Shift) == Keys.Shift) mods |= HotkeyModifiers.Shift;
        if (Down(0x5B) || Down(0x5C)) mods |= HotkeyModifiers.Win;   // VK_LWIN/RWIN

        if (mods == HotkeyModifiers.None)
        {
            Text = "Ctrl/Alt/Shift 를 포함하세요";
            return true;
        }
        if (!HotkeyKeys.IsAllowed((int)key))
        {
            Text = "지원하지 않는 키예요";
            return true;
        }

        Value = new Hotkey(mods, (int)key);
        Changed?.Invoke();
        return true;
    }
}
