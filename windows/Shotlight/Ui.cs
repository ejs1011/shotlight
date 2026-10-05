using Wpf = System.Windows;
using Controls = System.Windows.Controls;
using Media = System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace Shotlight;

internal static class Ui
{
    public static Media.SolidColorBrush Brush(string hex) => new((Media.Color)Media.ColorConverter.ConvertFromString(hex));
    public static readonly Media.Brush Ink = Brush("#202637"), Muted = Brush("#687386"), Accent = Brush("#5265E9"), Line = Brush("#E1E5ED");
    public static void Apply(Wpf.Window window)
    {
        window.Resources.MergedDictionaries.Add(new Wpf.ResourceDictionary { Source = new Uri("/Shotlight;component/Theme.xaml",UriKind.Relative) });
        window.FontFamily = new Media.FontFamily("Segoe UI Variable Text, Segoe UI"); window.FontSize = 13;
        window.Background = Brush("#F5F6FA"); window.Foreground = Ink;
        window.WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen;
        window.UseLayoutRounding = true; Media.TextOptions.SetTextFormattingMode(window,Media.TextFormattingMode.Display);
    }
    public static Controls.TextBlock Label(string text, double size = 13, Media.Brush? color = null, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Ink, FontWeight = bold ? Wpf.FontWeights.SemiBold : Wpf.FontWeights.Normal,
        VerticalAlignment = Wpf.VerticalAlignment.Center
    };
    public static Shapes.Path Icon(string name, double size = 17)
    {
        string path = name switch
        {
            "Capture" => "M 3,7 L 3,3 7,3 M 13,3 L 17,3 17,7 M 17,13 L 17,17 13,17 M 7,17 L 3,17 3,13 M 7,10 L 13,10 M 10,7 L 10,13",
            "Pen" => "M 3,17 L 4,12 13,3 17,7 8,16 Z M 11,5 L 15,9",
            "Arrow" => "M 3,17 L 17,3 M 7,3 L 17,3 17,13",
            "Rectangle" => "M 3,4 L 17,4 17,16 3,16 Z",
            "Text" => "M 3,5 L 3,3 17,3 17,5 M 10,3 L 10,17 M 6,17 L 14,17",
            "Copy" => "M 7,6 L 17,6 17,17 7,17 Z M 4,13 L 3,13 3,3 13,3 13,4",
            "Save" => "M 4,3 L 14,3 17,6 17,17 3,17 3,3 Z M 7,3 L 7,8 13,8 13,3 M 7,17 L 7,12 13,12 13,17",
            "Undo" => "M 7,3 L 2,8 7,13 M 2,8 L 11,8 C 18,8 18,17 11,17",
            "Redo" => "M 13,3 L 18,8 13,13 M 18,8 L 9,8 C 2,8 2,17 9,17",
            "Previous" => "M 12,4 L 6,10 12,16",
            "Next" => "M 8,4 L 14,10 8,16",
            "History" => "M 3,8 A 7,7 0 1 1 3,13 M 3,3 L 3,8 8,8 M 10,6 L 10,10 13,12",
            "Settings" => "M 3,5 L 17,5 M 3,10 L 17,10 M 3,15 L 17,15 M 7,2 L 7,8 M 13,7 L 13,13 M 8,12 L 8,18",
            "Check" => "M 4,10 L 8,14 16,6",
            "More" => "M 3,10 L 3.1,10 M 10,10 L 10.1,10 M 17,10 L 17.1,10",
            "Weight" => "M 3,4 L 17,4 M 3,10 L 17,10 M 3,16 L 17,16 M 3,17 L 17,17",
            "Zoom" => "M 13,13 L 18,18 M 15,8 A 7,7 0 1 1 1,8 A 7,7 0 1 1 15,8 M 5,8 L 11,8 M 8,5 L 8,11",
            "Trash" => "M 3,5 L 17,5 M 7,5 L 7,2 13,2 13,5 M 5,5 L 6,18 14,18 15,5 M 8,8 L 8,15 M 12,8 L 12,15",
            _ => "M 3,10 L 17,10"
        };
        var icon = new Shapes.Path { Data = Media.Geometry.Parse(path), Width = size, Height = size, Stretch = Media.Stretch.Uniform,
            StrokeThickness = 1.6, StrokeStartLineCap = Media.PenLineCap.Round, StrokeEndLineCap = Media.PenLineCap.Round, StrokeLineJoin = Media.PenLineJoin.Round };
        icon.SetBinding(Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(Controls.Control),1) });
        return icon;
    }
    public static Controls.StackPanel ButtonContent(string text, string? icon = null)
    {
        var row = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
        if (icon is not null) { var shape = Icon(icon); shape.Margin = new Wpf.Thickness(0,0,text.Length > 0 ? 8 : 0,0); row.Children.Add(shape); }
        if (text.Length > 0) row.Children.Add(new Controls.TextBlock { Text = text, VerticalAlignment = Wpf.VerticalAlignment.Center });
        return row;
    }
    public static Controls.Button Button(string text, Action click, string? icon = null, string? style = null, string? hint = null)
    {
        var button = new Controls.Button { Content = ButtonContent(text,icon), Margin = new Wpf.Thickness(0,0,6,0), ToolTip = hint ?? text };
        if (style is not null) button.SetResourceReference(Wpf.FrameworkElement.StyleProperty,style);
        Wpf.Automation.AutomationProperties.SetName(button,text.Length > 0 ? text : hint ?? icon ?? "Button");
        button.Click += (_,_) => click(); return button;
    }
    public static Controls.Border Card(Wpf.UIElement content, Wpf.Thickness? padding = null) => new()
    { Child = content, Background = Media.Brushes.White, BorderBrush = Line, BorderThickness = new Wpf.Thickness(1), CornerRadius = new Wpf.CornerRadius(12), Padding = padding ?? new Wpf.Thickness(20) };
    public static Controls.Border Divider() => new() { Width = 1, Height = 22, Background = Line, Margin = new Wpf.Thickness(5,0,7,0) };
    public static Controls.ContextMenu Menu()
    {
        var menu = new Controls.ContextMenu { Placement = Controls.Primitives.PlacementMode.Top, VerticalOffset = -8 };
        menu.Resources.MergedDictionaries.Add(new Wpf.ResourceDictionary { Source = new Uri("/Shotlight;component/Theme.xaml",UriKind.Relative) });
        return menu;
    }
    public static Controls.MenuItem MenuItem(string text, Action click, string? icon = null)
    {
        var item = new Controls.MenuItem { Header = text }; if (icon is not null) item.Icon = Icon(icon);
        item.Click += (_,_) => click(); return item;
    }
    public static void AttachMenu(Controls.Button button, Controls.ContextMenu menu)
    {
        button.ContextMenu = menu;
        button.Click += (_,_) => { menu.PlacementTarget = button; menu.IsOpen = true; };
    }
    public static void Error(string title, Exception error) => Wpf.MessageBox.Show(error.Message,title,Wpf.MessageBoxButton.OK,Wpf.MessageBoxImage.Error);
}
