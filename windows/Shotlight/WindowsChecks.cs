using System.Text;
using Shotlight.Core;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Input = System.Windows.Input;

namespace Shotlight;

internal static class WindowsChecks
{
    // These Windows-only checks use generated images and an injected clipboard
    // writer. They do not capture the desktop, touch normal history, or replace
    // the user's clipboard. The desktop/tray integration still needs a real PC.
    public static int Run(string[] args)
    {
        var report = new StringBuilder(); int failed = 0;
        string root = Path.Combine(Path.GetTempPath(),"Shotlight-windows-checks-" + Guid.NewGuid().ToString("N"));
        var app = new Wpf.Application { ShutdownMode = Wpf.ShutdownMode.OnExplicitShutdown };
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Check(string name, Action action)
        {
            try { action(); report.AppendLine("PASS: " + name); }
            catch (Exception error) { failed++; report.AppendLine("FAIL: " + name + "\n" + error); }
        }
        try
        {
            using var owner = new AppController(app,root);
            using var source = new Bitmap(400,240);
            for (int y = 0; y < source.Height; y++) for (int x = 0; x < source.Width; x++) source.SetPixel(x,y,Color.FromArgb(255,x%256,y%256,60));
            Check("reverse frozen selection crops original pixels without decorations",() =>
            {
                using var frozen = new Bitmap(source); using var selector = new SelectionForm(new Bitmap(frozen),new Rectangle(-400,200,400,240));
                Bitmap? selected = null; selector.Selected += image => selected = image;
                source.SetPixel(80,60,Color.Magenta); // The live desktop changed after activation.
                selector.DragForCheck(new Point(300,180),new Point(80,60));
                Require(selected is not null,"Selection produced no image.");
                using (selected)
                {
                    Require(selected!.Width == 220 && selected.Height == 120,"Reverse selection dimensions changed.");
                    Require(selected.GetPixel(0,0) == frozen.GetPixel(80,60) && selected.GetPixel(100,60) == frozen.GetPixel(180,120),"The crop recaptured content or included overlay decoration.");
                }
            });
            Check("empty selection keeps capture active; Escape cancels",() =>
            {
                using var selector = new SelectionForm(new Bitmap(source),new Rectangle(0,0,400,240)); int captures = 0, cancelled = 0;
                selector.Selected += image => { captures++; image.Dispose(); }; selector.Cancelled += () => cancelled++;
                selector.DragForCheck(new Point(20,20),new Point(20,20)); selector.CancelForCheck(); Require(captures == 0 && cancelled == 1,"Empty selection or cancellation generated a screenshot.");
            });
            Check("PNG export retains physical pixel dimensions and annotations",() =>
            {
                var editor = owner.OpenNew(source);
                editor.Canvas.Document.Add(new(Tool.Rectangle,[new(20,20),new(80,80)],0xFFFF0000,4));
                using var exported = ImageFiles.Read(editor.Canvas.ExportPng());
                Require(exported.Width == 400 && exported.Height == 240,"PNG export changed resolution.");
                Color red = exported.GetPixel(20,40); Require(red.R > 240 && red.G < 20 && red.B < 20,"Rectangle was not rendered at its pixel coordinates.");
                Require(editor.Flush(),"Draft could not be archived."); editor.Close();
            });
            Check("inline text is transparent, remains editable, and is retained while typing",() =>
            {
                var editor = owner.OpenNew(source); editor.Canvas.BeginText(new(40,30));
                var box = editor.Canvas.Children.OfType<Controls.TextBox>().Single(); box.Text = "First line\nSecond line";
                Require(box.Background.Opacity == 1 && box.Background is System.Windows.Media.SolidColorBrush brush && brush.Color.A == 0,"Inline text has an opaque background.");
                owner.TestStore.Save(editor.CaptureId,editor.Canvas.DraftSnapshot());
                var restarted = new CaptureStore(owner.TestStore.Root); Require(restarted.Get(editor.CaptureId).Marks.Single().Text == box.Text,"Typing was not retained.");
                editor.Canvas.FinishText(); editor.Canvas.BeginText(new(40,30),0);
                editor.Canvas.Children.OfType<Controls.TextBox>().Single().Text = "Edited"; editor.Canvas.FinishText(); editor.Canvas.Undo();
                Require(editor.Canvas.Document.Marks.Single().Text == "First line\nSecond line","Text edit could not be undone.");
                editor.Canvas.Redo(); Require(editor.Canvas.Document.Marks.Single().Text == "Edited","Text edit could not be redone."); editor.Close();
            });
            Check("Ctrl+C commits text, copies the image, closes the editor, and retains its draft",() =>
            {
                var editor = owner.OpenNew(source); Guid id = editor.CaptureId; editor.Canvas.BeginText(new(40,30));
                editor.Canvas.Children.OfType<Controls.TextBox>().Single().Text = "Copy this draft"; bool copied = false, closed = false;
                editor.Closed += (_,_) => closed = true;
                Require(editor.HandleShortcut(Input.Key.C,Input.ModifierKeys.Control,image => copied = image.Width == 400 && image.Height == 240),"Ctrl+C was not routed.");
                Require(copied && closed && !editor.Canvas.IsEditing,"Ctrl+C did not copy and dismiss.");
                var restored = new CaptureStore(owner.TestStore.Root); Require(restored.Get(id).Marks.Single().Text == "Copy this draft","Copy-close discarded its history.");
            });
            Check("compact toolbar menus change size and zoom while preserving pixels and history navigation",() =>
            {
                static IEnumerable<Wpf.DependencyObject> Descendants(Wpf.DependencyObject root)
                {
                    yield return root;
                    foreach (var child in Wpf.LogicalTreeHelper.GetChildren(root).OfType<Wpf.DependencyObject>())
                        foreach (var nested in Descendants(child)) yield return nested;
                }
                var editor = owner.OpenNew(source); Guid original = editor.CaptureId;
                var buttons = Descendants(editor).OfType<Controls.Button>().ToArray();
                var size = buttons.Single(button => button.ToolTip is string hint && hint == "Stroke width");
                var thick = size.ContextMenu.Items.OfType<Controls.MenuItem>().Single(item => Equals(item.Header,"8 px"));
                thick.RaiseEvent(new Wpf.RoutedEventArgs(Controls.MenuItem.ClickEvent));
                Require(editor.Canvas.StrokeWidth == 8,"Compact size menu did not update the annotation size.");
                var more = buttons.Single(button => button.ToolTip is string hint && hint.StartsWith("More ·")).ContextMenu;
                more.PlacementTarget = editor; more.IsOpen = true; more.UpdateLayout();
                editor.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Require(more.ActualWidth > 100,"More menu did not open.");
                var zoom = more.Items.OfType<Controls.MenuItem>().Single(item => Equals(item.Header,"Zoom"));
                zoom.IsSubmenuOpen = true; zoom.ApplyTemplate();
                editor.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var popup = (Controls.Primitives.Popup)zoom.Template.FindName("PART_Popup",zoom);
                Require(popup.IsOpen && ((Wpf.FrameworkElement)popup.Child).ActualWidth > 0,"Zoom submenu did not open.");
                zoom.Items.OfType<Controls.MenuItem>().Single(item => Equals(item.Header,"150%")).RaiseEvent(new Wpf.RoutedEventArgs(Controls.MenuItem.ClickEvent));
                Require(editor.Canvas.LayoutTransform is System.Windows.Media.ScaleTransform transform && transform.ScaleX == 1.5,"Zoom menu did not update the preview.");
                using var output = ImageFiles.Read(editor.Canvas.ExportPng());
                Require(output.Width == source.Width && output.Height == source.Height,"Preview zoom changed exported pixels.");
                zoom.IsSubmenuOpen = false; more.IsOpen = false;
                more.Items.OfType<Controls.MenuItem>().Single(item => Equals(item.Header,"Previous capture")).RaiseEvent(new Wpf.RoutedEventArgs(Controls.MenuItem.ClickEvent));
                Require(editor.CaptureId != original && owner.TestStore.Contains(original),"History navigation lost the original capture."); editor.Close();
            });
            Check("both copy modes honor settings for Ctrl+C, buttons and existing editors",() =>
            {
                int copies = 0; var editor = owner.OpenNew(source,image => { Require(image.Width == 400 && image.Height == 240,"Copy resolution changed."); copies++; });
                Guid id = editor.CaptureId;
                Require(owner.ApplySettings(owner.Settings with { CloseEditorAfterCopy = false }) is null,"Could not apply copy setting.");
                Require(!editor.CloseEditorAfterCopy && Wpf.Automation.AutomationProperties.GetName(editor.CopyButton) == "Copy","Existing editor did not update its copy label.");
                editor.Canvas.BeginText(new(30,30)); editor.Canvas.Children.OfType<Controls.TextBox>().Single().Text = "Keep this draft";
                editor.HandleShortcut(Input.Key.C,Input.ModifierKeys.Control);
                Require(copies == 1 && editor.IsVisible && !editor.Canvas.IsEditing,"Ctrl+C did not keep the editor open.");
                editor.CopyButton.RaiseEvent(new Wpf.RoutedEventArgs(Controls.Button.ClickEvent));
                Require(copies == 2 && editor.IsVisible && new CaptureStore(owner.TestStore.Root).Get(id).Marks.Single().Text == "Keep this draft","Toolbar copy lost the draft or closed the editor.");
                Require(owner.ApplySettings(owner.Settings with { CloseEditorAfterCopy = true }) is null,"Could not restore close-on-copy.");
                editor.CopyButton.RaiseEvent(new Wpf.RoutedEventArgs(Controls.Button.ClickEvent));
                Require(copies == 3 && !editor.IsVisible,"Toolbar did not close when configured.");
                var reopened = new UserSettings { CloseEditorAfterCopy = false, HasSeenWelcome = true }; string path = Path.Combine(root,"copy-check.json"); reopened.Save(path);
                Require(UserSettings.Load(path) == reopened,"Settings did not survive restart.");
            });
            Check("fit follows compact resizing and explicit 100% preserves export resolution",() =>
            {
                using var large = new Bitmap(900,460); var editor = owner.OpenNew(large); editor.Width = 760; editor.Height = 460; editor.UpdateLayout(); editor.FitToWindow(); editor.UpdateLayout();
                editor.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var transform = (System.Windows.Media.ScaleTransform)editor.Canvas.LayoutTransform;
                Require(editor.IsFittingPreview && transform.ScaleX < 1 && editor.PreviewScroll.ExtentWidth <= editor.PreviewScroll.ViewportWidth+1 && editor.PreviewScroll.ExtentHeight <= editor.PreviewScroll.ViewportHeight+1,"Fit clipped the image in a compact editor.");
                var toolbar = (Wpf.FrameworkElement)Wpf.LogicalTreeHelper.GetParent(editor.CopyButton);
                var content = (Wpf.FrameworkElement)editor.Content; var bounds = toolbar.TransformToAncestor(content).TransformBounds(new Wpf.Rect(toolbar.RenderSize));
                Require(bounds.Left >= 0 && bounds.Right <= content.ActualWidth,"Compact editor clipped the toolbar actions.");
                editor.SetZoom(1); editor.Width = 820; editor.UpdateLayout(); editor.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Require(!editor.IsFittingPreview && ((System.Windows.Media.ScaleTransform)editor.Canvas.LayoutTransform).ScaleX == 1,"Resizing discarded the chosen 100% scale.");
                using var output = ImageFiles.Read(editor.Canvas.ExportPng()); Require(output.Width == 900 && output.Height == 460,"Fit changed exported pixels."); editor.Close();
            });
            Check("text sizes use points independently of drawing width and preserve old annotations",() =>
            {
                var editor = owner.OpenNew(source); editor.Canvas.StrokeWidth = 8; editor.Canvas.BeginText(new(20,20));
                editor.Canvas.FontSizePoints = 16; var box = editor.Canvas.Children.OfType<Controls.TextBox>().Single(); box.Text = "Sized text";
                Require(Math.Abs(box.FontSize-16*96d/72) < .01 && editor.Canvas.StrokeWidth == 8,"Point sizing changed stroke width or font units.");
                editor.Canvas.FinishText(); editor.Canvas.BeginText(new(20,20),0);
                Require(Math.Abs(editor.Canvas.FontSizePoints-16) < .01,"Edited text size was not restored."); editor.Canvas.FinishText();
                editor.Canvas.Document.Add(new(Tool.Text,[new(20,80)],0xFFFF0000,4,"Legacy text")); editor.Canvas.BeginText(new(20,80),1);
                Require(editor.Canvas.FontSizePoints == 18 && editor.Canvas.Children.OfType<Controls.TextBox>().Single().FontSize == 24,"Legacy text changed its rendered size.");
                editor.Canvas.FinishText(); editor.Close();
            });
            Check("history thumbnails refresh annotations, undo and older cached drafts",() =>
            {
                var editor = owner.OpenNew(source); var store = owner.TestStore; Guid id = editor.CaptureId; byte[] original = store.Original(id);
                editor.Canvas.Document.Add(new(Tool.Rectangle,[new(20,20),new(80,80)],0xFFFF0000,4)); Require(editor.Flush(),"Could not save annotated thumbnail.");
                using (var thumb = ImageFiles.Read(store.Thumbnail(id))) { var red = thumb.GetPixel(12,27); Require(red.R > 230 && red.G < 20,"Thumbnail omitted annotations."); }
                editor.Canvas.Undo(); Require(editor.Flush(),"Undo did not save.");
                using (var thumb = ImageFiles.Read(store.Thumbnail(id))) Require(thumb.GetPixel(12,27).R < 100,"Undo left a stale annotation preview.");
                editor.Canvas.Redo(); store.Save(id,editor.Canvas.DraftSnapshot()); Require(!store.ThumbnailIsCurrent(id),"Old cache was not invalidated.");
                var history = new HistoryWindow(store,_ => { });
                Require(store.ThumbnailIsCurrent(id) && store.Original(id).SequenceEqual(original),"Older cache upgrade changed original pixels.");
                using (var thumb = ImageFiles.Read(store.Thumbnail(id))) Require(thumb.GetPixel(12,27).R > 230,"Older cache upgrade omitted annotations.");
                history.Close(); editor.Close();
            });
            Check("settings show inline errors and actual retention counts and respect cancellation",() =>
            {
                int applied = 0, confirmed = 0; UserSettings? saved = null;
                var settings = new SettingsWindow(new UserSettings { HasSeenWelcome = true },candidate => { applied++; saved = candidate; return null; },() => { },() => 5) { Height = 480 };
                settings.Show(); settings.UpdateLayout(); settings.LimitField.Text = "0"; settings.SaveChanges(); settings.UpdateLayout();
                Require(applied == 0 && settings.HistoryError.Visibility == Wpf.Visibility.Visible && settings.ShortcutError.Visibility == Wpf.Visibility.Collapsed,"Retention error appeared in the shortcut section.");
                settings.LimitField.Text = "2";
                Require(settings.RetentionWarning.Text.Contains("remove 3 older drafts"),"Warning did not show the actual removal count.");
                settings.ConfirmReduction = (limit,count) => { confirmed = count; return false; }; settings.SaveChanges(); Require(applied == 0 && confirmed == 3,"Cancelled reduction applied settings.");
                settings.ConfirmReduction = (_,_) => true; settings.CloseOnCopy.IsChecked = false; settings.SaveChanges();
                Require(applied == 1 && saved?.HistoryLimit == 2 && saved.CloseEditorAfterCopy == false && saved.HasSeenWelcome,"Confirmed settings lost the copy/welcome preference.");
            });
        }
        catch (Exception error) { failed++; report.AppendLine("FAIL: check setup\n" + error); }
        finally
        {
            foreach (var editor in app.Windows.OfType<EditorWindow>().ToArray()) editor.CloseAfterQuit();
            app.Shutdown(); if (Directory.Exists(root)) Directory.Delete(root,recursive: true);
        }
        report.AppendLine($"\n{(failed == 0 ? "ALL CHECKS PASSED" : $"{failed} CHECK(S) FAILED")}");
        int flag = Array.IndexOf(args,"--report"); string path = flag >= 0 && flag+1 < args.Length ? args[flag+1] : Path.Combine(Path.GetTempPath(),"Shotlight-checks.txt");
        try { File.WriteAllText(path,report.ToString()); } catch { return 1; }
        return failed == 0 ? 0 : 1;
    }
}
