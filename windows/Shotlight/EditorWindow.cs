using System.ComponentModel;
using Shotlight.Core;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;
using Input = System.Windows.Input;

namespace Shotlight;

internal static class Ui
{
    public static Controls.Button Button(string text, Action click)
    {
        var button = new Controls.Button { Content = text, Padding = new Wpf.Thickness(12,6,12,6), Margin = new Wpf.Thickness(0,0,8,6), MinHeight = 32 };
        button.Click += (_,_) => click(); return button;
    }
    public static void Error(string title, Exception error) => Wpf.MessageBox.Show(error.Message,title,Wpf.MessageBoxButton.OK,Wpf.MessageBoxImage.Error);
    public static Wpf.Window Window(string title, double width, double height) => new()
    { Title = title, Width = width, Height = height, FontFamily = new Media.FontFamily("Segoe UI"), FontSize = 14, WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen };
}

internal sealed class EditorWindow : Wpf.Window
{
    private readonly AppController owner;
    private readonly CaptureStore store;
    public Guid CaptureId { get; private set; }
    public AnnotationCanvas Canvas { get; private set; }
    private readonly Controls.ScrollViewer scroll = new() { HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto, Background = new Media.SolidColorBrush(Media.Color.FromRgb(44,48,56)) };
    private readonly Controls.Button previous, next, undo, redo;
    private readonly Controls.TextBlock status = new() { VerticalAlignment = Wpf.VerticalAlignment.Center, Foreground = Media.Brushes.DimGray, Margin = new Wpf.Thickness(4,0,0,6) };
    private readonly System.Windows.Threading.DispatcherTimer autosave = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool permitClose;
    private bool exported;
    private double zoom = 1;
    public EditorWindow(AppController owner, CaptureStore store, Guid id, byte[]? unretainedPng = null, CaptureDraft? unretainedDraft = null)
    {
        this.owner = owner; this.store = store; CaptureId = id;
        var draft = unretainedDraft ?? store.Get(id); Canvas = new(AnnotationCanvas.Read(unretainedPng ?? store.Original(id)),new AnnotationDocument(draft));
        Title = "Shotlight — Annotate Screenshot"; Width = 1120; Height = 780; MinWidth = 760; MinHeight = 320;
        FontFamily = new Media.FontFamily("Segoe UI"); FontSize = 14; WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen;
        var root = new Controls.DockPanel(); Content = root;
        var toolbar = new Controls.WrapPanel { Margin = new Wpf.Thickness(12,12,4,0) };
        Controls.DockPanel.SetDock(toolbar,Controls.Dock.Top); root.Children.Add(toolbar);
        toolbar.Children.Add(Ui.Button("Capture",owner.QueueCapture));
        var toolButtons = new Dictionary<Tool,Controls.Button>();
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var button = Ui.Button(tool.ToString(), () =>
            {
                Canvas.FinishText(); Canvas.Tool = tool;
                foreach (var pair in toolButtons) pair.Value.FontWeight = pair.Key == tool ? Wpf.FontWeights.Bold : Wpf.FontWeights.Normal;
            });
            button.FontWeight = tool == Tool.Arrow ? Wpf.FontWeights.Bold : Wpf.FontWeights.Normal;
            toolButtons.Add(tool,button); toolbar.Children.Add(button);
        }
        var color = Ui.Button("Color…",ChooseColor); toolbar.Children.Add(color);
        var widths = new Controls.ComboBox { Width = 92, Margin = new Wpf.Thickness(0,0,8,6), VerticalContentAlignment = Wpf.VerticalAlignment.Center };
        foreach (var text in new[] { "Thin","Medium","Thick" }) widths.Items.Add(text);
        widths.SelectedIndex = 1; widths.SelectionChanged += (_,_) => Canvas.StrokeWidth = new float[] { 2,4,8 }[widths.SelectedIndex]; toolbar.Children.Add(widths);
        undo = Ui.Button("Undo",() => Canvas.Undo()); redo = Ui.Button("Redo",() => Canvas.Redo()); toolbar.Children.Add(undo); toolbar.Children.Add(redo);
        toolbar.Children.Add(Ui.Button("Copy & Close",() => CopyAndClose())); toolbar.Children.Add(Ui.Button("Save…",Save));
        toolbar.Children.Add(Ui.Button("Settings…",owner.ShowSettings));
        var navigation = new Controls.WrapPanel { Margin = new Wpf.Thickness(12,0,4,4) };
        Controls.DockPanel.SetDock(navigation,Controls.Dock.Top); root.Children.Add(navigation);
        previous = Ui.Button("Previous",() => owner.Navigate(this,1)); next = Ui.Button("Next",() => owner.Navigate(this,-1));
        navigation.Children.Add(previous); navigation.Children.Add(next); navigation.Children.Add(Ui.Button("Recent Captures…",owner.ShowHistory));
        var zoomBox = new Controls.ComboBox { Width = 82, Margin = new Wpf.Thickness(0,0,8,6), VerticalContentAlignment = Wpf.VerticalAlignment.Center };
        foreach (var label in new[] { "25%","50%","75%","100%","150%","200%" }) zoomBox.Items.Add(label);
        zoomBox.SelectedIndex = 3;
        zoomBox.SelectionChanged += (_,_) => { zoom = new double[] { .25,.5,.75,1,1.5,2 }[zoomBox.SelectedIndex]; Canvas.LayoutTransform = new Media.ScaleTransform(zoom,zoom); };
        navigation.Children.Add(zoomBox); navigation.Children.Add(status);
        root.Children.Add(scroll); scroll.Content = Canvas; Canvas.Changed += OnChanged;
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
        if (chooser.ShowDialog() == System.Windows.Forms.DialogResult.OK) Canvas.Argb = unchecked((uint)chooser.Color.ToArgb());
    }
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
        status.Text = index >= 0 ? $"{store.Records[index].CapturedAt.LocalDateTime:g} · retained" : "Outside recent history — save or copy";
        undo.IsEnabled = Canvas.IsEditing || Canvas.Document.UndoStack.Count > 0; redo.IsEnabled = Canvas.Document.RedoStack.Count > 0;
    }
    public void LoadCapture(Guid id)
    {
        if (!Flush()) return;
        var draft = store.Get(id); var image = AnnotationCanvas.Read(store.Original(id));
        var canvas = new AnnotationCanvas(image,new AnnotationDocument(draft)) { Tool = Canvas.Tool, Argb = Canvas.Argb, StrokeWidth = Canvas.StrokeWidth, LayoutTransform = new Media.ScaleTransform(zoom,zoom) };
        Canvas.Changed -= OnChanged; Canvas = canvas; CaptureId = id; exported = false; Canvas.Changed += OnChanged;
        scroll.Content = Canvas; scroll.ScrollToTop(); scroll.ScrollToLeftEnd(); Canvas.Focus(); UpdateNavigation();
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
