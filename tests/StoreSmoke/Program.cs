using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StemForge;

var root = Path.GetFullPath(args[0]);
var folder = Path.Combine(root, "artifacts", "store-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var legacy = Path.Combine(folder, "settings.json");
File.WriteAllText(legacy, JsonSerializer.Serialize(new Settings { Python = "C:\\Âm nhạc\\python.exe", Ffmpeg = "ffmpeg-test" }, Settings.JsonOptions));
var store = new AppStore(Path.Combine(folder, "stemforge.db"));
var settings = store.LoadSettings();
Assert(settings.Python.Contains("Âm nhạc"), "Legacy migration preserves Unicode");
settings.Theme = "Light"; settings.Repeat = true; settings.RestoreLastSession = false;
store.SaveSettings(settings);
File.WriteAllText(legacy, "invalid legacy JSON must not be read again");
var reopened = new AppStore(store.DatabasePath);
Assert(reopened.LoadSettings().Theme == "Light" && reopened.LoadSettings().Repeat, "SQLite settings survive reopen; one-time migration");
var project = new Project { Name = "Bài 'thử' — âm nhạc", Source = Path.Combine(folder, "nhạc.wav"), Duration = 8,
    SourceUrl = "https://suno.com/song/52e94026-fa85-4779-b54e-da4d8df58d62", DownloadKind = "video",
    Stems = [new Stem { Name = "drums", Path = "drums.wav", Gain = -6, Mute = true, Solo = true }, new Stem { Name = "vocals", Path = "vocals.wav" }],
    Drums = new() { ["transient"] = 35, ["replacement"] = 20 }, Refix = new() { ["enabled"] = true, ["mud"] = -4 } };
store.SaveSession(project, "Nhập audio");
project.Stems[0].Gain = -9; store.SaveSession(project);
store.Record("Xuất bản mix", project, "mix.flac", "-14 LUFS");
Assert(reopened.Sessions().Count == 1, "Autosave upserts instead of duplicating sessions");
var restored = reopened.LoadSession(project.Source)!;
Assert(restored.SourceUrl == project.SourceUrl && restored.DownloadKind == "video", "Suno origin survives database reopen");
Assert(restored.ProcessingMode == "legacy", "Missing mode preserves legacy projects");
Assert(restored.Stems[0].Gain == -9 && restored.Stems[0].Mute && restored.Stems[0].Solo, "Gain/mute/solo snapshot");
Assert(restored.Drums["replacement"].ToString() == "20", "DSP snapshot");
Assert(reopened.History().Count == 2 && reopened.History()[0].Output == "mix.flac", "Ordered event history");
Assert(!reopened.Sessions()[0].Available, "Missing audio is labelled");
project.ProcessingMode = "structural"; project.Stereo = new() { ["attack80"] = 3.5, ["harsh"] = -2.5, ["subHz"] = 42 };
store.SaveSession(project);
var stereoRestored = reopened.LoadSession(project.Source)!;
Assert(stereoRestored.ProcessingMode == "structural" && stereoRestored.Stereo["attack80"].ToString() == "3.5", "Stereo mode/parameters survive database reopen");
using (var db = new SqliteConnection("Data Source="+store.DatabasePath))
{
    db.Open(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
    Assert((string?)command.ExecuteScalar() == "ok", "SQLite integrity");
}
foreach (var stage in new[] { "mix", "stems", "drums", "stem:drums" })
{
    var value = PreviewRequest.Create(project, stage, "preview.wav", stage == "stem:drums" ? project.Stems[0] : null);
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, Settings.JsonOptions));
    Assert(doc.RootElement.GetProperty("processingMode").GetString() == (stage == "mix" ? "structural" : "legacy"), "Only final preview uses stereo mode");
    var json = doc.RootElement;
    Assert(json.GetProperty("refix").GetProperty("enabled").GetBoolean() == (stage == "mix"), "Refix applies only to final mix");
    Assert(json.GetProperty("drums").EnumerateObject().Any() == (stage is "mix" or "drums"), "Drum effects routing");
    if (stage is "drums" or "stem:drums")
    {
        var stems = json.GetProperty("stems"); Assert(stems.GetArrayLength() == 1, "Single-stem audition");
        Assert(!stems[0].GetProperty("mute").GetBoolean() && !stems[0].GetProperty("solo").GetBoolean(), "Audition ignores mixer mute/solo");
    }
}
Console.WriteLine("PASS: SQLite migration, durable settings, session snapshots/upserts, ordered history, Unicode/SQL quoting, missing-file state, integrity, all preview routing modes.");
static void Assert(bool value, string label) { if (!value) throw new Exception(label); }
