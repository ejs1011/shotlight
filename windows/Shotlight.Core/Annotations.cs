namespace Shotlight.Core;

public enum Tool { Pen, Arrow, Rectangle, Text }
public readonly record struct PixelPoint(float X, float Y);
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect Between(PixelPoint a, PixelPoint b, int width, int height)
    {
        int left = Math.Clamp((int)Math.Floor(Math.Min(a.X, b.X)), 0, width);
        int top = Math.Clamp((int)Math.Floor(Math.Min(a.Y, b.Y)), 0, height);
        int right = Math.Clamp((int)Math.Ceiling(Math.Max(a.X, b.X)), 0, width);
        int bottom = Math.Clamp((int)Math.Ceiling(Math.Max(a.Y, b.Y)), 0, height);
        return new(left, top, right-left, bottom-top);
    }
    public bool IsCapture => Width >= 2 && Height >= 2;
}

public sealed record Annotation(Tool Tool, PixelPoint[] Points, uint Argb, float Width, string Text = "")
{
    public Annotation Copy() => this with { Points = Points.ToArray() };
    public void Validate()
    {
        if (!Enum.IsDefined(Tool) || Points is null || Points.Length == 0 ||
            !float.IsFinite(Width) || Width <= 0 || Text is null ||
            Points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y)))
            throw new InvalidDataException("The screenshot contains an invalid annotation.");
    }
}

public sealed class AnnotationDocument
{
    public List<Annotation> Marks { get; private set; } = [];
    public List<List<Annotation>> UndoStack { get; private set; } = [];
    public List<List<Annotation>> RedoStack { get; private set; } = [];
    public static List<Annotation> Copy(IEnumerable<Annotation> marks) => marks.Select(m => m.Copy()).ToList();
    public static List<List<Annotation>> CopyHistory(IEnumerable<List<Annotation>> history) => history.Select(Copy).ToList();
    public static bool Equal(IReadOnlyList<Annotation> a, IReadOnlyList<Annotation> b) => a.Count == b.Count &&
        a.Zip(b).All(pair => pair.First.Tool == pair.Second.Tool && pair.First.Argb == pair.Second.Argb &&
            pair.First.Width == pair.Second.Width && pair.First.Text == pair.Second.Text && pair.First.Points.SequenceEqual(pair.Second.Points));

    public AnnotationDocument() { }
    public AnnotationDocument(CaptureDraft draft)
    {
        draft.Validate(); Marks = Copy(draft.Marks);
        UndoStack = CopyHistory(draft.UndoHistory); RedoStack = CopyHistory(draft.RedoHistory);
    }
    public void Apply(List<Annotation> next)
    {
        if (Equal(Marks, next)) return;
        foreach (var mark in next) mark.Validate();
        UndoStack.Add(Copy(Marks)); Marks = Copy(next); RedoStack.Clear();
    }
    public void Add(Annotation mark) { var next = Copy(Marks); next.Add(mark); Apply(next); }
    public void Undo()
    {
        if (UndoStack.Count == 0) return;
        RedoStack.Add(Copy(Marks)); Marks = UndoStack[^1]; UndoStack.RemoveAt(UndoStack.Count-1);
    }
    public void Redo()
    {
        if (RedoStack.Count == 0) return;
        UndoStack.Add(Copy(Marks)); Marks = RedoStack[^1]; RedoStack.RemoveAt(RedoStack.Count-1);
    }
    // An in-progress inline edit is archived without interrupting the caret.
    public DocumentSnapshot Snapshot(TextDraft? text = null)
    {
        var marks = Copy(Marks);
        if (text is not null)
        {
            if (text.Index is int index)
            {
                if (text.Text.Length == 0) marks.RemoveAt(index);
                else marks[index] = text.Mark;
            }
            else if (text.Text.Length > 0) marks.Add(text.Mark);
        }
        var changed = !Equal(marks, Marks);
        var undo = CopyHistory(UndoStack);
        if (changed) undo.Add(Copy(Marks));
        return new(marks, undo, changed ? [] : CopyHistory(RedoStack));
    }
    public void CommitText(TextDraft draft) => Apply(Snapshot(draft).Marks);
}

public sealed record TextDraft(int? Index, PixelPoint Position, uint Argb, float Width, string Text)
{
    public Annotation Mark => new(Tool.Text, [Position], Argb, Width, Text);
}
public sealed record DocumentSnapshot(List<Annotation> Marks, List<List<Annotation>> Undo, List<List<Annotation>> Redo);
