using System.Drawing;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>첫 실행 안내 창. macOS WelcomeWindow 대응 — 트레이 앱이라 첫 실행 시 무피드백 방지.</summary>
public sealed class WelcomeForm : Form
{
    public WelcomeForm()
    {
        Text = "Quick 시작하기";
        ClientSize = new Size(460, 380);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;

        var title = new Label
        {
            Text = "Quick — 스크린샷 선반",
            Font = new Font("Segoe UI Semibold", 16F),
            AutoSize = true,
            Location = new Point(24, 22),
        };
        var sub = new Label
        {
            Text = "트레이에 상주하며 스크린샷을 캡처·보관·검색합니다.",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Location = new Point(24, 56),
        };
        var body = new Label
        {
            Location = new Point(24, 96),
            Size = new Size(412, 210),
            Font = new Font("Segoe UI", 10.5F),
            Text =
                "· Ctrl + Shift + 4  —  영역 캡처 (드래그로 선택)\r\n" +
                "· Ctrl + Shift + 3  —  전체 화면 캡처\r\n" +
                "· Ctrl + Shift + Q  —  사이드 패널 열기\r\n\r\n" +
                "캡처한 스크린샷은 오른쪽 패널에 쌓이고, 자동으로 글자를 읽어\r\n" +
                "'인보이스', '에러' 같은 내용으로 검색됩니다.\r\n\r\n" +
                "메뉴바(트레이) 아이콘에서 언제든 다시 열 수 있어요.",
        };
        var start = new Button
        {
            Text = "시작하기",
            Size = new Size(120, 36),
            Location = new Point(316, 324),
            DialogResult = DialogResult.OK,
        };
        start.Click += (_, _) => Close();

        Controls.Add(title);
        Controls.Add(sub);
        Controls.Add(body);
        Controls.Add(start);
        AcceptButton = start;
    }
}
