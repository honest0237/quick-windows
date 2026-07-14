namespace Quick.Core;

/// <summary>메모리 항목 — 예전에 찍은 스크린샷 하나(OCR 전문 색인). Swift의 MemoryEntry와 동일.</summary>
public sealed record MemoryEntry(string Path, DateTimeOffset Date, string Title, string Text);
