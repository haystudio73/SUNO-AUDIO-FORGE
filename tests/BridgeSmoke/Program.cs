using System.IO;
using System.Text.Json;
using StemForge;

var root = Path.GetFullPath(args[0]);
var client = new EngineClient();
var settings = new Settings();
void Log(string message) => Console.WriteLine(message);
var diagnostics = await client.RunAsync(new { action = "diagnostics" }, settings, Log, CancellationToken.None);
if (!diagnostics.GetProperty("versions").TryGetProperty("demucs", out _)) throw new Exception("Missing diagnostics");
var imported = await client.RunAsync(new { action = "import", source = Path.Combine(root, "artifacts", "smoke", "Synthetic drum test.wav"), directory = Path.Combine(root, "artifacts", "bridge", "âm thanh") }, settings, Log, CancellationToken.None);
var source = imported.GetProperty("source").GetString()!;
var exported = await client.RunAsync(new { action = "render", source, output = Path.Combine(root, "artifacts", "bridge", "mix.flac"), bits = "24", sampleRate = 48000, normalize = true, refix = new { enabled = true } }, settings, Log, CancellationToken.None);
if (!File.Exists(exported.GetProperty("output").GetString())) throw new Exception("Export missing");
using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
try
{
    await client.RunAsync(new { action = "separate", source, directory = Path.Combine(root, "artifacts", "bridge"), device = "cpu" }, settings, Log, cancellation.Token);
    throw new Exception("Cancellation failed");
}
catch (OperationCanceledException) { Console.WriteLine("Cancellation propagated correctly."); }
var model = new Project { Source = source, Stems = [new Stem { Name = "drums", Path = source, Gain = -3, Solo = true }] };
var restored = JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(model, Settings.JsonOptions), Settings.JsonOptions)!;
if (restored.Stems[0].Gain != -3 || !restored.Stems[0].Solo) throw new Exception("Project roundtrip failed");
Console.WriteLine("PASS: C# / Python bridge, Unicode paths, FLAC output, cancellation, project roundtrip.");
foreach (var mode in new[] { "structural", "impact" })
{
    var stereoProject = new Project { Source = source, ProcessingMode = mode, Stereo = new() { ["impactDb"] = 6, ["subHz"] = 45 } };
    var output = Path.Combine(root, "artifacts", "bridge", mode+"-preview.wav");
    var result = await client.RunAsync(PreviewRequest.Create(stereoProject, "mix", output), settings, Log, CancellationToken.None);
    if (result.GetProperty("stereo").GetProperty("mode").GetString() != mode || !File.Exists(output)) throw new Exception("Stereo mode routing failed");
    Console.WriteLine("PASS: native stereo preview without stems: "+mode);
}
var session = JsonSerializer.Deserialize<Project>(File.ReadAllText(Path.Combine(root, "artifacts", "smoke", "Synthetic test.stemforge.json")), Settings.JsonOptions)!;
if (session.Stems.Count > 0)
{
    session.Drums["replacement"] = 60;
    session.Drums["thresholdDb"] = -48;
    session.Drums["retriggerMs"] = 65;
    session.Drums["bleed"] = 20;
    session.Drums["dynamics"] = 80;
    session.Drums["sampleMode"] = "velocity";
    foreach (var stage in new[] { "stems", "drums", "mix", "stem:drums" })
    {
        var preview = Path.Combine(root, "artifacts", "bridge", stage.Replace(':', '-')+".wav");
        var request = PreviewRequest.Create(session, stage, preview, stage == "stem:drums" ? session.Stems.First(s => s.Name == "drums") : null);
        var result = await client.RunAsync(request, settings, Log, CancellationToken.None);
        if (!File.Exists(preview) || result.GetProperty("bits").GetString() != "16") throw new Exception("Preview render failed: "+stage);
        if (stage is "drums" or "mix" && !result.GetProperty("hits").TryGetProperty("kick", out _)) throw new Exception("Trigger parameters did not reach engine");
        Console.WriteLine("PASS preview: "+stage);
    }
}
var cascadeFile = Path.Combine(root, "artifacts", "cascade-real", "Cascade.stemforge.json");
if (File.Exists(cascadeFile))
{
    var cascade = JsonSerializer.Deserialize<Project>(File.ReadAllText(cascadeFile), Settings.JsonOptions)!;
    var drum = cascade.Stems.First(s => s.Name == "drums");
    cascade.Drums["parts"] = cascade.DrumParts;
    cascade.Drums["partsSource"] = cascade.DrumPartsSource;
    var midi = await client.RunAsync(new { action = "drum_midi", source = drum.Path, drums = cascade.Drums,
        output = Path.Combine(root, "artifacts", "bridge", "bộ trống.mid") }, settings, Log, CancellationToken.None);
    var rendered = await client.RunAsync(PreviewRequest.Create(cascade, "mix", Path.Combine(root, "artifacts", "bridge", "cascade.wav")), settings, Log, CancellationToken.None);
    foreach (var name in new[] { "kick", "snare", "hat" })
        if (midi.GetProperty("hits").GetProperty(name).GetInt32() != rendered.GetProperty("hits").GetProperty(name).GetInt32())
            throw new Exception("MIDI / rendered hit decisions differ: "+name);
    var parts = await client.RunAsync(new { action = "separate", source = drum.Path, model = "drumsep_mdx23c",
        directory = Path.Combine(root, "artifacts", "bridge"), modelDirectory = Path.Combine(root, "artifacts", "model-cache"), device = "auto" }, settings, Log, CancellationToken.None);
    if (parts.GetProperty("stems").GetArrayLength() != 6) throw new Exception("Native MDX bridge failed");
    Console.WriteLine("PASS: native DrumSep inference, percussion MIDI, consistent hit counts across MIDI / 48 kHz preview.");
}
