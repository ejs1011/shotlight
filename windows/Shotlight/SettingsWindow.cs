using Shotlight.Core;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;
using Input = System.Windows.Input;

namespace Shotlight;

internal enum SettingField { Shortcut, History, General }
internal sealed record SettingsFailure(SettingField Field, string Message);

internal sealed class SettingsWindow : Wpf.Window
{
    private readonly UserSettings initial;
    private readonly Func<UserSettings,SettingsFailure?> apply;
    private readonly Func<int> captureCount;
    private CaptureShortcut draft;
    private bool recording;
    private readonly Controls.Button recorder;
    private readonly Controls.TextBlock message = Note("Click the shortcut, then press your preferred combination. Include Ctrl, Alt, or Win.");
    private readonly Controls.TextBlock shortcutError = ErrorLabel(), historyError = ErrorLabel(), applicationError = ErrorLabel(), retentionWarning = Note("");
    private readonly Controls.TextBox limit;
    private readonly Controls.CheckBox closeOnCopy = new() { Content = "Close editor after copying", Margin = new Wpf.Thickness(0,14,0,0) };
    private readonly Controls.StackPanel body = new() { Margin = new Wpf.Thickness(26,26,26,0) };
    private readonly Controls.ScrollViewer scroller = new() { VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Disabled };
    internal Func<int,int,bool>? ConfirmReduction { get; set; }
    internal Controls.TextBox LimitField => limit;
    internal Controls.TextBlock HistoryError => historyError;
    internal Controls.TextBlock ShortcutError => shortcutError;
    internal Controls.CheckBox CloseOnCopy => closeOnCopy;
    internal Controls.TextBlock RetentionWarning => retentionWarning;
    internal Controls.ScrollViewer BodyScroll => scroller;

