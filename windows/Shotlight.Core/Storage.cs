using System.Text.Json;

namespace Shotlight.Core;

public sealed record CaptureDraft
{
    public int FormatVersion { get; init; } = 1;
    public required Guid Id { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public List<Annotation> Marks { get; init; } = [];
    public List<List<Annotation>> UndoHistory { get; init; } = [];
    public List<List<Annotation>> RedoHistory { get; init; } = [];
    public int ThumbnailVersion { get; init; }
    public void Validate()
    {
        if (FormatVersion != 1 || Id == Guid.Empty || Width <= 0 || Height <= 0 ||
            Marks is null || UndoHistory is null || RedoHistory is null ||
            UndoHistory.Any(s => s is null) || RedoHistory.Any(s => s is null))
            throw new InvalidDataException("The stored screenshot draft could not be read.");
        foreach (var mark in Marks.Concat(UndoHistory.SelectMany(s => s)).Concat(RedoHistory.SelectMany(s => s)))
        {
            if (mark is null) throw new InvalidDataException("The stored screenshot contains an invalid annotation.");
            mark.Validate();
        }
    }
}

public static class AtomicFile
{
    public static void Write(string path, ReadOnlySpan<byte> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(data); stream.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class CaptureStore
{
    public string Root { get; }
    public int Limit { get; private set; }
    public List<CaptureDraft> Records { get; private set; } = [];
    public event Action? Changed;
    public List<string> RecoveryWarnings { get; } = [];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public CaptureStore(string root, int limit = 50)
    {
        Root = root; Limit = Math.Clamp(limit, 1, 500); Directory.CreateDirectory(Root);
        foreach (string folder in Directory.EnumerateDirectories(Root))
        {
            if (!Guid.TryParse(Path.GetFileName(folder), out var id)) continue;
            try
            {
                var draft = JsonSerializer.Deserialize<CaptureDraft>(File.ReadAllBytes(Path.Combine(folder, "draft.json")), Json)
                    ?? throw new InvalidDataException("The draft is empty.");
                draft.Validate();
                if (draft.Id != id || !File.Exists(Path.Combine(folder, "original.png")))
                    throw new InvalidDataException("The original screenshot is missing.");
                Records.Add(draft);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { RecoveryWarnings.Add($"{Path.GetFileName(folder)}: {error.Message}"); }
        }
        Sort(); Trim();
    }
    private string Folder(Guid id) => Path.Combine(Root, id.ToString("D"));
    private void Sort() => Records.Sort((a, b) => a.CapturedAt == b.CapturedAt ? b.Id.CompareTo(a.Id) : b.CapturedAt.CompareTo(a.CapturedAt));
    public bool Contains(Guid id) => Records.Any(r => r.Id == id);
    public CaptureDraft Get(Guid id) => Records.FirstOrDefault(r => r.Id == id) ?? throw new FileNotFoundException("This screenshot is no longer in recent history.");
    public byte[] Original(Guid id) { _ = Get(id); return File.ReadAllBytes(Path.Combine(Folder(id), "original.png")); }
    public byte[] Thumbnail(Guid id) { _ = Get(id); string path = Path.Combine(Folder(id), "thumbnail.png"); return File.ReadAllBytes(File.Exists(path) ? path : Path.Combine(Folder(id), "original.png")); }
    public bool ThumbnailIsCurrent(Guid id) => Get(id).ThumbnailVersion == 1 && File.Exists(Path.Combine(Folder(id), "thumbnail.png"));
    public CaptureDraft Add(byte[] png, byte[] thumbnail, int width, int height, DateTimeOffset? at = null)
    {
        var draft = new CaptureDraft { Id = Guid.NewGuid(), CapturedAt = at ?? DateTimeOffset.Now, Width = width, Height = height, ThumbnailVersion = 1 };
        draft.Validate();
        if (png.Length == 0) throw new InvalidDataException("The screenshot is empty.");
        string pending = Path.Combine(Root, ".pending-" + draft.Id.ToString("N"));
        Directory.CreateDirectory(pending);
        try
        {
            AtomicFile.Write(Path.Combine(pending, "original.png"), png);
            AtomicFile.Write(Path.Combine(pending, "thumbnail.png"), thumbnail);
            AtomicFile.Write(Path.Combine(pending, "draft.json"), JsonSerializer.SerializeToUtf8Bytes(draft, Json));
            Directory.Move(pending, Folder(draft.Id));
        }
        catch { if (Directory.Exists(pending)) Directory.Delete(pending, recursive: true); throw; }
        Records.Add(draft); Sort(); Trim(); Changed?.Invoke(); return draft;
    }
    public void Save(Guid id, DocumentSnapshot snapshot, byte[]? thumbnail = null)
    {
        var previous = Get(id);
        bool changed = !AnnotationDocument.Equal(previous.Marks, snapshot.Marks);
        var updated = previous with { Marks = AnnotationDocument.Copy(snapshot.Marks), UndoHistory = AnnotationDocument.CopyHistory(snapshot.Undo), RedoHistory = AnnotationDocument.CopyHistory(snapshot.Redo), ThumbnailVersion = thumbnail is not null ? 1 : changed ? 0 : previous.ThumbnailVersion };
        updated.Validate();
        if (thumbnail is not null) AtomicFile.Write(Path.Combine(Folder(id), "thumbnail.png"), thumbnail);
        AtomicFile.Write(Path.Combine(Folder(id), "draft.json"), JsonSerializer.SerializeToUtf8Bytes(updated, Json));
        Records[Records.FindIndex(r => r.Id == id)] = updated;
        if (changed) Changed?.Invoke();
    }
    public void UpdateThumbnail(Guid id, byte[] thumbnail)
    {
        var updated = Get(id) with { ThumbnailVersion = 1 };
        AtomicFile.Write(Path.Combine(Folder(id), "thumbnail.png"), thumbnail);
        AtomicFile.Write(Path.Combine(Folder(id), "draft.json"), JsonSerializer.SerializeToUtf8Bytes(updated, Json));
        Records[Records.FindIndex(r => r.Id == id)] = updated;
    }
    private void Trim()
    {
        while (Records.Count > Limit)
        {
            var oldest = Records[^1]; Directory.Delete(Folder(oldest.Id), recursive: true); Records.RemoveAt(Records.Count-1);
        }
    }
    public void SetLimit(int limit)
    {
        int previous = Limit; Limit = Math.Clamp(limit, 1, 500);
        try { Trim(); } catch { Limit = previous; throw; } finally { Changed?.Invoke(); }
    }
    public void Delete(Guid id, Action<string> removeDirectory)
    {
        _ = Get(id); string folder = Folder(id);
        removeDirectory(folder);
        if (Directory.Exists(folder)) throw new IOException("The capture could not be removed from recent history.");
        Records.RemoveAll(record => record.Id == id); Changed?.Invoke();
    }
    public void Clear(Action<string> removeDirectory)
    {
        removeDirectory(Root); Directory.CreateDirectory(Root); Records.Clear(); Changed?.Invoke();
    }
}

// Native RegisterHotKey modifiers; keeping this independent allows shortcut
// persistence and reservations to be checked on any development platform.
public sealed record CaptureShortcut(uint Key, uint Modifiers, string Label)
{
    public static CaptureShortcut Default => new(0x53, 0x0002 | 0x0004, "S");
    public string Display => (Modifiers.HasFlag(0x0002) ? "Ctrl+" : "") + (Modifiers.HasFlag(0x0001) ? "Alt+" : "") +
        (Modifiers.HasFlag(0x0004) ? "Shift+" : "") + (Modifiers.HasFlag(0x0008) ? "Win+" : "") + Label;
    public bool Valid => !string.IsNullOrWhiteSpace(Label) && Key is > 0 and <= 0xFE && (Modifiers & ~0xF) == 0 && (Modifiers & 0xB) != 0 &&
        !(Key == 0x43 && Modifiers == 0x2) && Key != 0x7B; // Ctrl+C and Windows' reserved F12.
}
internal static class Flags { public static bool HasFlag(this uint value, uint flag) => (value & flag) != 0; }

public sealed record UserSettings
{
    public CaptureShortcut Shortcut { get; init; } = CaptureShortcut.Default;
    public int HistoryLimit { get; init; } = 50;
    public bool CloseEditorAfterCopy { get; init; } = true;
    public bool HasSeenWelcome { get; init; }
    public static UserSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        var value = JsonSerializer.Deserialize<UserSettings>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Settings are empty.");
        if (value.Shortcut is null || !value.Shortcut.Valid || value.HistoryLimit is < 1 or > 500)
            throw new InvalidDataException("The saved capture settings are invalid.");
        return value;
    }
    public void Save(string path) => AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(this));
}
