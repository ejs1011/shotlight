using Shotlight.Core;
using Wpf = System.Windows;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;

namespace Shotlight;

// Render the actual Windows controls with synthetic content for visual review.
// This never captures the desktop or reads the user's screenshot history.
internal static class UiPreview
{
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(Path.GetTempPath(),"Shotlight-preview-" + Guid.NewGuid().ToString("N"));
        var app = new Wpf.Application { ShutdownMode = Wpf.ShutdownMode.OnExplicitShutdown };
        try
        {
            using var owner = new AppController(app,temporary);
            using var sample = Sample();
            var editor = owner.OpenNew(sample);
            editor.Canvas.Document.Add(new(Tool.Rectangle,[new(48,196),new(282,355)],0xFF5265E9,3));
            editor.Canvas.Document.Add(new(Tool.Arrow,[new(610,105),new(708,202)],0xFFFF3B30,4));
            editor.Canvas.Document.Add(new(Tool.Text,[new(382,68)],0xFF5265E9,4,"Ready for a little adventure"));
            editor.Flush(); editor.Canvas.InvalidateVisual();
            Render(editor,directory,"editor",1200,820);
            Render(editor,directory,"editor-compact",820,700);
            editor.Close();
            for (int i = 0; i < 5; i++)
                owner.TestStore.Add(ImageFiles.Png(sample),ImageFiles.Thumbnail(sample),sample.Width,sample.Height,DateTimeOffset.Now.AddHours(-i-1));
            var history = new HistoryWindow(owner.TestStore,_ => { }); history.Show(); Render(history,directory,"recent-captures",840,680); history.Close();
            var settings = new SettingsWindow(new UserSettings(),_ => null,() => { }); settings.Show(); Render(settings,directory,"settings",550,null); settings.Close();
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(directory,"preview-error.txt"),error.ToString()); return 1; }
        finally
        {
            foreach (var window in app.Windows.Cast<Wpf.Window>().ToArray())
                if (window is EditorWindow editor) editor.CloseAfterQuit(); else window.Close();
            app.Shutdown(); if (Directory.Exists(temporary)) Directory.Delete(temporary,true);
        }
    }
    private static void Render(Wpf.Window window, string directory, string name, double width, double? height)
    {
        window.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.Width = width; if (height is double h) window.Height = h;
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.Render);
        var content = (Wpf.FrameworkElement)window.Content; content.UpdateLayout();
        var image = new Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,Media.PixelFormats.Pbgra32);
        image.Render(content); var encoder = new Imaging.PngBitmapEncoder(); encoder.Frames.Add(Imaging.BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(directory,name+".png")); encoder.Save(file);
    }
    private static Bitmap Sample()
    {
        var image = new Bitmap(900,460);
        using var g = Graphics.FromImage(image); g.Clear(Color.FromArgb(250,249,246));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var ink = new SolidBrush(Color.FromArgb(40,55,51)); using var gray = new SolidBrush(Color.FromArgb(105,118,111));
        using var title = new Font("Segoe UI",26,FontStyle.Bold); using var heading = new Font("Segoe UI",14,FontStyle.Bold); using var body = new Font("Segoe UI",11);
        using var line = new Pen(Color.FromArgb(224,226,219));
        g.DrawString("WEEKEND NOTES",body,gray,48,28); g.DrawString("A day out of the ordinary.",title,ink,45,104);
        g.DrawString("A few good places, a slower pace, and room to wander.",body,gray,49,154);
        string[] titles = ["Start somewhere green","Take the scenic route","Stay for the sunset"];
        string[] subtitles = ["09:00  ·  Coffee & the gardens","12:30  ·  A walk by the water","17:45  ·  The best view in town"];
        Color[] colors = [Color.FromArgb(219,232,214),Color.FromArgb(215,229,240),Color.FromArgb(243,224,207)];
        for (int i = 0; i < 3; i++)
        {
            int x = 52+i*267;
            using var fill = new SolidBrush(colors[i]); g.FillRectangle(fill,x,204,225,102);
            using var landscape = new SolidBrush(Color.FromArgb(90,colors[i].R-50,colors[i].G-50,colors[i].B-50));
            g.FillEllipse(landscape,x+24,232,125,110); g.FillEllipse(fill,x+116,211,138,127);
            g.DrawString(titles[i],heading,ink,x,322); g.DrawString(subtitles[i],body,gray,x,353);
        }
        g.DrawLine(line,49,397,851,397); g.DrawString("SAVED PLACES     /     OCTOBER COLLECTION",body,gray,49,414);
        return image;
    }
}