    public SettingsWindow(UserSettings settings, Func<UserSettings,SettingsFailure?> apply, Action clear, Func<int>? captureCount = null)
    {
        initial = settings; draft = settings.Shortcut; this.apply = apply; this.captureCount = captureCount ?? (() => 0);
        Title = "Shotlight — Settings"; Width = 550; Height = Math.Min(760,Wpf.SystemParameters.WorkArea.Height-40); ResizeMode = Wpf.ResizeMode.NoResize; Ui.Apply(this);
        var root = new Controls.DockPanel(); Content = root;
        var footer = new Controls.StackPanel { Margin = new Wpf.Thickness(26,16,26,20) };
        footer.Children.Add(applicationError);
        var actions = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal, HorizontalAlignment = Wpf.HorizontalAlignment.Right };
        var cancel = Ui.Button("Cancel",Close); cancel.IsCancel = true; actions.Children.Add(cancel);
        var save = Ui.Button("Save changes",SaveChanges,"Check","PrimaryButton"); save.IsDefault = true; actions.Children.Add(save); footer.Children.Add(actions);
        Controls.DockPanel.SetDock(footer,Controls.Dock.Bottom); root.Children.Add(footer);
        scroller.Content = body; root.Children.Add(scroller);
        body.Children.Add(Ui.Label("Settings",26,bold:true));
        var shortcut = new Controls.StackPanel(); shortcut.Children.Add(Ui.Label("Capture shortcut",15,bold:true));
        recorder = Ui.Button(draft.Display,BeginRecording,"Capture"); recorder.Margin = new Wpf.Thickness(0,14,0,0); recorder.PreviewKeyDown += Record;
        shortcut.Children.Add(recorder); shortcut.Children.Add(message); shortcut.Children.Add(shortcutError);
        var restore = Ui.Button("Restore default",() => { recording = false; draft = CaptureShortcut.Default; RecorderLabel(); Hide(shortcutError); },style:"QuietButton");
        restore.HorizontalAlignment = Wpf.HorizontalAlignment.Left; shortcut.Children.Add(restore); AddCard(shortcut);
        var copy = new Controls.StackPanel(); copy.Children.Add(Ui.Label("Copying",15,bold:true));
        closeOnCopy.IsChecked = settings.CloseEditorAfterCopy; copy.Children.Add(closeOnCopy);
        copy.Children.Add(Note("Applies to the Copy button and Ctrl+C. Your editable capture stays in Recent Captures either way.")); AddCard(copy);
        var history = new Controls.StackPanel(); history.Children.Add(Ui.Label("Recent captures",15,bold:true));
        history.Children.Add(Note("Keep editable screenshots for later. Older drafts are removed when this limit is reached."));
        var retention = new Controls.DockPanel();
        limit = new Controls.TextBox { Text = settings.HistoryLimit.ToString(), Width = 78, HorizontalContentAlignment = Wpf.HorizontalAlignment.Center };
        limit.SetResourceReference(Wpf.FrameworkElement.StyleProperty,"SettingsField"); Wpf.Automation.AutomationProperties.SetName(limit,"Captures to keep, from 1 to 500");
        Controls.DockPanel.SetDock(limit,Controls.Dock.Right); retention.Children.Add(limit); retention.Children.Add(Ui.Label("Captures to keep · 1–500")); history.Children.Add(retention);
        history.Children.Add(historyError); history.Children.Add(retentionWarning);
        limit.TextChanged += (_,_) => { Hide(historyError); UpdateWarning(); };
        var clearButton = Ui.Button("Clear history…",() => { clear(); UpdateWarning(); },"Trash","QuietButton");
        clearButton.Foreground = Ui.Brush("#B6414C"); clearButton.HorizontalAlignment = Wpf.HorizontalAlignment.Left; clearButton.Margin = new Wpf.Thickness(0,14,0,0); history.Children.Add(clearButton); AddCard(history);
        UpdateWarning();
    }
    private void AddCard(Wpf.UIElement content) { var card = Ui.Card(content); card.Margin = new Wpf.Thickness(0,16,0,0); body.Children.Add(card); }
    private static Controls.TextBlock Note(string text) => new() { Text = text, TextWrapping = Wpf.TextWrapping.Wrap, Margin = new Wpf.Thickness(0,10,0,12), Foreground = Ui.Muted, FontSize = 12 };
    private static Controls.TextBlock ErrorLabel() => new() { TextWrapping = Wpf.TextWrapping.Wrap, Margin = new Wpf.Thickness(0,8,0,8), Foreground = Media.Brushes.Firebrick, FontSize = 12, Visibility = Wpf.Visibility.Collapsed };
    private static void Hide(Controls.TextBlock label) { label.Text = ""; label.Visibility = Wpf.Visibility.Collapsed; }
    private void UpdateWarning()
    {
        int removing = int.TryParse(limit.Text,out int count) && count is >= 1 and <= 500 ? Math.Max(0,captureCount()-count) : 0;
        retentionWarning.Text = removing > 0 ? $"Keeping {count} captures will remove {removing} older {(removing == 1 ? "draft" : "drafts")} when you save." : "";
        retentionWarning.Visibility = removing > 0 ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
    }
    internal void SaveChanges()
    {
        recording = false; RecorderLabel(); Hide(shortcutError); Hide(historyError); Hide(applicationError);
        if (!int.TryParse(limit.Text,out int count) || count is < 1 or > 500) { ShowFailure(new(SettingField.History,"Enter a history limit from 1 to 500.")); return; }
        int removing = Math.Max(0,captureCount()-count);
        if (removing > 0 && !(ConfirmReduction?.Invoke(count,removing) ?? ConfirmRemoving(count,removing))) return;
        var candidate = initial with { Shortcut = draft, HistoryLimit = count, CloseEditorAfterCopy = closeOnCopy.IsChecked == true };
        var failure = apply(candidate); if (failure is not null) ShowFailure(failure); else Close();
    }
    private bool ConfirmRemoving(int limitCount, int removing) => Wpf.MessageBox.Show(this,
        $"Keeping {limitCount} captures removes {removing} older {(removing == 1 ? "draft" : "drafts")} from Recent Captures. Exported PNGs are unaffected. Continue?",
        "Remove older drafts?",Wpf.MessageBoxButton.YesNo,Wpf.MessageBoxImage.Question,Wpf.MessageBoxResult.No) == Wpf.MessageBoxResult.Yes;
    private void ShowFailure(SettingsFailure failure)
    {
        var label = failure.Field switch { SettingField.Shortcut => shortcutError, SettingField.History => historyError, _ => applicationError };
        label.Text = failure.Message; label.Visibility = Wpf.Visibility.Visible; UpdateLayout();
        if (failure.Field == SettingField.General) return;
        Wpf.FrameworkElement field = failure.Field == SettingField.Shortcut ? recorder : limit;
        field.Focus(); if (field == limit) limit.SelectAll();
        // TextBox focus schedules its own caret scroll. Reveal the whole error
        // after that request, so the field cannot hide the message below it.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(() =>
        {
            if (!IsLoaded || !label.IsVisible) return;
            UpdateLayout();
            var bounds = field.TransformToAncestor(scroller).TransformBounds(new Wpf.Rect(field.RenderSize));
            bounds.Union(label.TransformToAncestor(scroller).TransformBounds(new Wpf.Rect(label.RenderSize)));
            if (bounds.Bottom > scroller.ViewportHeight-12) scroller.ScrollToVerticalOffset(scroller.VerticalOffset+bounds.Bottom-scroller.ViewportHeight+12);
            else if (bounds.Top < 12) scroller.ScrollToVerticalOffset(scroller.VerticalOffset+bounds.Top-12);
        }));
    }
    private void RecorderLabel() => recorder.Content = Ui.ButtonContent(draft.Display,"Capture");
    private void BeginRecording() { recording = true; recorder.Content = "Press a shortcut…"; recorder.Focus(); }
    private void Record(object sender, Input.KeyEventArgs e)
    {
        if (!recording) return; e.Handled = true;
        var key = e.Key == Input.Key.System ? e.SystemKey : e.Key; var flags = Input.Keyboard.Modifiers;
        if (key == Input.Key.Escape && flags == Input.ModifierKeys.None) { recording = false; RecorderLabel(); return; }
        if (key is Input.Key.LeftCtrl or Input.Key.RightCtrl or Input.Key.LeftAlt or Input.Key.RightAlt or Input.Key.LeftShift or Input.Key.RightShift or Input.Key.LWin or Input.Key.RWin) return;
        uint modifiers = (flags.HasFlag(Input.ModifierKeys.Alt) ? 1u : 0) | (flags.HasFlag(Input.ModifierKeys.Control) ? 2u : 0) | (flags.HasFlag(Input.ModifierKeys.Shift) ? 4u : 0) | (flags.HasFlag(Input.ModifierKeys.Windows) ? 8u : 0);
        string name = key.ToString(); if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name[1..];
        var candidate = new CaptureShortcut((uint)Input.KeyInterop.VirtualKeyFromKey(key),modifiers,name);
        if (!candidate.Valid) { ShowFailure(new(SettingField.Shortcut,"Include Ctrl, Alt, or Win. Ctrl+C is reserved for copying; F12 is reserved by Windows.")); return; }
        draft = candidate; recording = false; RecorderLabel(); Hide(shortcutError);
    }
}
