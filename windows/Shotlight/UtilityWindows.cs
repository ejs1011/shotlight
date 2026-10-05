using Shotlight.Core;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;
using Input = System.Windows.Input;

namespace Shotlight;

internal sealed class HistoryWindow : Wpf.Window
{
    private readonly CaptureStore store;
    private readonly Action<Guid> open;
    private readonly Controls.StackPanel list = new();
    private readonly Controls.TextBlock summary = new() { Margin = new Wpf.Thickness(16,14,16,12), Foreground = Media.Brushes.DimGray };
    public HistoryWindow(CaptureStore store, Action<Guid> open)
    {
        this.store = store; this.open = open; Title = "Shotlight — Recent Captures"; Width = 680; Height = 650; MinWidth = 460; MinHeight = 300;
        FontFamily = new Media.FontFamily("Segoe UI"); FontSize = 14; WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen;
        var root = new Controls.DockPanel(); Controls.DockPanel.SetDock(summary,Controls.Dock.Top); root.Children.Add(summary);
        root.Children.Add(new Controls.ScrollViewer { Content = list, VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto }); Content = root;
        Activated += (_,_) => Reload(); Reload();
    }
    public void Reload()
    {
        summary.Text = $"{store.Records.Count} of {store.Limit} recent captures · retained automatically"; list.Children.Clear();
        if (store.Records.Count == 0) list.Children.Add(new Controls.TextBlock { Text = "No captures yet. Your next screenshot will appear here automatically.", TextWrapping = Wpf.TextWrapping.Wrap, Margin = new Wpf.Thickness(24) });
        foreach (var draft in store.Records)
        {
            var row = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
            try { row.Children.Add(new Controls.Image { Source = AnnotationCanvas.Read(store.Thumbnail(draft.Id)), Width = 120, Height = 75, Stretch = Media.Stretch.Uniform, Margin = new Wpf.Thickness(0,0,14,0) }); }
            catch (Exception error) when (error is IOException or NotSupportedException or System.IO.FileFormatException) { /* Keep the recoverable row even if its thumbnail is unreadable. */ }
            row.Children.Add(new Controls.TextBlock { Text = $"{draft.CapturedAt.LocalDateTime:f}\n{draft.Width} × {draft.Height} px", VerticalAlignment = Wpf.VerticalAlignment.Center });
            var button = Ui.Button("",() => open(draft.Id)); button.Content = row; button.HorizontalContentAlignment = Wpf.HorizontalAlignment.Left;
            button.Margin = new Wpf.Thickness(12,0,12,8); Wpf.Automation.AutomationProperties.SetName(button,$"Capture {draft.CapturedAt.LocalDateTime:f}"); list.Children.Add(button);
        }
    }
}

internal sealed class SettingsWindow : Wpf.Window
{
    private CaptureShortcut draft;
    private bool recording;
    private readonly Controls.Button recorder;
    private readonly Controls.TextBlock message = new() { Text = "Click the shortcut, then press your preferred combination. Include Ctrl, Alt, or Win.", TextWrapping = Wpf.TextWrapping.Wrap, Margin = new Wpf.Thickness(0,12,0,12), Foreground = Media.Brushes.DimGray };
    private readonly Controls.TextBox limit;
    public SettingsWindow(UserSettings settings, Func<UserSettings,string?> apply, Action clear)
    {
        draft = settings.Shortcut; Title = "Shotlight Settings"; Width = 510; Height = 365; ResizeMode = Wpf.ResizeMode.NoResize;
        SizeToContent = Wpf.SizeToContent.Height;
        FontFamily = new Media.FontFamily("Segoe UI"); FontSize = 14; WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen;
        var root = new Controls.StackPanel { Margin = new Wpf.Thickness(24) }; Content = root;
        root.Children.Add(new Controls.TextBlock { Text = "Capture shortcut", FontWeight = Wpf.FontWeights.SemiBold, Margin = new Wpf.Thickness(0,0,0,10) });
        recorder = Ui.Button(draft.Display,BeginRecording);
        recorder.PreviewKeyDown += Record; root.Children.Add(recorder); root.Children.Add(message);
        var retention = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal, Margin = new Wpf.Thickness(0,0,0,16) };
        retention.Children.Add(new Controls.TextBlock { Text = "Recent captures to keep (1–500)", VerticalAlignment = Wpf.VerticalAlignment.Center });
        limit = new Controls.TextBox { Text = settings.HistoryLimit.ToString(), Width = 66, Margin = new Wpf.Thickness(12,0,0,0), Padding = new Wpf.Thickness(4) };
        Wpf.Automation.AutomationProperties.SetName(limit,"History limit"); retention.Children.Add(limit); root.Children.Add(retention);
        root.Children.Add(Ui.Button("Clear History…",clear));
        var actions = new Controls.WrapPanel { Margin = new Wpf.Thickness(0,12,0,0) }; root.Children.Add(actions);
        actions.Children.Add(Ui.Button("Restore Default",() => { recording = false; draft = CaptureShortcut.Default; recorder.Content = draft.Display; }));
        actions.Children.Add(Ui.Button("Cancel",Close)); actions.Children.Add(Ui.Button("Save",() =>
        {
            recording = false; recorder.Content = draft.Display;
            if (!int.TryParse(limit.Text,out int count) || count is < 1 or > 500) { Error("Enter a history limit from 1 to 500."); return; }
            string? error = apply(new UserSettings { Shortcut = draft, HistoryLimit = count });
            if (error is not null) Error(error); else Close();
        }));
    }
    private void Error(string text) { message.Text = text; message.Foreground = Media.Brushes.Firebrick; }
    private void BeginRecording() { recording = true; recorder.Content = "Press a shortcut…"; recorder.Focus(); }
    private void Record(object sender, Input.KeyEventArgs e)
    {
        if (!recording) return; e.Handled = true;
        var key = e.Key == Input.Key.System ? e.SystemKey : e.Key;
        var flags = Input.Keyboard.Modifiers;
        if (key == Input.Key.Escape && flags == Input.ModifierKeys.None) { recording = false; recorder.Content = draft.Display; return; }
        if (key is Input.Key.LeftCtrl or Input.Key.RightCtrl or Input.Key.LeftAlt or Input.Key.RightAlt or Input.Key.LeftShift or Input.Key.RightShift or Input.Key.LWin or Input.Key.RWin) return;
        uint modifiers = (flags.HasFlag(Input.ModifierKeys.Alt) ? 1u : 0) | (flags.HasFlag(Input.ModifierKeys.Control) ? 2u : 0) | (flags.HasFlag(Input.ModifierKeys.Shift) ? 4u : 0) | (flags.HasFlag(Input.ModifierKeys.Windows) ? 8u : 0);
        string label = key.ToString(); if (label.Length == 2 && label[0] == 'D' && char.IsDigit(label[1])) label = label[1..];
        var candidate = new CaptureShortcut((uint)Input.KeyInterop.VirtualKeyFromKey(key),modifiers,label);
        if (!candidate.Valid) { Error("Include Ctrl, Alt, or Win. Ctrl+C is reserved for copying; F12 is reserved by Windows."); return; }
        draft = candidate; recorder.Content = draft.Display; recording = false; message.Foreground = Media.Brushes.DimGray; message.Text = "Save to apply this shortcut.";
    }
}
