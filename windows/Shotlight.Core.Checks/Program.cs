using System.Text.Json;
using Shotlight.Core;

int passed = 0;
var errors = new List<string>();
void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS: " + name); }
    catch (Exception error) { errors.Add(name + ": " + error); Console.WriteLine("FAIL: " + name + ": " + error.Message); }
}
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action action) { try { action(); } catch (Exception error) when (error is IOException or JsonException or InvalidDataException) { return; } throw new Exception("Invalid data was accepted."); }
Annotation Text(string value) => new(Tool.Text,[new(10,20)],0xFFFF0000,4,value);
var root = Path.Combine(Path.GetTempPath(),"Shotlight-core-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXp8AAAAASUVORK5CYII=");
try
{
    Check("reverse drag produces the same pixel rectangle",() =>
    {
        var forward = PixelRect.Between(new(40,30),new(150,90),400,240);
        var reverse = PixelRect.Between(new(150,90),new(40,30),400,240);
        Require(forward == reverse && reverse == new PixelRect(40,30,110,60),"Reverse drag moved the crop.");
    });
    Check("selection clamps to display edges and rejects a click",() =>
    {
        Require(PixelRect.Between(new(-30,-50),new(700,450),400,240) == new PixelRect(0,0,400,240),"Selection escaped display bounds.");
        Require(!PixelRect.Between(new(20,20),new(20,20),400,240).IsCapture,"A click generated a screenshot.");
    });
    Check("fractional pointer coordinates preserve all selected pixels",() =>
        Require(PixelRect.Between(new(40.2f,30.4f),new(150.2f,90.7f),400,240) == new PixelRect(40,30,111,61),"Fractional crop was truncated."));
    Check("undo/redo and branched edits",() =>
    {
        var document = new AnnotationDocument(); document.Add(Text("one")); document.Add(Text("two"));
        document.Undo(); Require(document.Marks.Count == 1,"Undo failed."); document.Redo(); Require(document.Marks.Count == 2,"Redo failed.");
        document.Undo(); document.Add(Text("three")); Require(document.RedoStack.Count == 0 && document.Marks[^1].Text == "three","A branched edit retained stale redo.");
    });
    Check("live inline edit persists without committing the editing session",() =>
    {
        var document = new AnnotationDocument(); document.Add(Text("before"));
        var live = document.Snapshot(new TextDraft(0,new(10,20),0xFF0000FF,8,"after\nsecond line"));
        Require(document.Marks[0].Text == "before" && live.Marks[0].Text == "after\nsecond line" && live.Undo[^1][0].Text == "before","Live snapshot lost its undo baseline.");
        document.CommitText(new(0,new(10,20),0xFF0000FF,8,"after\nsecond line"));
        document.Undo(); Require(document.Marks[0].Text == "before","Committed text could not be undone.");
    });
    Check("empty text deletes only the edited mark",() =>
    {
        var document = new AnnotationDocument(); document.Add(Text("one")); document.Add(Text("two"));
        document.CommitText(new(0,new(10,20),0xFFFF0000,4,""));
        Require(document.Marks.Count == 1 && document.Marks[0].Text == "two","Text removal changed another annotation.");
        document.Undo(); Require(document.Marks.Count == 2,"Text removal could not be undone.");
    });
    Check("snapshot mutations cannot corrupt previous undo states",() =>
    {
        var document = new AnnotationDocument(); document.Add(Text("retained")); var snapshot = document.Snapshot(); snapshot.Marks[0].Points[0] = new(900,900);
        Require(document.Marks[0].Points[0] == new PixelPoint(10,20),"Snapshots share mutable point arrays.");
    });
    Check("drafts reopen after restart with original bytes and undo/redo",() =>
    {
        string directory = Path.Combine(root,"restart"); var store = new CaptureStore(directory);
        var capture = store.Add(png,png,400,200); var document = new AnnotationDocument(); document.Add(Text("one")); document.Add(Text("two")); document.Undo();
        store.Save(capture.Id,document.Snapshot()); var restarted = new CaptureStore(directory); var restored = new AnnotationDocument(restarted.Get(capture.Id));
        Require(restarted.Original(capture.Id).SequenceEqual(png),"Original PNG bytes changed.");
        restored.Redo(); Require(restored.Marks.Count == 2 && restored.Marks[1].Text == "two","Redo history did not survive restart.");
        restored.Undo(); restored.Undo(); Require(restored.Marks.Count == 0,"Undo history did not survive restart.");
    });
    Check("unfinished inline text can be recovered after a restart",() =>
    {
        string directory = Path.Combine(root,"typing"); var store = new CaptureStore(directory); var capture = store.Add(png,png,400,200);
        var document = new AnnotationDocument(); store.Save(capture.Id,document.Snapshot(new(null,new(25,30),0xFF00AA00,4,"forgot to export")));
        var restored = new AnnotationDocument(new CaptureStore(directory).Get(capture.Id));
        Require(restored.Marks.Single().Text == "forgot to export","Typed text was lost."); restored.Undo(); Require(restored.Marks.Count == 0,"Recovered live text has no undo baseline.");
    });
    Check("retention removes the oldest capture and edits preserve capture order",() =>
    {
        var store = new CaptureStore(Path.Combine(root,"retention"),2); var time = DateTimeOffset.UtcNow;
        var first = store.Add(png,png,1,1,time); var second = store.Add(png,png,1,1,time.AddSeconds(1));
        var document = new AnnotationDocument(); document.Add(Text("older edit")); store.Save(first.Id,document.Snapshot());
        Require(store.Records[0].Id == second.Id,"Editing changed capture order.");
        var third = store.Add(png,png,1,1,time.AddSeconds(2)); Require(!store.Contains(first.Id) && store.Records[0].Id == third.Id && store.Records[1].Id == second.Id,"Wrong capture was evicted.");
        store.SetLimit(1); Require(store.Records.Single().Id == third.Id,"Reducing retention kept the wrong capture.");
    });
    Check("incomplete captures and malformed drafts do not hide valid history",() =>
    {
        string directory = Path.Combine(root,"recovery"); var store = new CaptureStore(directory); var valid = store.Add(png,png,1,1);
        Directory.CreateDirectory(Path.Combine(directory,".pending-interrupted")); string broken = Path.Combine(directory,Guid.NewGuid().ToString()); Directory.CreateDirectory(broken); File.WriteAllText(Path.Combine(broken,"draft.json"),"{broken");
        var restored = new CaptureStore(directory); Require(restored.Records.Single().Id == valid.Id && restored.RecoveryWarnings.Count == 1 && Directory.Exists(broken),"Malformed history was silently deleted or blocked recovery.");
    });
    Check("clearing uses the supplied recycle operation and recreates storage",() =>
    {
        string directory = Path.Combine(root,"clear"); var store = new CaptureStore(directory); store.Add(png,png,1,1); string recycle = directory+"-recycled";
        store.Clear(path => Directory.Move(path,recycle)); Require(store.Records.Count == 0 && Directory.Exists(directory) && Directory.GetDirectories(recycle).Length == 1,"Clear failed to preserve the recycled originals.");
        store.Add(png,png,1,1); Require(store.Records.Count == 1,"Capture failed after clearing.");
    });
    Check("settings retain the shortcut and history limit",() =>
    {
        string path = Path.Combine(root,"settings.json"); var settings = new UserSettings { Shortcut = new(0x4B,0x0002|0x0004,"K"), HistoryLimit = 80 };
        settings.Save(path); Require(UserSettings.Load(path) == settings && settings.Shortcut.Display == "Ctrl+Shift+K","Settings were not restored.");
        Require(!new CaptureShortcut(0x43,2,"C").Valid && !new CaptureShortcut(0x53,4,"S").Valid && !new CaptureShortcut(0x7B,2,"F12").Valid,"A reserved or modifier-free hotkey was allowed.");
    });
    Check("copy and welcome preferences persist while older settings keep copy-close enabled",() =>
    {
        string path = Path.Combine(root,"copy-settings.json"); File.WriteAllText(path,"{\"HistoryLimit\":50}");
        Require(UserSettings.Load(path).CloseEditorAfterCopy && !UserSettings.Load(path).HasSeenWelcome,"Old settings changed default copy behavior.");
        var settings = UserSettings.Load(path) with { CloseEditorAfterCopy = false, HasSeenWelcome = true };
        settings.Save(path); Require(UserSettings.Load(path) == settings,"Copy/welcome preferences did not persist.");
    });
    Check("edited thumbnail invalidation and upgrades preserve originals and undo",() =>
    {
        var store = new CaptureStore(Path.Combine(root,"thumbnails")); var record = store.Add(png,png,1,1);
        var document = new AnnotationDocument(); document.Add(Text("edited")); store.Save(record.Id,document.Snapshot());
        Require(!store.ThumbnailIsCurrent(record.Id),"A stale thumbnail was marked current.");
        byte[] updated = [1,2,3]; store.UpdateThumbnail(record.Id,updated);
        var reopened = new CaptureStore(store.Root);
        Require(reopened.ThumbnailIsCurrent(record.Id) && reopened.Thumbnail(record.Id).SequenceEqual(updated) && reopened.Original(record.Id).SequenceEqual(png),"Thumbnail refresh changed original pixels.");
        var restored = new AnnotationDocument(reopened.Get(record.Id)); restored.Undo();
        Require(restored.Marks.Count == 0,"Thumbnail refresh discarded undo history.");
    });
    Check("fit shows all image edges without enlarging small captures",() =>
    {
        foreach (var (width,height) in new[] { (900d,460d),(460d,900d),(8000d,4000d) })
        {
            double scale = PreviewScale.Fit(width,height,680,400);
            Require(width*scale <= 624.001 && height*scale <= 344.001,"Fit clipped an image edge.");
        }
        Require(PreviewScale.Fit(200,100,680,400) == 1,"Fit enlarged a small screenshot.");
    });
    Check("invalid annotation coordinates and invalid saved settings are rejected",() =>
    {
        Reject(() => (Text("invalid") with { Points = [new(float.NaN,20)] }).Validate());
        string path = Path.Combine(root,"invalid-settings.json"); File.WriteAllText(path,"{\"HistoryLimit\":0}"); Reject(() => UserSettings.Load(path));
    });
}
finally { Directory.Delete(root,recursive: true); }
Console.WriteLine($"\n{passed} passed; {errors.Count} failed.");
if (errors.Count > 0) { Console.Error.WriteLine(string.Join("\n",errors)); return 1; }
return 0;
