using System.Globalization;
using Shotlight.Core;
using Wpf = System.Windows;
using Media = System.Windows.Media;
using Input = System.Windows.Input;
using Controls = System.Windows.Controls;
using Imaging = System.Windows.Media.Imaging;

namespace Shotlight;

internal sealed class AnnotationCanvas : Controls.Canvas
{
    public Imaging.BitmapSource Original { get; }
    public AnnotationDocument Document { get; }
    public Tool Tool { get; set; } = Tool.Arrow;
    private uint argb = 0xFFFF3B30;
    private float strokeWidth = 4;
    public uint Argb { get => argb; set { argb = value; if (editing is not null) { editing = editing with { Argb = value }; RefreshText(); } } }
    public float StrokeWidth { get => strokeWidth; set { strokeWidth = value; if (editing is not null) { editing = editing with { Width = value }; RefreshText(); } } }
    private Annotation? current;
    private Controls.TextBox? textBox;
    private TextDraft? editing;
    public event Action? Changed;
    public bool IsEditing => textBox is not null;
    public AnnotationCanvas(Imaging.BitmapSource original, AnnotationDocument document)
    {
        Original = original; Document = document; Width = original.PixelWidth; Height = original.PixelHeight;
        Background = Media.Brushes.Transparent; ClipToBounds = true; Focusable = true;
        Cursor = Input.Cursors.Cross; Media.TextOptions.SetTextFormattingMode(this, Media.TextFormattingMode.Ideal);
    }
    public static Imaging.BitmapSource Read(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = new Imaging.BitmapImage(); image.BeginInit(); image.CacheOption = Imaging.BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    private static Media.SolidColorBrush Brush(uint color)
    {
        var brush = new Media.SolidColorBrush(Media.Color.FromArgb((byte)(color>>24), (byte)(color>>16), (byte)(color>>8), (byte)color));
        brush.Freeze(); return brush;
    }
    private static Media.FormattedText Text(Annotation mark, double dpi = 1) => new(mark.Text.Length == 0 ? " " : mark.Text,
        CultureInfo.CurrentCulture, Wpf.FlowDirection.LeftToRight, new Media.Typeface("Segoe UI Semibold"), Math.Max(16, mark.Width*6), Brush(mark.Argb), dpi);
    internal static void DrawMark(Media.DrawingContext g, Annotation mark, double dpi = 1)
    {
        var a = mark.Points[0]; var b = mark.Points[^1]; var brush = Brush(mark.Argb);
        if (mark.Tool == Tool.Text) { g.DrawText(Text(mark,dpi), new Wpf.Point(a.X,a.Y)); return; }
        if (mark.Tool == Tool.Pen && mark.Points.All(p => p == a))
        { g.DrawEllipse(brush,null,new Wpf.Point(a.X,a.Y),mark.Width/2,mark.Width/2); return; }
        var pen = new Media.Pen(brush, mark.Width) { StartLineCap = Media.PenLineCap.Round, EndLineCap = Media.PenLineCap.Round, LineJoin = Media.PenLineJoin.Round };
        if (mark.Tool == Tool.Rectangle)
        {
            g.DrawRectangle(null,pen,new Wpf.Rect(Math.Min(a.X,b.X), Math.Min(a.Y,b.Y), Math.Abs(b.X-a.X), Math.Abs(b.Y-a.Y))); return;
        }
        var geometry = new Media.StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Wpf.Point(a.X,a.Y), false, false);
            if (mark.Tool == Tool.Pen)
            {
                if (mark.Points.Length == 1) path.LineTo(new Wpf.Point(a.X+0.1,a.Y), true, false);
                foreach (var p in mark.Points.Skip(1)) path.LineTo(new Wpf.Point(p.X,p.Y), true, false);
            }
            else
            {
                path.LineTo(new Wpf.Point(b.X,b.Y), true, false);
                double angle = Math.Atan2(b.Y-a.Y,b.X-a.X), length = Math.Max(12, mark.Width*4);
                foreach (double offset in new[] { -Math.PI/6, Math.PI/6 })
                {
                    path.BeginFigure(new Wpf.Point(b.X,b.Y), false, false);
                    path.LineTo(new Wpf.Point(b.X-length*Math.Cos(angle+offset), b.Y-length*Math.Sin(angle+offset)), true, false);
                }
            }
        }
        geometry.Freeze(); g.DrawGeometry(null,pen,geometry);
    }
    protected override void OnRender(Media.DrawingContext g)
    {
        base.OnRender(g); g.DrawImage(Original,new Wpf.Rect(0,0,Width,Height));
        double dpi = Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int i = 0; i < Document.Marks.Count; i++) if (editing?.Index != i) DrawMark(g,Document.Marks[i],dpi);
        if (current is not null) DrawMark(g,current,dpi);
    }
    private PixelPoint Point(Input.MouseEventArgs e)
    {
        var p = e.GetPosition(this); return new((float)Math.Clamp(p.X,0,Width), (float)Math.Clamp(p.Y,0,Height));
    }
    protected override void OnMouseLeftButtonDown(Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled) return;
        Focus(); var p = Point(e);
        if (Tool == Tool.Text)
        {
            FinishText(); int? index = null;
            for (int i = Document.Marks.Count-1; i >= 0; i--)
            {
                var mark = Document.Marks[i]; if (mark.Tool != Tool.Text) continue;
                var size = Text(mark); var origin = mark.Points[0];
                var rect = new Wpf.Rect(origin.X-5,origin.Y-5,size.WidthIncludingTrailingWhitespace+10,size.Height+10);
                if (rect.Contains(new Wpf.Point(p.X,p.Y))) { index = i; break; }
            }
            BeginText(p,index);
        }
        else { current = new(Tool,[p],argb,strokeWidth); CaptureMouse(); InvalidateVisual(); }
        e.Handled = true;
    }
    protected override void OnMouseMove(Input.MouseEventArgs e)
    {
        base.OnMouseMove(e); if (current is null || e.LeftButton != Input.MouseButtonState.Pressed) return;
        var point = Point(e);
        current = current with { Points = current.Tool == Tool.Pen ? [..current.Points,point] : [current.Points[0],point] }; InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e); if (current is null) return;
        var point = Point(e);
        var final = current with { Points = current.Tool == Tool.Pen ? [..current.Points,point] : [current.Points[0],point] };
        current = null; ReleaseMouseCapture(); Document.Add(final); InvalidateVisual(); Changed?.Invoke(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (current is not null) { current = null; InvalidateVisual(); }
    }
    internal void BeginText(PixelPoint position, int? index = null)
    {
        FinishText(); var mark = index is int i ? Document.Marks[i] : null;
        editing = new(index,mark?.Points[0] ?? position,mark?.Argb ?? argb,mark?.Width ?? strokeWidth,mark?.Text ?? "");
        var box = new Controls.TextBox
        {
            Text = editing.Text, AcceptsReturn = true, AcceptsTab = false, TextWrapping = Wpf.TextWrapping.NoWrap,
            Background = Media.Brushes.Transparent, BorderThickness = new Wpf.Thickness(0), Padding = new Wpf.Thickness(0),
            FontFamily = new Media.FontFamily("Segoe UI Semibold"), VerticalContentAlignment = Wpf.VerticalAlignment.Top,
            HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Hidden,
            MinWidth = 40, MinHeight = Math.Max(16,editing.Width*6)*1.4
        };
        Wpf.Automation.AutomationProperties.SetName(box,"Screenshot text annotation");
        Media.TextOptions.SetTextFormattingMode(box,Media.TextFormattingMode.Ideal);
        box.TextChanged += (_,_) => { if (editing is not null) { editing = editing with { Text = box.Text }; RefreshText(); } };
        box.PreviewKeyDown += (_,e) =>
        {
            if (e.Key == Input.Key.Escape) { FinishText(cancel: true); e.Handled = true; }
            else if (e.Key == Input.Key.Enter && !Input.Keyboard.Modifiers.HasFlag(Input.ModifierKeys.Shift)) { FinishText(); e.Handled = true; }
        };
        textBox = box; Children.Add(box); RefreshText(); box.Focus(); box.CaretIndex = box.Text.Length; InvalidateVisual();
    }
    private void RefreshText()
    {
        if (textBox is null || editing is null) return;
        SetLeft(textBox,editing.Position.X); SetTop(textBox,editing.Position.Y);
        textBox.Foreground = Brush(editing.Argb); textBox.CaretBrush = Brush(editing.Argb);
        textBox.FontSize = Math.Max(16,editing.Width*6);
        var measured = Text(editing.Mark);
        textBox.Width = Math.Max(40,measured.WidthIncludingTrailingWhitespace+12);
        textBox.Height = Math.Max(textBox.FontSize*1.4,measured.Height+4);
        InvalidateVisual(); Changed?.Invoke();
    }
    public void FinishText(bool cancel = false)
    {
        if (textBox is null || editing is null) return;
        if (!cancel) Document.CommitText(editing with { Text = textBox.Text });
        var box = textBox; textBox = null; editing = null; Children.Remove(box);
        Focus(); InvalidateVisual(); Changed?.Invoke();
    }
    public DocumentSnapshot DraftSnapshot() => Document.Snapshot(editing is null ? null : editing with { Text = textBox?.Text ?? editing.Text });
    public void Undo() { FinishText(); Document.Undo(); InvalidateVisual(); Changed?.Invoke(); }
    public void Redo() { FinishText(); Document.Redo(); InvalidateVisual(); Changed?.Invoke(); }
    public byte[] ExportPng()
    {
        FinishText(); var visual = new Media.DrawingVisual();
        using (var g = visual.RenderOpen())
        {
            g.DrawImage(Original,new Wpf.Rect(0,0,Original.PixelWidth,Original.PixelHeight));
            foreach (var mark in Document.Marks) DrawMark(g,mark);
        }
        var bitmap = new Imaging.RenderTargetBitmap(Original.PixelWidth,Original.PixelHeight,96,96,Media.PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new Imaging.PngBitmapEncoder(); encoder.Frames.Add(Imaging.BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
}
