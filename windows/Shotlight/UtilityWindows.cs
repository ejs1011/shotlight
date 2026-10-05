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
    private readonly Controls.WrapPanel list = new() { Margin = new Wpf.Thickness(16,0,8,20) };
    private readonly Controls.TextBlock summary = Ui.Label("",13,Ui.Muted);
    public HistoryWindow(CaptureStore store, Action<Guid> open)
    {
        this.store = store; this.open = open; Title = "Shotlight — Recent Captures"; Width = 840; Height = 680; MinWidth = 580; MinHeight = 320;
        Ui.Apply(this);
        var root = new Controls.DockPanel();
        var heading = new Controls.StackPanel { Margin = new Wpf.Thickness(26,24,26,22) };
        heading.Children.Add(Ui.Label("Recent captures",26,bold:true)); summary.Margin = new Wpf.Thickness(0,7,0,0); heading.Children.Add(summary);
        Controls.DockPanel.SetDock(heading,Controls.Dock.Top); root.Children.Add(heading);
        var note = Ui.Label("Pick up where you left off. Your annotations stay editable.",12,Ui.Muted); note.Margin = new Wpf.Thickness(26,12,26,18);
        Controls.DockPanel.SetDock(note,Controls.Dock.Bottom); root.Children.Add(note);
        root.Children.Add(new Controls.ScrollViewer { Content = list, HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto }); Content = root;
        Activated += (_,_) => Reload(); Reload();
    }
    public void Reload()
    {
        summary.Text = $"{store.Records.Count} captures · Automatically kept on this computer · Limit {store.Limit}"; list.Children.Clear();
        if (store.Records.Count == 0)
        {
            var empty = new Controls.StackPanel { Margin = new Wpf.Thickness(24,55,24,55) };
            var icon = Ui.Icon("History",40); icon.Stroke = Ui.Accent; icon.Margin = new Wpf.Thickness(0,0,0,20); empty.Children.Add(icon);
            empty.Children.Add(new Controls.TextBlock { Text = "Your next capture starts here", FontSize = 21, FontWeight = Wpf.FontWeights.SemiBold, HorizontalAlignment = Wpf.HorizontalAlignment.Center });
            empty.Children.Add(new Controls.TextBlock { Text = "Take a screenshot and it will be kept here automatically.", Foreground = Ui.Muted, Margin = new Wpf.Thickness(0,10,0,0), TextWrapping = Wpf.TextWrapping.Wrap, TextAlignment = Wpf.TextAlignment.Center });
            list.Children.Add(empty);
        }
        foreach (var draft in store.Records)
        {
            var column = new Controls.StackPanel { Width = 224 };
            var preview = new Controls.Border { Height = 132, Background = Ui.Brush("#EDF0F6"), CornerRadius = new Wpf.CornerRadius(7), Padding = new Wpf.Thickness(7), Margin = new Wpf.Thickness(0,0,0,13) };
            try { preview.Child = new Controls.Image { Source = AnnotationCanvas.Read(store.Thumbnail(draft.Id)), Stretch = Media.Stretch.Uniform }; }
            catch (Exception error) when (error is IOException or NotSupportedException or System.IO.FileFormatException) { preview.Child = Ui.Label("Preview unavailable",12,Ui.Muted); }
            column.Children.Add(preview);
            column.Children.Add(Ui.Label(draft.CapturedAt.LocalDateTime.ToString("MMM d · h:mm tt"),14,bold:true));
            var detail = Ui.Label($"{draft.Width} × {draft.Height} px  ·  {draft.Marks.Count} annotations",12,Ui.Muted); detail.Margin = new Wpf.Thickness(0,6,0,2); column.Children.Add(detail);
            var button = Ui.Button("",() => open(draft.Id)); button.Content = column; button.HorizontalContentAlignment = Wpf.HorizontalAlignment.Left;
            button.Padding = new Wpf.Thickness(12); button.Margin = new Wpf.Thickness(8,0,4,14);
            button.ToolTip = $"Open capture from {draft.CapturedAt.LocalDateTime:f}";
            Wpf.Automation.AutomationProperties.SetName(button,$"Capture {draft.CapturedAt.LocalDateTime:f}"); list.Children.Add(button);
        }
    }
}

