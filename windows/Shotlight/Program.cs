using Shotlight.Core;
using Wpf = System.Windows;

namespace Shotlight;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        if (args.Contains("--run-checks")) return WindowsChecks.Run(args);
        using var singleInstance = new Mutex(true,"Local\\Shotlight.Personal.Windows",out bool first);
        if (!first) { Wpf.MessageBox.Show("Shotlight is already running. Look for its camera icon in the system tray.","Shotlight"); return 0; }
        var app = new Wpf.Application { ShutdownMode = Wpf.ShutdownMode.OnExplicitShutdown };
        try
        {
            using var controller = new AppController(app); app.Run(); return 0;
        }
        catch (Exception error) { Ui.Error("Shotlight could not start",error); return 1; }
    }
}

internal sealed class AppController : IDisposable
{
    private readonly Wpf.Application app;
    private readonly NotifyIcon? tray;
    private readonly HotkeyWindow? hotkey;
    private readonly ToolStripMenuItem? captureItem;
    private readonly CaptureStore store;
    private readonly string settingsPath;
    private UserSettings settings;
    private readonly List<EditorWindow> editors = [];
    private FrozenCapture? frozen;
    private bool capturing;
    private HistoryWindow? history;
    private SettingsWindow? settingsWindow;
    private bool disposed;
    public AppController(Wpf.Application app, string? testRoot = null)
    {
        this.app = app;
        string root = testRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Shotlight");
        settingsPath = Path.Combine(root,"settings.json");
        try { settings = UserSettings.Load(settingsPath); }
        catch (Exception error) { settings = new(); Ui.Error("Could not load settings — using defaults",error); }
        store = new CaptureStore(Path.Combine(root,"Captures"),settings.HistoryLimit); store.Changed += HistoryChanged;
        if (testRoot is not null) return;
        hotkey = new HotkeyWindow(); hotkey.Pressed += QueueCapture;
        var menu = new ContextMenuStrip(); captureItem = new ToolStripMenuItem("Capture Area  " + settings.Shortcut.Display,null,(_,_) => QueueCapture());
        menu.Items.Add(captureItem);
        menu.Items.Add("Recent Captures…",null,(_,_) => ShowHistory()); menu.Items.Add("Settings…",null,(_,_) => ShowSettings());
        menu.Items.Add("About Shotlight",null,(_,_) => Wpf.MessageBox.Show("Shotlight 1.0 for Windows\n\n" + settings.Shortcut.Display + " captures a frozen desktop. Drag to select an area; Escape cancels.\n\nAnnotate locally. Ctrl+C copies the screenshot and closes its editor. Ctrl+S saves a PNG. Recent Captures retains editable drafts automatically.","Shotlight"));
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Quit Shotlight",null,(_,_) => Quit());
        tray = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application, Text = "Shotlight", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_,_) => QueueCapture();
        if (!hotkey.Register(settings.Shortcut)) Wpf.MessageBox.Show("The capture shortcut is already in use. Open Shotlight Settings from the tray to choose another; Capture Area remains available in the menu.","Shortcut unavailable");
        tray.ShowBalloonTip(5000,"Shotlight is ready",$"Press {settings.Shortcut.Display}, or right-click this icon to capture.",ToolTipIcon.Info);
        if (store.RecoveryWarnings.Count > 0) Wpf.MessageBox.Show($"{store.RecoveryWarnings.Count} stored draft(s) could not be loaded. Their files have been left in {store.Root}.","Some history could not be loaded");
    }
    private void HistoryChanged() { history?.Reload(); foreach (var editor in editors.ToArray()) editor.UpdateNavigation(); }
    public void QueueCapture()
    {
        if (capturing) return; capturing = true;
        // Let the tray menu finish closing before snapshotting the desktop.
        app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(() =>
        {
            try
            {
                frozen = new FrozenCapture(image =>
                {
                    frozen?.Dispose(); frozen = null; capturing = false;
                    if (image is null) return;
                    using (image)
                    {
                        try { OpenNew(image); }
                        catch (Exception error) { Ui.Error("Could not retain screenshot",error); }
                    }
                });
                frozen.Start();
            }
            catch (Exception error) { frozen?.Dispose(); frozen = null; capturing = false; Ui.Error("Capture failed",error); }
        }));
    }
    internal EditorWindow OpenNew(Bitmap bitmap)
    {
        byte[] png = ImageFiles.Png(bitmap);
        try
        {
            var draft = store.Add(png,ImageFiles.Thumbnail(bitmap),bitmap.Width,bitmap.Height); return Open(draft.Id);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A storage failure must still leave the captured pixels available
            // for a manual save or copy instead of discarding the screenshot.
            var draft = new CaptureDraft { Id = Guid.NewGuid(), CapturedAt = DateTimeOffset.Now, Width = bitmap.Width, Height = bitmap.Height };
            var editor = new EditorWindow(this,store,draft.Id,png,draft); editors.Add(editor); editor.Show(); editor.Activate();
            Ui.Error("Screenshot not retained — save or copy before closing",error); return editor;
        }
    }
    private EditorWindow Open(Guid id)
    {
        var existing = editors.FirstOrDefault(e => e.CaptureId == id);
        if (existing is not null) { if (existing.WindowState == Wpf.WindowState.Minimized) existing.WindowState = Wpf.WindowState.Normal; existing.Activate(); return existing; }
        var editor = new EditorWindow(this,store,id); editors.Add(editor); editor.Show(); editor.Activate(); return editor;
    }
    public void EditorClosed(EditorWindow editor) => editors.Remove(editor);
    public void Navigate(EditorWindow editor, int direction)
    {
        try
        {
            if (!editor.Flush()) return;
            int index = store.Records.FindIndex(r => r.Id == editor.CaptureId), target = index+direction;
            if (index < 0 || target < 0 || target >= store.Records.Count) return;
            var id = store.Records[target].Id; var existing = editors.FirstOrDefault(e => e != editor && e.CaptureId == id);
            if (existing is not null) { if (existing.WindowState == Wpf.WindowState.Minimized) existing.WindowState = Wpf.WindowState.Normal; existing.Activate(); }
            else editor.LoadCapture(id);
        }
        catch (Exception error) { Ui.Error("Could not reopen screenshot",error); }
    }
    public void ShowHistory()
    {
        if (history is null)
        {
            history = new HistoryWindow(store,id => { try { Open(id); } catch (Exception error) { Ui.Error("Could not reopen screenshot",error); } });
            history.Closed += (_,_) => history = null; history.Show();
        }
        history.Reload(); if (history.WindowState == Wpf.WindowState.Minimized) history.WindowState = Wpf.WindowState.Normal; history.Activate();
    }
    public void ShowSettings()
    {
        if (settingsWindow is null)
        {
            settingsWindow = new SettingsWindow(settings,ApplySettings,ClearHistory); settingsWindow.Closed += (_,_) => settingsWindow = null; settingsWindow.Show();
        }
        settingsWindow.Activate();
    }
    private string? ApplySettings(UserSettings candidate)
    {
        if (editors.Any(e => !e.Flush())) return "A draft could not be retained. Save it before changing settings.";
        var previous = settings;
        bool changedShortcut = candidate.Shortcut.Key != previous.Shortcut.Key || candidate.Shortcut.Modifiers != previous.Shortcut.Modifiers;
        if (hotkey is not null && (changedShortcut || !hotkey.HasRegistration) && !hotkey.Register(candidate.Shortcut)) return "That shortcut is already in use. Choose another combination; any registered shortcut remains active.";
        try
        {
            candidate.Save(settingsPath); store.SetLimit(candidate.HistoryLimit); settings = candidate;
            if (captureItem is not null) captureItem.Text = "Capture Area  " + settings.Shortcut.Display; return null;
        }
        catch (Exception error)
        {
            if (changedShortcut) hotkey?.Register(previous.Shortcut);
            try { previous.Save(settingsPath); } catch { /* Preserve the main error in the settings UI. */ }
            return error.Message;
        }
    }
    private void ClearHistory()
    {
        if (Wpf.MessageBox.Show(settingsWindow,"Retained drafts will move to the Recycle Bin and open screenshot editors will close. Clear screenshot history?","Clear History",Wpf.MessageBoxButton.YesNo,Wpf.MessageBoxImage.Question) != Wpf.MessageBoxResult.Yes) return;
        if (editors.Any(e => !e.Flush())) return;
        try
        {
            store.Clear(path => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException));
            foreach (var editor in editors.ToArray()) editor.CloseAfterClear();
        }
        catch (Exception error) { Ui.Error("Could not clear history",error); }
    }
    private void Quit()
    {
        if (editors.Any(e => !e.Flush())) return;
        frozen?.Dispose(); frozen = null;
        foreach (var editor in editors.ToArray()) editor.CloseAfterQuit(); Dispose(); app.Shutdown();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; store.Changed -= HistoryChanged;
        frozen?.Dispose(); hotkey?.Dispose(); if (tray is not null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); }
    }
    internal CaptureStore TestStore => store;
}
