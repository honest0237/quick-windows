using System;
using System.Collections.Generic;

namespace Quick.Core;

/// <summary>전역 단축키 수정자. 값은 Win32 MOD_* 상수와 동일하게 맞춤(그대로 RegisterHotKey에 전달 가능).</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,      // MOD_ALT
    Control = 0x0002,  // MOD_CONTROL
    Shift = 0x0004,    // MOD_SHIFT
    Win = 0x0008,      // MOD_WIN
}

/// <summary>전역 단축키 정의 — 수정자 + 가상키(VK). 순수 로직(포맷·검증)만 Core에 두어 어디서나 테스트.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    private const HotkeyModifiers AllModifiers =
        HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win;

    /// <summary>전역 단축키로 안전하려면 수정자 1개 이상(정의된 비트만) + 허용된 키여야 함(맨키·손상값 방지).</summary>
    public bool IsValid =>
        Modifiers != HotkeyModifiers.None &&
        (Modifiers & ~AllModifiers) == 0 &&
        HotkeyKeys.Name(VirtualKey) is not null;

    /// <summary>"Ctrl+Shift+Q" 형태로 표시.</summary>
    public string Format()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(HotkeyKeys.Name(VirtualKey) ?? $"0x{VirtualKey:X2}");
        return string.Join("+", parts);
    }

    public override string ToString() => Format();
}

/// <summary>가상키(VK) ↔ 표시 이름. 전역 단축키에 쓸 만한 키만 화이트리스트로.</summary>
public static class HotkeyKeys
{
    private static readonly Dictionary<int, string> Names = Build();

    /// <summary>허용된 키면 표시 이름, 아니면 null.</summary>
    public static string? Name(int vk) => Names.TryGetValue(vk, out var n) ? n : null;

    public static bool IsAllowed(int vk) => Names.ContainsKey(vk);

    private static Dictionary<int, string> Build()
    {
        var d = new Dictionary<int, string>();
        for (int c = 'A'; c <= 'Z'; c++) d[c] = ((char)c).ToString();        // A–Z  0x41–0x5A
        for (int n = 0; n <= 9; n++) d[0x30 + n] = n.ToString();            // 0–9  0x30–0x39
        for (int f = 1; f <= 12; f++) d[0x70 + (f - 1)] = "F" + f;           // F1–F12 0x70–0x7B
        d[0x20] = "Space";
        // PrintScreen(0x2C)은 WM_KEYUP 로만 와서 HotkeyBox(ProcessCmdKey)로는 못 잡으므로 제외
        d[0x2D] = "Insert";
        d[0x2E] = "Delete";
        d[0x24] = "Home";
        d[0x23] = "End";
        d[0x21] = "PageUp";
        d[0x22] = "PageDown";
        d[0xBC] = ",";   d[0xBE] = ".";   d[0xBF] = "/";   d[0xC0] = "`";
        d[0xDB] = "[";   d[0xDD] = "]";   d[0xBA] = ";";   d[0xDE] = "'";
        d[0xBD] = "-";   d[0xBB] = "=";   d[0xDC] = "\\";
        return d;
    }
}
