using System.Drawing.Drawing2D;
using Shotlight.Core;

namespace Shotlight;

internal sealed class FrozenCapture : IDisposable
{
    private readonly List<SelectionForm> windows = [];
    private Action<Bitmap?>? completed;
    private bool finishing;
    public FrozenCapture(Action<Bitmap?> completed) => this.completed = completed;
    public void Start()
    {
        try
        {
            using var dpi = Native.PixelCoordinates();
            var displays = Screen.AllScreens.Select(s => s.Bounds).ToArray();
            Rectangle desktop = displays.Aggregate(Rectangle.Union);
            // One BitBlt snapshots the entire virtual desktop before any overlay
            // appears. Each display's selector is derived from that single frame.
            using var frozen = Native.CaptureDesktop(desktop);
            foreach (var display in displays)
            {
                var local = new Rectangle(display.X-desktop.X, display.Y-desktop.Y, display.Width, display.Height);
                var image = frozen.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var form = new SelectionForm(image, display);
                form.Selected += crop => Finish(crop); form.Cancelled += () => Finish(null); windows.Add(form);
            }
            foreach (var form in windows) form.Show();
            (windows.FirstOrDefault(f => f.Bounds.Contains(Cursor.Position)) ?? windows[0]).Activate();
        }
        catch { Dispose(); throw; }
    }
    private void Finish(Bitmap? crop)
    {
        if (finishing) { crop?.Dispose(); return; }
        finishing = true; var callback = completed; completed = null;
        foreach (var window in windows) { window.Hide(); window.Dispose(); }
        windows.Clear(); callback?.Invoke(crop);
    }
    public void Dispose() { completed = null; finishing = true; foreach (var window in windows) window.Dispose(); windows.Clear(); }
}

internal sealed class SelectionForm : Form
{
    private readonly Bitmap frozen;
    private Point? anchor;
    private PixelRect? selection;
    private readonly Rectangle screenBounds;
    public event Action<Bitmap>? Selected;
    public event Action? Cancelled;
    public SelectionForm(Bitmap frozen, Rectangle screenBounds)
    {
        this.frozen = frozen; this.screenBounds = screenBounds;
        AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; Bounds = screenBounds; TopMost = true;
        ShowInTaskbar = false; KeyPreview = true; DoubleBuffered = true; Cursor = Cursors.Cross;
        Text = "Shotlight — Frozen Capture"; AccessibleName = "Frozen screenshot. Drag to select an area. Escape cancels.";
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Bounds = screenBounds; }
    protected override void OnFormClosing(FormClosingEventArgs e) { base.OnFormClosing(e); Cancelled?.Invoke(); }
    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        if (keys == Keys.Escape) { Cancelled?.Invoke(); return true; }
        return base.ProcessCmdKey(ref message, keys);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
        Activate(); anchor = e.Location; selection = null; Capture = true; Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (anchor is not Point a) return;
        selection = PixelRect.Between(new(a.X,a.Y), new(e.X,e.Y), frozen.Width, frozen.Height); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); if (e.Button != MouseButtons.Left || anchor is not Point a) return;
        anchor = null; Capture = false;
        var rect = PixelRect.Between(new(a.X,a.Y), new(e.X,e.Y), frozen.Width, frozen.Height);
        if (!rect.IsCapture) { selection = null; Invalidate(); return; }
        // Crop the existing bitmap, never the live desktop or the decorated view.
        Selected?.Invoke(Crop(frozen, rect));
    }
    internal static Bitmap Crop(Bitmap frozen, PixelRect rect) => frozen.Clone(new Rectangle(rect.X,rect.Y,rect.Width,rect.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    internal void DragForCheck(Point from, Point to)
    {
        OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,from.X,from.Y,0));
        OnMouseMove(new MouseEventArgs(MouseButtons.Left,0,to.X,to.Y,0));
        OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,to.X,to.Y,0));
    }
    internal void CancelForCheck() { var message = new Message(); ProcessCmdKey(ref message,Keys.Escape); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.DrawImageUnscaled(frozen, 0, 0);
        using var dim = new SolidBrush(Color.FromArgb(75, Color.Black));
        if (selection is PixelRect r)
        {
            g.FillRectangle(dim, 0, 0, frozen.Width, r.Y);
            g.FillRectangle(dim, 0, r.Y+r.Height, frozen.Width, frozen.Height-r.Y-r.Height);
            g.FillRectangle(dim, 0, r.Y, r.X, r.Height);
            g.FillRectangle(dim, r.X+r.Width, r.Y, frozen.Width-r.X-r.Width, r.Height);
            using var black = new Pen(Color.Black, 2); using var white = new Pen(Color.White);
            g.DrawRectangle(black, r.X,r.Y,r.Width,r.Height); g.DrawRectangle(white,r.X,r.Y,r.Width,r.Height);
            Badge(g, $"{r.Width} × {r.Height} px", new Point(r.X, r.Y+r.Height+6));
        }
        else
        {
            g.FillRectangle(dim, ClientRectangle);
            Badge(g, "Drag to capture · Esc to cancel", new Point(Math.Max(10, frozen.Width/2-140), 26));
        }
    }
    private void Badge(Graphics g, string label, Point position)
    {
        float scale = DeviceDpi / 96f;
        using var font = new Font("Segoe UI", 12*scale, GraphicsUnit.Pixel);
        var size = g.MeasureString(label, font); int padding = (int)(8*scale), w = (int)Math.Ceiling(size.Width)+padding*2, h = (int)(28*scale);
        int x = Math.Clamp(position.X, 0, Math.Max(0, frozen.Width-w)), y = Math.Clamp(position.Y, 0, Math.Max(0, frozen.Height-h));
        using var brush = new SolidBrush(Color.FromArgb(225, Color.Black)); g.FillRectangle(brush, x,y,w,h);
        g.DrawString(label,font,Brushes.White,x+padding,y+6*scale);
    }
    protected override void Dispose(bool disposing) { if (disposing) frozen.Dispose(); base.Dispose(disposing); }
}
