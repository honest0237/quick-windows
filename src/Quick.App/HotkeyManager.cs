using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Quick.App;

/// <summary>전역 단축키 (Win32 RegisterHotKey). 여러 개 등록 지원.</summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    // 수정자
    public const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004;
    // 가상 키
    public const uint VkQ = 0x51, Vk3 = 0x33, Vk4 = 0x34;

    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager() => CreateHandle(new CreateParams());

    /// <summary>단축키 등록. 실패해도(충돌 등) 조용히 무시.</summary>
    public void Register(uint modifiers, uint vk, Action action)
    {
        int id = _nextId++;
        if (RegisterHotKey(Handle, id, modifiers, vk))
            _actions[id] = action;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action))
            action();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
            UnregisterHotKey(Handle, id);
        _actions.Clear();
        DestroyHandle();
    }
}
