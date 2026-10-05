using System.ComponentModel;
using Shotlight.Core;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;
using Input = System.Windows.Input;

namespace Shotlight;

internal sealed class EditorWindow : Wpf.Window
{
    private readonly AppController owner;
    private readonly CaptureStore store;
    private readonly Action<Bitmap> copyWriter;
    public Guid CaptureId { get; private set; }
    public AnnotationCanvas Canvas { get; private set; }
    private readonly Controls.ScrollViewer scroll = new() { HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto, Background = Ui.Brush("#E9ECF2"), HorizontalContentAlignment = Wpf.HorizontalAlignment.Center, VerticalContentAlignment = Wpf.VerticalAlignment.Center };
    private readonly Controls.Button undo, redo, copyButton, weightButton;
    private readonly Controls.ContextMenu weights = Ui.Menu();
    private readonly Controls.MenuItem zoomMenu;
    private readonly Controls.Primitives.ToggleButton fitButton = new() { Content = "Fit" }, actualButton = new() { Content = "100%" };
    private readonly Controls.TextBlock zoomLabel = Ui.Label("100%",12,Ui.Muted);
    private bool fittingPreview = true, fitQueued;
    private string? archiveError;
    public bool CloseEditorAfterCopy { get; private set; }
    private readonly Controls.MenuItem previous, next;
    private readonly Controls.Border canvasFrame = new() { Margin = new Wpf.Thickness(28), Background = Media.Brushes.White, Effect = new Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 3, Opacity = .14 } };
    private readonly System.Windows.Shapes.Ellipse colorChip = new() { Width = 16, Height = 16, Stroke = Ui.Line, StrokeThickness = 1 };
    private readonly Controls.TextBlock dimensions = Ui.Label("",12,Ui.Muted);
    private readonly Controls.TextBlock status = new() { VerticalAlignment = Wpf.VerticalAlignment.Center, Foreground = Ui.Muted, Margin = new Wpf.Thickness(6,0,0,0) };
    private readonly System.Windows.Threading.DispatcherTimer autosave = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool permitClose;
    private bool exported;
    private double zoom = 1;
    public EditorWindow(AppController owner, CaptureStore store, Guid id, byte[]? unretainedPng = null, CaptureDraft? unretainedDraft = null, Action<Bitmap>? copyWriter = null)
    {
        this.owner = owner; this.store = store; this.copyWriter = copyWriter ?? ImageFiles.Copy; CaptureId = id; CloseEditorAfterCopy = owner.Settings.CloseEditorAfterCopy;
        var draft = unretainedDraft ?? store.Get(id); Canvas = new(AnnotationCanvas.Read(unretainedPng ?? store.Original(id)),new AnnotationDocument(draft));
        Title = "Shotlight — Annotate Screenshot"; Width = 1200; Height = 820; MinWidth = 760; MinHeight = 460;
        Ui.Apply(this);
        var root = new Controls.Grid(); Content = root;
        root.RowDefinitions.Add(new() { Height = Wpf.GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = Wpf.GridLength.Auto });
        Controls.Grid.SetRow(scroll,1); root.Children.Add(scroll); canvasFrame.Child = Canvas; scroll.Content = canvasFrame; Canvas.Changed += OnChanged;
        var header = new Controls.DockPanel { Margin = new Wpf.Thickness(24,12,24,0) };
        var scaleControls = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
        foreach (var button in new[] { fitButton, actualButton }) { button.MinHeight = 30; button.Padding = new Wpf.Thickness(12,5,12,5); }
        fitButton.Click += (_,_) => FitToWindow(); actualButton.Click += (_,_) => SetZoom(1);
        zoomLabel.Margin = new Wpf.Thickness(10,0,0,0); zoomLabel.MinWidth = 46;
        scaleControls.Children.Add(fitButton); scaleControls.Children.Add(actualButton); scaleControls.Children.Add(zoomLabel);
        Controls.DockPanel.SetDock(scaleControls,Controls.Dock.Right); header.Children.Add(scaleControls);
        status.TextTrimming = Wpf.TextTrimming.CharacterEllipsis; header.Children.Add(status); root.Children.Add(header);
        scroll.SizeChanged += (_,_) => QueueFit();
        scroll.ScrollChanged += (_,e) => { if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) QueueFit(); };
        scroll.PreviewMouseWheel += (_,e) => { if (Input.Keyboard.Modifiers == Input.ModifierKeys.Control) { SetZoom(zoom*(e.Delta > 0 ? 1.1 : 1/1.1)); e.Handled = true; } };

        var toolbar = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
        var toolbarSurface = Ui.Card(toolbar,new Wpf.Thickness(7));
        toolbarSurface.HorizontalAlignment = Wpf.HorizontalAlignment.Center; toolbarSurface.VerticalAlignment = Wpf.VerticalAlignment.Bottom;
        toolbarSurface.Margin = new Wpf.Thickness(12); toolbarSurface.Effect = new Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Opacity = .16 };
        Controls.Grid.SetRow(toolbarSurface,2); root.Children.Add(toolbarSurface);
        toolbar.Children.Add(Ui.Button("",owner.QueueCapture,"Capture","QuietButton","New capture"));
        toolbar.Children.Add(Ui.Divider());
        var toolButtons = new Dictionary<Tool,Controls.Primitives.ToggleButton>();
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var button = new Controls.Primitives.ToggleButton { Content = Ui.ButtonContent("",tool.ToString()), IsChecked = tool == Tool.Arrow,
                Width = 34, Height = 36, MinHeight = 36, Padding = new Wpf.Thickness(7), Margin = new Wpf.Thickness(0), ToolTip = tool == Tool.Text ? "Text · Click to type or edit an annotation" : tool.ToString() };
            Wpf.Automation.AutomationProperties.SetName(button,tool.ToString());
            button.Click += (_,_) => { Canvas.FinishText(); Canvas.Tool = tool; RefreshSize(); foreach (var pair in toolButtons) pair.Value.IsChecked = pair.Key == tool; };
            toolButtons.Add(tool,button); toolbar.Children.Add(button);
        }
        toolbar.Children.Add(Ui.Divider());
        var colorMenu = Ui.Menu();
        foreach (var (argb,label) in new[] { (0xFFFF3B30u,"Red"),(0xFF5265E9u,"Indigo"),(0xFF16A085u,"Green"),(0xFFFFB020u,"Amber"),(0xFF202637u,"Ink"),(0xFFFFFFFFu,"White") })
        {
            var item = Ui.MenuItem(label,() => { Canvas.Argb = argb; RefreshColor(); });
            item.Icon = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = Ui.Brush($"#{argb:X8}"), Stroke = Ui.Line, StrokeThickness = 1 }; colorMenu.Items.Add(item);
        }
        colorMenu.Items.Add(new Controls.Separator()); colorMenu.Items.Add(Ui.MenuItem("Custom color…",ChooseColor));
        var color = Ui.Button("",() => { },style:"QuietButton",hint:"Annotation color"); color.Content = colorChip; Ui.AttachMenu(color,colorMenu); toolbar.Children.Add(color); RefreshColor();
        weightButton = Ui.Button("4 px",() => { },"Weight","QuietButton","Stroke width");
        Ui.AttachMenu(weightButton,weights); toolbar.Children.Add(weightButton); toolbar.Children.Add(Ui.Divider());
        undo = Ui.Button("",() => Canvas.Undo(),"Undo","QuietButton","Undo · Ctrl+Z"); redo = Ui.Button("",() => Canvas.Redo(),"Redo","QuietButton","Redo · Ctrl+Y"); toolbar.Children.Add(undo); toolbar.Children.Add(redo);
        toolbar.Children.Add(Ui.Divider());
        copyButton = Ui.Button("Copy & Close",() => Copy(),"Copy","PrimaryButton"); toolbar.Children.Add(copyButton);
        toolbar.Children.Add(Ui.Button("",Save,"Save","QuietButton","Save PNG · Ctrl+S"));
        var more = Ui.Menu();
        var info = new Controls.MenuItem { IsEnabled = false };
        info.SetBinding(Controls.HeaderedItemsControl.HeaderProperty,new System.Windows.Data.Binding("Text") { Source = dimensions }); more.Items.Add(info);
        more.Items.Add(Ui.MenuItem("Recent captures…",owner.ShowHistory,"History"));
        previous = Ui.MenuItem("Previous capture",() => owner.Navigate(this,1),"Previous"); next = Ui.MenuItem("Next capture",() => owner.Navigate(this,-1),"Next");
        more.Items.Add(previous); more.Items.Add(next); more.Items.Add(new Controls.Separator());
        zoomMenu = new Controls.MenuItem { Header = "Zoom", Icon = Ui.Icon("Zoom") };
        var fitItem = Ui.MenuItem("Fit to Window",FitToWindow); fitItem.Tag = 0d; fitItem.IsCheckable = true; zoomMenu.Items.Add(fitItem);
        foreach (double scale in new[] { .25,.5,.75,1,1.5,2 })
        {
            var item = Ui.MenuItem($"{scale*100:0}%",() => SetZoom(scale)); item.Tag = scale; item.IsCheckable = true; zoomMenu.Items.Add(item);
        }
        more.Items.Add(zoomMenu); more.Items.Add(Ui.MenuItem("Settings…",owner.ShowSettings,"Settings"));
        var moreButton = Ui.Button("",() => { },"More","QuietButton","More · History, zoom & settings"); Ui.AttachMenu(moreButton,more); toolbar.Children.Add(moreButton);
        foreach (var item in toolbar.Children.OfType<Controls.Button>()) { item.Width = item == copyButton ? 132 : item == weightButton ? 80 : 34; item.Height = 36; item.MinHeight = 36; item.Padding = new Wpf.Thickness(7); item.Margin = new Wpf.Thickness(0); }
        autosave.Tick += (_,_) => { autosave.Stop(); SaveDraft(showError: false); };
        Closing += OnClosing; Closed += (_,_) => { autosave.Stop(); Canvas.Changed -= OnChanged; owner.EditorClosed(this); };
        PreviewKeyDown += OnKey;
        Loaded += (_,_) =>
        {
            var area = Wpf.SystemParameters.WorkArea; Width = Math.Min(Width,Math.Max(MinWidth,area.Width-64)); Height = Math.Min(Height,Math.Max(MinHeight,area.Height-64));
            Canvas.Focus(); UpdateNavigation(); QueueFit();
        };
        UpdateNavigation(); RefreshSize(); ApplyCopyPreference(CloseEditorAfterCopy);
    }
    internal void ApplyCopyPreference(bool closes)
    {
        CloseEditorAfterCopy = closes; string label = closes ? "Copy & Close" : "Copy";
        copyButton.Content = Ui.ButtonContent(label,"Copy"); copyButton.ToolTip = label + " · Ctrl+C";
        Wpf.Automation.AutomationProperties.SetName(copyButton,label);
    }
    private void RefreshSize()
    {
        bool text = Canvas.Tool == Tool.Text; float selected = text ? Canvas.FontSizePoints : Canvas.StrokeWidth; string unit = text ? "pt" : "px";
        weightButton.Content = Ui.ButtonContent($"{selected:0.#} {unit}",text ? "Text" : "Weight"); weightButton.ToolTip = text ? "Text size" : "Stroke width";
        Wpf.Automation.AutomationProperties.SetName(weightButton,$"{weightButton.ToolTip}: {selected:0.#} {unit}");
        weights.Items.Clear();
        foreach (float value in text ? new[] { 16f,24f,48f } : new[] { 2f,4f,8f })
        {
            var item = Ui.MenuItem($"{value:0} {unit}",() => { if (Canvas.Tool == Tool.Text) Canvas.FontSizePoints = value; else Canvas.StrokeWidth = value; RefreshSize(); });
            item.IsCheckable = true; item.IsChecked = Math.Abs(selected-value) < .01; weights.Items.Add(item);
        }
    }
    private void QueueFit()
    {
        if (!fittingPreview || fitQueued) return; fitQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(() => { fitQueued = false; if (fittingPreview && IsLoaded) FitToWindow(); }));
    }
    internal void FitToWindow()
    {
        double width = scroll.ViewportWidth > 0 ? scroll.ViewportWidth : scroll.ActualWidth;
        double height = scroll.ViewportHeight > 0 ? scroll.ViewportHeight : scroll.ActualHeight;
        SetZoom(PreviewScale.Fit(Canvas.Width,Canvas.Height,width,height),fit:true);
    }
    internal void SetZoom(double scale, bool fit = false)
    {
        fittingPreview = fit; zoom = Math.Clamp(scale,.01,2); Canvas.LayoutTransform = new Media.ScaleTransform(zoom,zoom);
        fitButton.IsChecked = fit; actualButton.IsChecked = !fit && Math.Abs(zoom-1) < .001; zoomLabel.Text = $"{zoom*100:0}%";
        foreach (var item in zoomMenu.Items.OfType<Controls.MenuItem>()) item.IsChecked = (double)item.Tag == 0 ? fit : !fit && Math.Abs((double)item.Tag-zoom) < .001;
        UpdateNavigation();
    }
    internal bool IsFittingPreview => fittingPreview;
    internal Controls.ScrollViewer PreviewScroll => scroll;
    internal Controls.Button CopyButton => copyButton;
    private void OnKey(object sender, Input.KeyEventArgs e)
    {
        if (HandleShortcut(e.Key,Input.Keyboard.Modifiers)) e.Handled = true;
    }
    internal bool HandleShortcut(Input.Key key, Input.ModifierKeys modifiers, Action<Bitmap>? copy = null)
    {
        if (modifiers == Input.ModifierKeys.Control)
        {
            switch (key)
            {
                case Input.Key.C: Copy(copy); return true;
                case Input.Key.S: Save(); return true;
                case Input.Key.Z: Canvas.Undo(); return true;
                case Input.Key.Y: Canvas.Redo(); return true;
            }
        }
        if (key == Input.Key.Z && modifiers == (Input.ModifierKeys.Control | Input.ModifierKeys.Shift)) { Canvas.Redo(); return true; }
        return false;
    }
    private void ChooseColor()
    {
        using var chooser = new ColorDialog { FullOpen = true, Color = Color.FromArgb(unchecked((int)Canvas.Argb)) };
        if (chooser.ShowDialog() == System.Windows.Forms.DialogResult.OK) { Canvas.Argb = unchecked((uint)chooser.Color.ToArgb()); RefreshColor(); }
    }
    private void RefreshColor() => colorChip.Fill = Ui.Brush($"#{Canvas.Argb:X8}");
    private void OnChanged()
    {
        exported = false; RefreshSize(); undo.IsEnabled = Canvas.IsEditing || Canvas.Document.UndoStack.Count > 0; redo.IsEnabled = Canvas.Document.RedoStack.Count > 0;
        autosave.Stop(); if (store.Contains(CaptureId)) { status.Text = "Saving to Recent Captures…"; autosave.Start(); }
    }
    public bool Flush() { autosave.Stop(); Canvas.FinishText(); autosave.Stop(); return SaveDraft(showError: true); }
    private bool SaveDraft(bool showError)
    {
        if (!store.Contains(CaptureId)) { UpdateNavigation(); return true; }
        try
        {
            var snapshot = Canvas.DraftSnapshot();
            byte[]? thumbnail = !store.ThumbnailIsCurrent(CaptureId) || !AnnotationDocument.Equal(store.Get(CaptureId).Marks,snapshot.Marks) ? Canvas.ThumbnailPng(snapshot.Marks) : null;
            store.Save(CaptureId,snapshot,thumbnail); archiveError = null; UpdateNavigation(); return true;
        }
        catch (Exception error)
        {
            archiveError = error.Message; status.Text = "Draft not retained — save or copy"; status.Visibility = Wpf.Visibility.Visible;
            if (showError) Ui.Error("Could not retain this screenshot",error); return false;
        }
    }
    public void UpdateNavigation()
    {
        int index = store.Records.FindIndex(r => r.Id == CaptureId);
        previous.IsEnabled = index >= 0 && index+1 < store.Records.Count; next.IsEnabled = index > 0;
        status.Text = index >= 0 ? archiveError is null ? "Saved to Recent Captures" : "Draft not retained — save or copy" : "Outside history — save or copy";
        status.Visibility = Wpf.Visibility.Visible;
        status.ToolTip = index >= 0 ? store.Records[index].CapturedAt.LocalDateTime.ToString("f") : status.Text;
        dimensions.Text = $"{Canvas.Original.PixelWidth} × {Canvas.Original.PixelHeight} px · {zoom*100:0}%";
        undo.IsEnabled = Canvas.IsEditing || Canvas.Document.UndoStack.Count > 0; redo.IsEnabled = Canvas.Document.RedoStack.Count > 0;
    }
    public void LoadCapture(Guid id)
    {
        if (!Flush()) return;
        var draft = store.Get(id); var image = AnnotationCanvas.Read(store.Original(id));
        var canvas = new AnnotationCanvas(image,new AnnotationDocument(draft)) { Tool = Canvas.Tool, Argb = Canvas.Argb, StrokeWidth = Canvas.StrokeWidth, FontSizePoints = Canvas.FontSizePoints };
        Canvas.Changed -= OnChanged; Canvas = canvas; CaptureId = id; exported = false; Canvas.Changed += OnChanged;
        canvasFrame.Child = Canvas; archiveError = null; scroll.ScrollToTop(); scroll.ScrollToLeftEnd(); Canvas.Focus(); UpdateNavigation(); RefreshSize(); FitToWindow();
    }
    internal bool Copy(Action<Bitmap>? copy = null)
    {
        try
        {
            byte[] png = Canvas.ExportPng(); if (!Flush()) return false;
            using var bitmap = ImageFiles.Read(png); (copy ?? copyWriter)(bitmap);
            exported = true;
            if (CloseEditorAfterCopy) { permitClose = true; Close(); }
            else status.Text = "Copied · " + (store.Contains(CaptureId) ? "Saved to Recent Captures" : "Keep this editor open to retain your draft");
            return true;
        }
        catch (Exception error) { Ui.Error("Could not copy screenshot",error); return false; }
    }
    private void Save()
    {
        Canvas.FinishText(); var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", AddExtension = true, FileName = $"Screenshot-{DateTime.Now:yyyy-MM-dd-HHmmss}.png", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            byte[] png = Canvas.ExportPng(); AtomicFile.Write(dialog.FileName,png); exported = true;
            Title = "Shotlight — " + Path.GetFileName(dialog.FileName); _ = Flush();
        }
        catch (Exception error) { Ui.Error("Could not save screenshot",error); }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (permitClose) return;
        if (!Flush()) { e.Cancel = true; return; }
        if (!store.Contains(CaptureId) && !exported)
            e.Cancel = Wpf.MessageBox.Show(this,"This screenshot is outside recent history. Close and discard it without saving or copying?","Close screenshot?",Wpf.MessageBoxButton.YesNo,Wpf.MessageBoxImage.Question) != Wpf.MessageBoxResult.Yes;
    }
    public void CloseAfterClear() { permitClose = true; Close(); }
    public void CloseAfterQuit() { permitClose = true; Close(); }
}
