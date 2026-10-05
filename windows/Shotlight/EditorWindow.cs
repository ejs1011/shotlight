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
    public Guid CaptureId { get; private set; }
    public AnnotationCanvas Canvas { get; private set; }
    private readonly Controls.ScrollViewer scroll = new() { HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto, Background = Ui.Brush("#E9ECF2"), HorizontalContentAlignment = Wpf.HorizontalAlignment.Center, VerticalContentAlignment = Wpf.VerticalAlignment.Center };
    private readonly Controls.Button previous, next, undo, redo;
    private readonly Controls.Border canvasFrame = new() { Margin = new Wpf.Thickness(32), Background = Media.Brushes.White, Effect = new Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 3, Opacity = .14 } };
    private readonly System.Windows.Shapes.Ellipse colorChip = new() { Width = 16, Height = 16, Stroke = Ui.Line, StrokeThickness = 1 };
    private readonly Controls.TextBlock dimensions = Ui.Label("",12,Ui.Muted);
    private readonly Controls.TextBlock status = new() { VerticalAlignment = Wpf.VerticalAlignment.Center, Foreground = Ui.Muted, Margin = new Wpf.Thickness(6,0,0,0) };
    private readonly System.Windows.Threading.DispatcherTimer autosave = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool permitClose;
    private bool exported;
    private double zoom = 1;
    public EditorWindow(AppController owner, CaptureStore store, Guid id, byte[]? unretainedPng = null, CaptureDraft? unretainedDraft = null)
    {
        this.owner = owner; this.store = store; CaptureId = id;
        var draft = unretainedDraft ?? store.Get(id); Canvas = new(AnnotationCanvas.Read(unretainedPng ?? store.Original(id)),new AnnotationDocument(draft));
        Title = "Shotlight — Annotate Screenshot"; Width = 1200; Height = 820; MinWidth = 760; MinHeight = 460;
        Ui.Apply(this);
        var root = new Controls.DockPanel(); Content = root;
        var header = new Controls.Grid { Margin = new Wpf.Thickness(22,18,18,18) };
        header.ColumnDefinitions.Add(new Controls.ColumnDefinition()); header.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = Wpf.GridLength.Auto });
        var branding = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
        var logo = new Controls.Border { Width = 40, Height = 40, Background = Ui.Accent, CornerRadius = new Wpf.CornerRadius(12), Margin = new Wpf.Thickness(0,0,12,0) };
        var camera = Ui.Icon("Capture",22); camera.Stroke = Media.Brushes.White; logo.Child = camera; branding.Children.Add(logo);
        var name = new Controls.StackPanel(); name.Children.Add(Ui.Label("Shotlight",21,bold:true)); name.Children.Add(Ui.Label("Capture, annotate, keep.",12,Ui.Muted)); branding.Children.Add(name); header.Children.Add(branding);
        var actions = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal, VerticalAlignment = Wpf.VerticalAlignment.Center };
        actions.Children.Add(Ui.Button("New capture",owner.QueueCapture,"Capture",hint:"Capture a new area"));
        actions.Children.Add(Ui.Button("Save",Save,"Save",hint:"Save PNG · Ctrl+S"));
        actions.Children.Add(Ui.Button("Copy & Close",() => CopyAndClose(),"Copy","PrimaryButton","Copy screenshot and close · Ctrl+C"));
        actions.Children.Add(Ui.Button("",owner.ShowSettings,"Settings","QuietButton","Settings"));
        Controls.Grid.SetColumn(actions,1); header.Children.Add(actions); Controls.DockPanel.SetDock(header,Controls.Dock.Top); root.Children.Add(header);

        var toolbar = new Controls.WrapPanel { Margin = new Wpf.Thickness(18,10,12,10), VerticalAlignment = Wpf.VerticalAlignment.Center };
        var toolbarSurface = new Controls.Border { Child = toolbar, Background = Media.Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Wpf.Thickness(0,1,0,1) };
        Controls.DockPanel.SetDock(toolbarSurface,Controls.Dock.Top); root.Children.Add(toolbarSurface);
        var toolButtons = new Dictionary<Tool,Controls.Primitives.ToggleButton>();
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var button = new Controls.Primitives.ToggleButton { Content = Ui.ButtonContent(tool.ToString(),tool.ToString()), IsChecked = tool == Tool.Arrow, Margin = new Wpf.Thickness(0,0,4,0), ToolTip = tool == Tool.Text ? "Click the screenshot to type. Click existing text to edit it." : tool.ToString() };
            Wpf.Automation.AutomationProperties.SetName(button,tool.ToString());
            button.Click += (_,_) => { Canvas.FinishText(); Canvas.Tool = tool; foreach (var pair in toolButtons) pair.Value.IsChecked = pair.Key == tool; };
            toolButtons.Add(tool,button); toolbar.Children.Add(button);
        }
        toolbar.Children.Add(Ui.Divider());
        foreach (var (argb,label) in new[] { (0xFFFF3B30u,"Red"),(0xFF5265E9u,"Indigo"),(0xFF16A085u,"Green"),(0xFFFFB020u,"Amber"),(0xFF202637u,"Ink"),(0xFFFFFFFFu,"White") })
        {
            var swatch = Ui.Button("",() => { Canvas.Argb = argb; RefreshColor(); },hint:label + " annotation color");
            swatch.Padding = new Wpf.Thickness(7); swatch.Margin = new Wpf.Thickness(0,0,3,0); swatch.BorderThickness = new Wpf.Thickness(0);
            swatch.Content = new System.Windows.Shapes.Ellipse { Width = 16, Height = 16, Fill = Ui.Brush($"#{argb:X8}"), Stroke = Ui.Line, StrokeThickness = 1 };
            toolbar.Children.Add(swatch);
        }
        var color = Ui.Button("",ChooseColor,hint:"Choose a custom annotation color"); color.Content = colorChip; color.Padding = new Wpf.Thickness(10); toolbar.Children.Add(color); RefreshColor();
        var widths = new Controls.ComboBox { Width = 104, Margin = new Wpf.Thickness(6,0,4,0), ToolTip = "Stroke and text size" };
        Wpf.Automation.AutomationProperties.SetName(widths,"Stroke and text size");
        foreach (var text in new[] { "Thin","Medium","Thick" }) widths.Items.Add(text);
        widths.SelectedIndex = 1; widths.SelectionChanged += (_,_) => Canvas.StrokeWidth = new float[] { 2,4,8 }[widths.SelectedIndex]; toolbar.Children.Add(widths);
        toolbar.Children.Add(Ui.Divider());
        undo = Ui.Button("",() => Canvas.Undo(),"Undo","QuietButton","Undo · Ctrl+Z"); redo = Ui.Button("",() => Canvas.Redo(),"Redo","QuietButton","Redo · Ctrl+Y"); toolbar.Children.Add(undo); toolbar.Children.Add(redo);
        foreach (Wpf.FrameworkElement item in toolbar.Children) { var margin = item.Margin; item.Margin = new Wpf.Thickness(margin.Left,margin.Top,margin.Right,4); }

        var footer = new Controls.DockPanel { Margin = new Wpf.Thickness(16,10,16,10) };
        var footerSurface = new Controls.Border { Child = footer, Background = Media.Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Wpf.Thickness(0,1,0,0) };
        Controls.DockPanel.SetDock(footerSurface,Controls.Dock.Bottom); root.Children.Add(footerSurface);
        var zoomControls = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
        zoomControls.Children.Add(dimensions); dimensions.Margin = new Wpf.Thickness(0,0,16,0);
        var zoomBox = new Controls.ComboBox { Width = 90, ToolTip = "Preview zoom. Exports keep full resolution." };
        Wpf.Automation.AutomationProperties.SetName(zoomBox,"Preview zoom");
        foreach (var label in new[] { "25%","50%","75%","100%","150%","200%" }) zoomBox.Items.Add(label);
        zoomBox.SelectedIndex = 3;
        zoomBox.SelectionChanged += (_,_) => { zoom = new double[] { .25,.5,.75,1,1.5,2 }[zoomBox.SelectedIndex]; Canvas.LayoutTransform = new Media.ScaleTransform(zoom,zoom); };
        zoomControls.Children.Add(zoomBox); Controls.DockPanel.SetDock(zoomControls,Controls.Dock.Right); footer.Children.Add(zoomControls);
        var navigation = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
        previous = Ui.Button("",() => owner.Navigate(this,1),"Previous","QuietButton","Previous capture"); next = Ui.Button("",() => owner.Navigate(this,-1),"Next","QuietButton","Next capture");
        navigation.Children.Add(previous); navigation.Children.Add(next); navigation.Children.Add(Ui.Button("Recent captures",owner.ShowHistory,"History","QuietButton")); navigation.Children.Add(status); footer.Children.Add(navigation);
        root.Children.Add(scroll); canvasFrame.Child = Canvas; scroll.Content = canvasFrame; Canvas.Changed += OnChanged;
        autosave.Tick += (_,_) => { autosave.Stop(); SaveDraft(showError: false); };
        Closing += OnClosing; Closed += (_,_) => { autosave.Stop(); Canvas.Changed -= OnChanged; owner.EditorClosed(this); };
        PreviewKeyDown += OnKey;
        Loaded += (_,_) =>
        {
            var area = Wpf.SystemParameters.WorkArea; Width = Math.Min(Width,Math.Max(MinWidth,area.Width-64)); Height = Math.Min(Height,Math.Max(MinHeight,area.Height-64));
            Canvas.Focus(); UpdateNavigation();
        };
        UpdateNavigation();
    }
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
                case Input.Key.C: CopyAndClose(copy); return true;
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
        exported = false; undo.IsEnabled = Canvas.IsEditing || Canvas.Document.UndoStack.Count > 0; redo.IsEnabled = Canvas.Document.RedoStack.Count > 0;
        autosave.Stop(); if (store.Contains(CaptureId)) autosave.Start();
    }
    public bool Flush() { autosave.Stop(); Canvas.FinishText(); autosave.Stop(); return SaveDraft(showError: true); }
    private bool SaveDraft(bool showError)
    {
        if (!store.Contains(CaptureId)) { UpdateNavigation(); return true; }
        try { store.Save(CaptureId,Canvas.DraftSnapshot()); UpdateNavigation(); return true; }
        catch (Exception error)
        {
            status.Text = "Draft not retained — save or copy";
            if (showError) Ui.Error("Could not retain this screenshot",error); return false;
        }
    }
    public void UpdateNavigation()
    {
        int index = store.Records.FindIndex(r => r.Id == CaptureId);
        previous.IsEnabled = index >= 0 && index+1 < store.Records.Count; next.IsEnabled = index > 0;
        status.Text = index >= 0 ? $"{index+1} of {store.Records.Count} · Saved to history" : "Outside history — save or copy";
        status.ToolTip = index >= 0 ? store.Records[index].CapturedAt.LocalDateTime.ToString("f") : status.Text;
        dimensions.Text = $"{Canvas.Original.PixelWidth} × {Canvas.Original.PixelHeight} px";
        undo.IsEnabled = Canvas.IsEditing || Canvas.Document.UndoStack.Count > 0; redo.IsEnabled = Canvas.Document.RedoStack.Count > 0;
    }
    public void LoadCapture(Guid id)
    {
        if (!Flush()) return;
        var draft = store.Get(id); var image = AnnotationCanvas.Read(store.Original(id));
        var canvas = new AnnotationCanvas(image,new AnnotationDocument(draft)) { Tool = Canvas.Tool, Argb = Canvas.Argb, StrokeWidth = Canvas.StrokeWidth, LayoutTransform = new Media.ScaleTransform(zoom,zoom) };
        Canvas.Changed -= OnChanged; Canvas = canvas; CaptureId = id; exported = false; Canvas.Changed += OnChanged;
        canvasFrame.Child = Canvas; scroll.ScrollToTop(); scroll.ScrollToLeftEnd(); Canvas.Focus(); UpdateNavigation();
    }
    internal bool CopyAndClose(Action<Bitmap>? copy = null)
    {
        try
        {
            byte[] png = Canvas.ExportPng(); if (!Flush()) return false;
            using var bitmap = ImageFiles.Read(png); (copy ?? ImageFiles.Copy)(bitmap);
            permitClose = true; Close(); return true;
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
