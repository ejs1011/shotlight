using Shotlight.Core;
using Wpf = System.Windows;
using Controls = System.Windows.Controls;

namespace Shotlight;

internal sealed class WelcomeWindow : Wpf.Window
{
    public WelcomeWindow(CaptureShortcut shortcut, Action capture)
    {
        Title = "Welcome to Shotlight"; Width = 480; SizeToContent = Wpf.SizeToContent.Height; ResizeMode = Wpf.ResizeMode.NoResize; Ui.Apply(this);
        var stack = new Controls.StackPanel { Margin = new Wpf.Thickness(26) }; Content = stack;
        stack.Children.Add(Ui.Label("Welcome to Shotlight",24,bold:true));
        stack.Children.Add(new Controls.TextBlock { Text = $"Capture an area with {shortcut.Display} or the camera icon in your system tray. Annotate it, then copy or save. Editable drafts stay on this computer.", TextWrapping = Wpf.TextWrapping.Wrap, Margin = new Wpf.Thickness(0,20,0,0) });
        stack.Children.Add(new Controls.TextBlock { Text = "Copy closes the editor by default; you can change that in Settings. Windows may place the camera icon under the tray’s hidden-icons arrow.", Foreground = Ui.Muted, TextWrapping = Wpf.TextWrapping.Wrap, Margin = new Wpf.Thickness(0,14,0,20), FontSize = 12 });
        var button = Ui.Button("Capture Area",() => { Close(); capture(); },"Capture","PrimaryButton"); button.HorizontalAlignment = Wpf.HorizontalAlignment.Left; button.IsDefault = true; stack.Children.Add(button);
    }
}