internal sealed class SettingsWindow : Wpf.Window
{
    private CaptureShortcut draft;
    private bool recording;
    private readonly Controls.Button recorder;
    private readonly Controls.TextBlock message = new() { Text = "Click the shortcut, then press your preferred combination. Include Ctrl, Alt, or Win.", TextWrapping = Wpf.TextWrapping.Wrap, Margin = new Wpf.Thickness(0,12,0,12), Foreground = Ui.Muted };
    private readonly Controls.TextBox limit;
    public SettingsWindow(UserSettings settings, Func<UserSettings,string?> apply, Action clear)
    {
        draft = settings.Shortcut; Title = "Shotlight — Settings"; Width = 550; ResizeMode = Wpf.ResizeMode.NoResize;
        SizeToContent = Wpf.SizeToContent.Height; MaxHeight = Math.Max(360,Wpf.SystemParameters.WorkArea.Height-40); Ui.Apply(this);
        var root = new Controls.StackPanel { Margin = new Wpf.Thickness(26) }; Content = new Controls.ScrollViewer { Content = root, VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Disabled };
        root.Children.Add(Ui.Label("Make it yours",26,bold:true));
        var intro = Ui.Label("Your shortcuts. Your captures. All local.",13,Ui.Muted); intro.Margin = new Wpf.Thickness(0,6,0,24); root.Children.Add(intro);
        var shortcut = new Controls.StackPanel(); shortcut.Children.Add(Ui.Label("Capture shortcut",15,bold:true));
        recorder = Ui.Button(draft.Display,BeginRecording,"Capture"); recorder.Margin = new Wpf.Thickness(0,14,0,0);
        recorder.PreviewKeyDown += Record; shortcut.Children.Add(recorder); shortcut.Children.Add(message);
        var restore = Ui.Button("Restore default",() => { recording = false; draft = CaptureShortcut.Default; recorder.Content = Ui.ButtonContent(draft.Display,"Capture"); },style:"QuietButton");
        restore.HorizontalAlignment = Wpf.HorizontalAlignment.Left; shortcut.Children.Add(restore);
        root.Children.Add(Ui.Card(shortcut));
        var history = new Controls.StackPanel(); history.Children.Add(Ui.Label("Recent captures",15,bold:true));
        var explanation = new Controls.TextBlock { Text = "Keep editable screenshots so you can come back to them later.", Foreground = Ui.Muted, TextWrapping = Wpf.TextWrapping.Wrap, Margin = new Wpf.Thickness(0,7,0,16) }; history.Children.Add(explanation);
        var retention = new Controls.DockPanel();
        limit = new Controls.TextBox { Text = settings.HistoryLimit.ToString(), Width = 78, HorizontalContentAlignment = Wpf.HorizontalAlignment.Center };
        limit.SetResourceReference(Wpf.FrameworkElement.StyleProperty,"SettingsField"); Wpf.Automation.AutomationProperties.SetName(limit,"History limit");
        Controls.DockPanel.SetDock(limit,Controls.Dock.Right); retention.Children.Add(limit); retention.Children.Add(Ui.Label("Captures to keep · 1–500")); history.Children.Add(retention);
        var clearButton = Ui.Button("Clear history…",clear,"Trash","QuietButton"); clearButton.Foreground = Ui.Brush("#B6414C"); clearButton.HorizontalAlignment = Wpf.HorizontalAlignment.Left; clearButton.Margin = new Wpf.Thickness(0,14,0,0); history.Children.Add(clearButton);
        var historyCard = Ui.Card(history); historyCard.Margin = new Wpf.Thickness(0,14,0,0); root.Children.Add(historyCard);
        var actions = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal, HorizontalAlignment = Wpf.HorizontalAlignment.Right, Margin = new Wpf.Thickness(0,22,0,0) }; root.Children.Add(actions);
        actions.Children.Add(Ui.Button("Cancel",Close)); actions.Children.Add(Ui.Button("Save changes",() =>
        {
            recording = false; recorder.Content = Ui.ButtonContent(draft.Display,"Capture");
            if (!int.TryParse(limit.Text,out int count) || count is < 1 or > 500) { Error("Enter a history limit from 1 to 500."); return; }
            string? error = apply(new UserSettings { Shortcut = draft, HistoryLimit = count });
            if (error is not null) Error(error); else Close();
        },"Check","PrimaryButton"));
    }

    private void Error(string text) { message.Text = text; message.Foreground = Media.Brushes.Firebrick; }
    private void BeginRecording() { recording = true; recorder.Content = "Press a shortcut…"; recorder.Focus(); }
    private void Record(object sender, Input.KeyEventArgs e)
    {
        if (!recording) return; e.Handled = true;
        var key = e.Key == Input.Key.System ? e.SystemKey : e.Key;
        var flags = Input.Keyboard.Modifiers;
        if (key == Input.Key.Escape && flags == Input.ModifierKeys.None) { recording = false; recorder.Content = Ui.ButtonContent(draft.Display,"Capture"); return; }
        if (key is Input.Key.LeftCtrl or Input.Key.RightCtrl or Input.Key.LeftAlt or Input.Key.RightAlt or Input.Key.LeftShift or Input.Key.RightShift or Input.Key.LWin or Input.Key.RWin) return;
        uint modifiers = (flags.HasFlag(Input.ModifierKeys.Alt) ? 1u : 0) | (flags.HasFlag(Input.ModifierKeys.Control) ? 2u : 0) | (flags.HasFlag(Input.ModifierKeys.Shift) ? 4u : 0) | (flags.HasFlag(Input.ModifierKeys.Windows) ? 8u : 0);
        string label = key.ToString(); if (label.Length == 2 && label[0] == 'D' && char.IsDigit(label[1])) label = label[1..];
        var candidate = new CaptureShortcut((uint)Input.KeyInterop.VirtualKeyFromKey(key),modifiers,label);
        if (!candidate.Valid) { Error("Include Ctrl, Alt, or Win. Ctrl+C is reserved for copying; F12 is reserved by Windows."); return; }
        draft = candidate; recorder.Content = Ui.ButtonContent(draft.Display,"Capture"); recording = false; message.Foreground = Ui.Muted; message.Text = "Save to apply this shortcut.";
    }
}
