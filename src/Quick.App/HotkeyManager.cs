using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>전역 단축키 (Win32 RegisterHotKey). macOS Carbon 핫키의 Windows 대응.
/// 기본: Ctrl+Shift+Q (Windows에서 충돌 적음; Alt+Space는 시스템 메뉴라 회피).</summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 1;

    // MOD_CONTROL(0x0002) | MOD_SHIFT(0x0004), VK 'Q'(0x51)
    private const uint Modifiers = 0x0002 | 0x0004;
    private const uint VkQ = 0x51;

    public const string Label = "Ctrl+Shift+Q";

    private readonly Action _onPressed;

    public HotkeyManager(Action onPressed)
    {
        _onPressed = onPressed;
        CreateHandle(new CreateParams());
        RegisterHotKey(Handle, HotkeyId, Modifiers, VkQ);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam == HotkeyId)
            _onPressed();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle();
    }
}
