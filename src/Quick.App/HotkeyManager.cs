using System.Runtime.InteropServices;
using System.Windows.Forms;
using Quick.Core;

namespace Quick.App;

/// <summary>전역 단축키 (Win32 RegisterHotKey). 여러 개 등록 + 재등록 지원.</summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager() => CreateHandle(new CreateParams());

    /// <summary>단축키 등록. 충돌 등으로 실패하면 false.</summary>
    public bool Register(uint modifiers, uint vk, Action action)
    {
        int id = _nextId++;
        if (RegisterHotKey(Handle, id, modifiers, vk))
        {
            _actions[id] = action;
            return true;
        }
        return false;
    }

    /// <summary>Core Hotkey 로 등록(무효하면 false).</summary>
    public bool Register(Hotkey hk, Action action) =>
        hk.IsValid && Register((uint)hk.Modifiers, (uint)hk.VirtualKey, action);

    /// <summary>모든 단축키 해제(재설정 전 호출).</summary>
    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            UnregisterHotKey(Handle, id);
        _actions.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action))
            action();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
