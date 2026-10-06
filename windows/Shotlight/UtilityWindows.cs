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
    private readonly Func<Guid,bool> delete;
    private readonly Dictionary<Guid,Controls.Button> openButtons = [];
    private readonly Controls.WrapPanel list = new() { Margin = new Wpf.Thickness(16,0,8,20) };
    private readonly Controls.TextBlock summary = Ui.Label("",13,Ui.Muted);
    public HistoryWindow(CaptureStore store, Action<Guid> open, Func<Guid,bool> delete)
    {
        this.store = store; this.open = open; this.delete = delete; Title = "Shotlight — Recent Captures"; Width = 840; Height = 680; MinWidth = 580; MinHeight = 320;
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
    private void DeleteCapture(Guid id)
    {
        int index = Math.Max(0,store.Records.FindIndex(record => record.Id == id));
        if (!delete(id)) return;
        Reload();
        if (store.Records.Count > 0) openButtons[store.Records[Math.Min(index,store.Records.Count-1)].Id].Focus();
        else Focus();
    }
    public void Reload()
    {
        summary.Text = $"{store.Records.Count} captures · Automatically kept on this computer · Limit {store.Limit}"; list.Children.Clear(); openButtons.Clear();
        if (store.Records.Count == 0)
        {
            var empty = new Controls.StackPanel { Margin = new Wpf.Thickness(24,55,24,55) };
            var icon = Ui.Icon("History",40); icon.Stroke = Ui.Accent; icon.Margin = new Wpf.Thickness(0,0,0,20); empty.Children.Add(icon);
            empty.Children.Add(new Controls.TextBlock { Text = "Your next capture starts here", FontSize = 21, FontWeight = Wpf.FontWeights.SemiBold, HorizontalAlignment = Wpf.HorizontalAlignment.Center });
            empty.Children.Add(new Controls.TextBlock { Text = "Take a screenshot and it will be kept here automatically.", Foreground = Ui.Muted, Margin = new Wpf.Thickness(0,10,0,0), TextWrapping = Wpf.TextWrapping.Wrap, TextAlignment = Wpf.TextAlignment.Center });
            list.Children.Add(empty);
        }
        foreach (var draft in store.Records.ToArray())
        {
            var column = new Controls.StackPanel { Width = 220 };
            var preview = new Controls.Border { Height = 132, Background = Ui.Brush("#EDF0F6"), CornerRadius = new Wpf.CornerRadius(7), Padding = new Wpf.Thickness(7), Margin = new Wpf.Thickness(0,0,0,13) };
            try
            {
                if (!store.ThumbnailIsCurrent(draft.Id)) store.UpdateThumbnail(draft.Id,AnnotationCanvas.ThumbnailPng(AnnotationCanvas.Read(store.Original(draft.Id)),draft.Marks));
                preview.Child = new Controls.Image { Source = AnnotationCanvas.Read(store.Thumbnail(draft.Id)), Stretch = Media.Stretch.Uniform };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or System.IO.FileFormatException) { preview.Child = Ui.Label("Preview unavailable",12,Ui.Muted); }
            column.Children.Add(preview);
            column.Children.Add(Ui.Label(draft.CapturedAt.LocalDateTime.ToString("MMM d · h:mm tt"),14,bold:true));
            var detail = Ui.Label($"{draft.Width} × {draft.Height} px  ·  {draft.Marks.Count} annotations",12,Ui.Muted); detail.Margin = new Wpf.Thickness(0,6,0,2); column.Children.Add(detail);
            var button = Ui.Button("",() => open(draft.Id),style:"QuietButton"); button.Content = column; button.HorizontalContentAlignment = Wpf.HorizontalAlignment.Left;
            button.Padding = new Wpf.Thickness(12); button.Margin = new Wpf.Thickness(0);
            button.ToolTip = $"Open capture from {draft.CapturedAt.LocalDateTime:f}";
            Wpf.Automation.AutomationProperties.SetName(button,$"Capture {draft.CapturedAt.LocalDateTime:f}"); openButtons.Add(draft.Id,button);
            var remove = Ui.Button("Delete",() => DeleteCapture(draft.Id),"Trash","QuietButton","Move this capture to the Recycle Bin");
            remove.Tag = draft.Id; remove.HorizontalAlignment = Wpf.HorizontalAlignment.Right;
            remove.MinHeight = 30; remove.Padding = new Wpf.Thickness(9,5,9,5); remove.Margin = new Wpf.Thickness(12,0,12,12);
            Wpf.Automation.AutomationProperties.SetName(remove,$"Delete capture {draft.CapturedAt.LocalDateTime:f}");
            var cardContent = new Controls.StackPanel(); cardContent.Children.Add(button); cardContent.Children.Add(remove);
            var card = Ui.Card(cardContent,new Wpf.Thickness(0)); card.Margin = new Wpf.Thickness(8,0,4,14); list.Children.Add(card);
        }
    }
}
