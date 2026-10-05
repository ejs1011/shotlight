using System.ComponentModel;
using System.Runtime.InteropServices;
using Shotlight.Core;

namespace Shotlight;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private int activeId;
    private int nextId = 1;
    public bool HasRegistration => activeId != 0;
    public event Action? Pressed;
    public HotkeyWindow() => CreateHandle(new CreateParams { Caption = "Shotlight Hotkey", Parent = new IntPtr(-3) });
    public bool Register(CaptureShortcut shortcut)
    {
        int candidate = nextId++;
        if (nextId > 0xBFFF) nextId = 1;
        if (!RegisterHotKey(Handle, candidate, shortcut.Modifiers | 0x4000, shortcut.Key)) return false;
        if (activeId != 0) UnregisterHotKey(Handle, activeId);
        activeId = candidate; return true;
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && message.WParam.ToInt32() == activeId) Pressed?.Invoke();
        base.WndProc(ref message);
    }
    public void Dispose() { if (activeId != 0) UnregisterHotKey(Handle, activeId); activeId = 0; DestroyHandle(); }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}

internal static class Native
{
    // Use a pixel coordinate context while taking and selecting the screenshot.
    // Normal editors retain WinForms' PerMonitorV2 automatic UI scaling.
    public static IDisposable PixelCoordinates() => new DpiScope();
    private sealed class DpiScope : IDisposable
    {
        private readonly IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        public void Dispose() { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
    }
    public static Bitmap CaptureDesktop(Rectangle bounds)
    {
        // GDI screen blits do not populate alpha. An opaque RGB target prevents
        // the captured desktop from becoming transparent in the PNG/editor.
        var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            IntPtr destination = graphics.GetHdc(), source = GetDC(IntPtr.Zero);
            try
            {
                if (source == IntPtr.Zero || !BitBlt(destination, 0, 0, bounds.Width, bounds.Height, source, bounds.X, bounds.Y, 0x00CC0020 | 0x40000000))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The desktop could not be captured.");
            }
            finally { if (source != IntPtr.Zero) ReleaseDC(IntPtr.Zero, source); graphics.ReleaseHdc(destination); }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
}

internal static class ImageFiles
{
    public static byte[] Png(Image image) { using var stream = new MemoryStream(); image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return stream.ToArray(); }
    public static Bitmap Read(byte[] bytes) { using var stream = new MemoryStream(bytes); using var source = new Bitmap(stream); return new Bitmap(source); }
    public static byte[] Thumbnail(Bitmap image)
    {
        using var small = new Bitmap(240, 150); using var g = Graphics.FromImage(small);
        g.Clear(Color.FromArgb(40, 44, 52));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        float scale = Math.Min(240f / image.Width, 150f / image.Height);
        float w = image.Width * scale, h = image.Height * scale;
        g.DrawImage(image, (240-w)/2, (150-h)/2, w, h); return Png(small);
    }
    public static void Copy(Bitmap image)
    {
        // PNG retains full resolution, while Bitmap/DIB supports Office, Paint,
        // chat apps, and other Windows programs that do not read the PNG format.
        using var stream = new MemoryStream(Png(image));
        var data = new DataObject(); data.SetData(DataFormats.Bitmap, true, image);
        data.SetData("PNG", false, stream); Clipboard.SetDataObject(data, true, 5, 100);
    }
}
