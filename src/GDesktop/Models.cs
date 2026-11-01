namespace GDesktop;

public record Root(long Id, string Path);
public record Document(long Id, string Path, string Name, string Extension, long Size, long Mtime, string State, string? Error, int CloudFlags);
public record SearchHit(long Id, string Name, string Path, string Extension, long Size, string State, string? Error, string Snippet);
public record ExtractedPage(int Page, string Text);
public record Extracted(List<ExtractedPage> Pages, string Status, List<int>? OcrPages = null);
public record AddRoot(string Path);
public record RootAddition(Root Root, bool Added, List<Root> Consolidated);
public record Login(string Code);
