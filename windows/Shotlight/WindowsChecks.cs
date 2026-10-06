using System.Text;
using Shotlight.Core;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Input = System.Windows.Input;

namespace Shotlight;

internal static class WindowsChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
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
            bool failRecycling = false;
            using var owner = new AppController(app,root,path =>
            {
                if (failRecycling) throw new IOException("Injected recycle failure");
                string recycle = Path.Combine(root,"Recycled"); Directory.CreateDirectory(recycle);
                Directory.Move(path,Path.Combine(recycle,Path.GetFileName(path)));
            });
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
            Check("Escape reaches the selector through the WPF message loop before and during a drag",() =>
            {
                foreach (bool dragging in new[] { false,true })
                {
                    using var selector = new SelectionForm(new Bitmap(source),new Rectangle(0,0,400,240));
                    int captures = 0, cancelled = 0; int retained = owner.TestStore.Records.Count;
                    selector.Selected += image => { captures++; image.Dispose(); };
                    selector.Cancelled += () => { cancelled++; selector.Hide(); };
                    selector.Show();
                    if (dragging)
                    {
                        Require(PostMessage(selector.Handle,0x0201,new IntPtr(1),new IntPtr(20 | (20 << 16))),"Could not post drag start.");
                        Require(PostMessage(selector.Handle,0x0200,new IntPtr(1),new IntPtr(80 | (80 << 16))),"Could not post drag movement.");
                    }
                    Require(PostMessage(selector.Handle,0x0100,new IntPtr((int)Keys.Escape),new IntPtr(1)),"Could not post Escape.");
                    app.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Require(cancelled == 1 && !selector.Visible && !selector.Capture,"Escape did not dismiss the selector and release the drag through the WPF message loop.");
                    selector.CancelForCheck(); selector.DragForCheck(new Point(20,20),new Point(80,80));
                    Require(cancelled == 1 && captures == 0 && owner.TestStore.Records.Count == retained,"A repeated cancel or delayed mouse release produced a capture.");
                }
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
            Check("inline text stays inside screenshot edges while growing and changing size",() =>
            {
                var editor = owner.OpenNew(source);
                foreach (double zoom in new[] { .5,1d,1.5 })
                foreach (var origin in new PixelPoint[] { new(399,1),new(1,239),new(399,239),new(0,0) })
                {
                    editor.SetZoom(zoom);
                    editor.Canvas.BeginText(origin);
                    var box = editor.Canvas.Children.OfType<Controls.TextBox>().Single();
                    foreach (string text in new[] { "E","Edge text","Edge text\r\nSecond line" })
                    {
                        box.Text = text; box.CaretIndex = box.Text.Length; editor.UpdateLayout();
                        editor.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        double left = Controls.Canvas.GetLeft(box), top = Controls.Canvas.GetTop(box);
                        Require(left >= 0 && top >= 0 && left+box.ActualWidth <= 400.01 && top+box.ActualHeight <= 240.01,$"Typing clipped the inline editor at {origin}: {left},{top} {box.ActualWidth}×{box.ActualHeight}, requested {box.Width}×{box.Height}.");
                        Require(box.HorizontalOffset < .01 && box.VerticalOffset < .01,"Growing text scrolled earlier characters out of view.");
                        var mark = editor.Canvas.DraftSnapshot().Marks.Last();
                        Require(Math.Abs(mark.Points[0].X-left) < .01 && Math.Abs(mark.Points[0].Y-top) < .01,"Autosaved text did not use its visible position.");
                    }
                    box.Text = "Sized";
                    foreach (float points in new[] { 48f,16f,24f })
                    {
                        editor.Canvas.FontSizePoints = points; editor.UpdateLayout();
                        Require(Controls.Canvas.GetLeft(box)+box.ActualWidth <= 400.01 && Controls.Canvas.GetTop(box)+box.ActualHeight <= 240.01,"Changing font size clipped edge text.");
                    }
                    var snapshot = editor.Canvas.DraftSnapshot(); editor.Canvas.FinishText();
                    Require(AnnotationDocument.Equal(snapshot.Marks,editor.Canvas.Document.Marks),"Committing moved the visible text.");
                    int index = editor.Canvas.Document.Marks.Count-1; editor.Canvas.BeginText(origin,index);
                    Require(AnnotationDocument.Equal(snapshot.Marks,editor.Canvas.DraftSnapshot().Marks),"Reopening moved edge text.");
                    editor.Canvas.Children.OfType<Controls.TextBox>().Single().Text = "Cancel this change";
                    editor.Canvas.FinishText(cancel: true);
                    Require(AnnotationDocument.Equal(snapshot.Marks,editor.Canvas.Document.Marks),"Cancelling changed the saved edge annotation.");
                    editor.Canvas.FontSizePoints = 24;
                }
                Require(editor.Flush(),"Edge annotations could not be archived.");
                var restored = new CaptureStore(owner.TestStore.Root).Get(editor.CaptureId);
                Require(AnnotationDocument.Equal(restored.Marks,editor.Canvas.Document.Marks),"Saved edge positions changed after restart.");
                using var output = ImageFiles.Read(editor.Canvas.ExportPng());
                Require(output.Width == 400 && output.Height == 240,"Moving edge text changed PNG dimensions."); editor.Close();
            });
            Check("multiline typing keeps preceding lines and the trailing blank line visible",() =>
            {
                var editor = owner.OpenNew(source); editor.Canvas.BeginText(new(30,30));
                var box = editor.Canvas.Children.OfType<Controls.TextBox>().Single();
                box.Text = "First line"; box.CaretIndex = box.Text.Length;
                System.Windows.Documents.EditingCommands.EnterLineBreak.Execute(null,box);
                Require(editor.Canvas.IsEditing && box.Text.EndsWith('\n'),"The native newline command committed or lost the line break.");
                foreach (string text in new[] { "First line\r\n","First line\r\nSecond line","First line\r\nSecond line\r\n","First line\n\nThird line\n" })
                {
                    box.Text = text; box.CaretIndex = box.Text.Length; editor.UpdateLayout();
                    editor.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var first = box.GetRectFromCharacterIndex(0); var caret = box.GetRectFromCharacterIndex(box.Text.Length);
                    Require(!first.IsEmpty && !caret.IsEmpty && first.Top >= 0 && caret.Bottom <= box.ActualHeight+.01,$"The first line or final caret was outside the inline editor: first {first}, caret {caret}, height {box.ActualHeight}, extent {box.ExtentHeight}, viewport {box.ViewportHeight}, offset {box.VerticalOffset}.");
                    Require(box.VerticalOffset < .01 && box.ExtentHeight <= box.ViewportHeight+.01,"A new line hid the rows above it.");
                }
                editor.Canvas.FinishText(); editor.Close();
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
                var history = new HistoryWindow(store,_ => { },_ => false);
                Require(store.ThumbnailIsCurrent(id) && store.Original(id).SequenceEqual(original),"Older cache upgrade changed original pixels.");
                using (var thumb = ImageFiles.Read(store.Thumbnail(id))) Require(thumb.GetPixel(12,27).R > 230,"Older cache upgrade omitted annotations.");
                history.Close(); editor.Close();
            });
            Check("editor deletion closes a typing capture and removes only its retained draft",() =>
            {
                var keep = owner.OpenNew(source); var editor = owner.OpenNew(source); Guid id = editor.CaptureId;
                string savedPng = Path.Combine(root,"saved-export.png"); byte[] savedPixels = ImageFiles.Png(source); File.WriteAllBytes(savedPng,savedPixels);
                editor.Canvas.BeginText(new(30,30)); editor.Canvas.Children.OfType<Controls.TextBox>().Single().Text = "Discard this typing";
                var content = (Controls.Grid)editor.Content;
                var toolbar = content.Children.OfType<Controls.Border>().Single();
                var remove = ((Controls.StackPanel)toolbar.Child).Children.OfType<Controls.Button>().Single(button => Equals(button.ToolTip,"Delete capture from Recent Captures"));
                owner.ConfirmCaptureDeletion = _ => false; remove.RaiseEvent(new Wpf.RoutedEventArgs(Controls.Button.ClickEvent));
                Require(editor.IsVisible && editor.Canvas.IsEditing && owner.TestStore.Contains(id),"Cancelling delete changed the capture.");
                owner.ConfirmCaptureDeletion = _ => true; remove.RaiseEvent(new Wpf.RoutedEventArgs(Controls.Button.ClickEvent));
                editor.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Require(!editor.IsVisible && !editor.Canvas.IsEditing && !owner.TestStore.Contains(id) && keep.IsVisible && owner.TestStore.Contains(keep.CaptureId),"Delete did not close only the target editor and remove its draft.");
                Require(!Directory.Exists(Path.Combine(owner.TestStore.Root,id.ToString())) && Directory.Exists(Path.Combine(root,"Recycled",id.ToString())),"Delete did not recycle the target files.");
                Require(!new CaptureStore(owner.TestStore.Root).Contains(id),"A deleted capture returned after restart.");
                Require(File.ReadAllBytes(savedPng).SequenceEqual(savedPixels),"Deletion changed a separately saved PNG."); keep.Close();
            });
            Check("recent capture deletion supports cancellation, closes an open editor and refreshes the list",() =>
            {
                static IEnumerable<Wpf.DependencyObject> Descendants(Wpf.DependencyObject root)
                {
                    yield return root;
                    foreach (var child in Wpf.LogicalTreeHelper.GetChildren(root).OfType<Wpf.DependencyObject>())
                        foreach (var nested in Descendants(child)) yield return nested;
                }
                var editor = owner.OpenNew(source); Guid id = editor.CaptureId; editor.Canvas.BeginText(new(20,20));
                editor.Canvas.Children.OfType<Controls.TextBox>().Single().Text = "Pending recent edit";
                var history = new HistoryWindow(owner.TestStore,_ => { },capture => owner.DeleteCapture(capture)); history.Show();
                var remove = Descendants(history).OfType<Controls.Button>().Single(button => Equals(button.Tag,id));
                int count = owner.TestStore.Records.Count;
                owner.ConfirmCaptureDeletion = _ => false; remove.RaiseEvent(new Wpf.RoutedEventArgs(Controls.Button.ClickEvent));
                Require(owner.TestStore.Records.Count == count && editor.IsVisible,"Cancelled recent deletion removed a draft or closed its editor.");
                owner.ConfirmCaptureDeletion = _ => true; remove.RaiseEvent(new Wpf.RoutedEventArgs(Controls.Button.ClickEvent)); history.UpdateLayout();
                Require(!editor.IsVisible && owner.TestStore.Records.Count == count-1 && !Descendants(history).OfType<Controls.Button>().Any(button => Equals(button.Tag,id)),"Recent deletion left its card or an open editor behind.");
                Require(!new CaptureStore(owner.TestStore.Root).Contains(id),"Recent deletion did not survive restart."); history.Close();
            });
            Check("failed deletion retains the capture and resumes active-text autosave",() =>
            {
                var editor = owner.OpenNew(source); Guid id = editor.CaptureId; editor.Canvas.BeginText(new(20,20));
                var box = editor.Canvas.Children.OfType<Controls.TextBox>().Single(); box.Text = "Keep this typing";
                Exception? failure = null; owner.ConfirmCaptureDeletion = _ => true; owner.ReportDeleteError = error => failure = error;
                failRecycling = true;
                try { Require(!owner.DeleteCapture(id),"A failed recycle reported success."); }
                finally { failRecycling = false; }
                Require(failure is IOException && editor.IsVisible && editor.Canvas.IsEditing && owner.TestStore.Contains(id),"Failed deletion closed the editor or lost its active text.");
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                timer.Tick += (_,_) => { timer.Stop(); frame.Continue = false; }; timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
                Require(new CaptureStore(owner.TestStore.Root).Get(id).Marks.Single().Text == "Keep this typing","Autosave did not resume after failed deletion."); editor.Close();
            });
            Check("delete discards an open capture outside recent history without removing another draft",() =>
            {
                var editor = owner.OpenNew(source); var latest = owner.OpenNew(source); int limit = owner.TestStore.Limit;
                try
                {
                    owner.TestStore.SetLimit(1); Require(!owner.TestStore.Contains(editor.CaptureId),"Could not isolate an evicted capture.");
                    editor.Canvas.BeginText(new(20,20)); owner.ConfirmCaptureDeletion = _ => true;
                    Require(owner.DeleteCapture(editor.CaptureId) && !editor.IsVisible && latest.IsVisible && owner.TestStore.Contains(latest.CaptureId),"Discarding an evicted capture affected the retained draft.");
                }
                finally { owner.TestStore.SetLimit(limit); editor.CloseAfterQuit(); latest.Close(); }
            });
            Check("settings show inline errors and actual retention counts and respect cancellation",() =>
            {
                int applied = 0, confirmed = 0; UserSettings? saved = null;
                var settings = new SettingsWindow(new UserSettings { HasSeenWelcome = true },candidate => { applied++; saved = candidate; return null; },() => { },() => 5) { Height = 480 };
                settings.Show(); settings.UpdateLayout(); settings.LimitField.Text = "0"; settings.SaveChanges(); settings.UpdateLayout();
                settings.Dispatcher.Invoke(() => { },System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Require(applied == 0 && settings.HistoryError.Visibility == Wpf.Visibility.Visible && settings.ShortcutError.Visibility == Wpf.Visibility.Collapsed,"Retention error appeared in the shortcut section.");
                var errorBounds = settings.HistoryError.TransformToAncestor(settings.BodyScroll).TransformBounds(new Wpf.Rect(settings.HistoryError.RenderSize));
                var fieldBounds = settings.LimitField.TransformToAncestor(settings.BodyScroll).TransformBounds(new Wpf.Rect(settings.LimitField.RenderSize));
                Require(fieldBounds.Top >= 0 && errorBounds.Bottom <= settings.BodyScroll.ViewportHeight+1,"Focused retention field scrolled its error out of view.");
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
