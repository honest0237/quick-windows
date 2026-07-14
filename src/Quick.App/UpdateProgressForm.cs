using System.Drawing;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>업데이트 다운로드 진행률 + 취소(작은 모달리스 창).
/// X/Alt+F4/Esc 로는 닫히지 않고 '취소'로만 처리 → 다운로드/프로세스 정합성 유지.</summary>
internal sealed class UpdateProgressForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _label;
    private readonly Button _cancel;
    private bool _allowClose;

    /// <summary>사용자가 취소를 요청함(취소 버튼/닫기 시도/Esc).</summary>
    public event Action? CancelRequested;

    public UpdateProgressForm(string version)
    {
        Text = "Quick 업데이트";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(400, 128);
        try { Icon = AppIcon.Value; } catch { /* 무시 */ }

        _label = new Label
        {
            Text = $"새 버전 v{version} 다운로드 중…",
            Location = new Point(16, 14),
            Size = new Size(368, 22),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _bar = new ProgressBar
        {
            Location = new Point(16, 44),
            Size = new Size(368, 22),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
        };
        _cancel = new Button
        {
            Text = "취소",
            Location = new Point(154, 82),
            Size = new Size(92, 30),
        };
        _cancel.Click += (_, _) => RequestCancel();

        Controls.Add(_label);
        Controls.Add(_bar);
        Controls.Add(_cancel);
        CancelButton = _cancel;   // Esc → 취소 버튼 클릭
    }

    private void RequestCancel()
    {
        if (!_cancel.Enabled) return;
        _cancel.Enabled = false;
        _cancel.Text = "취소 중…";
        _label.Text = "취소하는 중…";
        CancelRequested?.Invoke();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 사용자가 닫으려 하면 닫지 말고 취소로 처리(폼 파괴 후 콜백 예외/무단 설치 방지).
        if (!_allowClose)
        {
            e.Cancel = true;
            RequestCancel();
            return;
        }
        base.OnFormClosing(e);
    }

    /// <summary>호출자만 실제로 닫을 수 있음.</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>0.0~1.0 진행률 반영. UI 스레드에서 호출될 것(Progress&lt;double&gt; 사용).</summary>
    public void SetProgress(double p)
    {
        if (IsDisposed || Disposing) return;
        var pct = (int)Math.Round(Math.Clamp(p, 0.0, 1.0) * 100);
        _bar.Value = Math.Clamp(pct, _bar.Minimum, _bar.Maximum);
        if (p >= 1.0 && _cancel.Enabled) _label.Text = "설치 준비 중…";
    }
}
