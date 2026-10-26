using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using StemForge;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]);
        var folder = Path.Combine(root, "artifacts", "separation-v0111", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var app = new App(false); app.InitializeComponent();
        var store = new AppStore(Path.Combine(folder, "test.db"));
        string source = Path.Combine(folder, "original.wav"); WriteAudio(source);
        var project = new Project { Source = source, Preview = source, Name = "Reuse test", Duration = 1, SampleRate = 44100 };
        var calls = new List<string>(); bool failParts = false;
        Task<JsonElement> Run(object request)
        {
            var json = JsonSerializer.SerializeToElement(request, Settings.JsonOptions);
            string model = json.GetProperty("model").GetString()!; calls.Add(model);
            if (failParts && model == "drumsep_mdx23c") throw new InvalidOperationException("Simulated stage 2 failure");
            string output = Path.Combine(folder, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
            var stems = SeparationResult.Names(model).Select(name => { var path = Path.Combine(output, name+".wav"); WriteAudio(path); return new Stem { Name = name, Path = path }; }).ToArray();
            return Task.FromResult(JsonSerializer.SerializeToElement(new { stems, model }, Settings.JsonOptions));
        }
        var window = new MainWindow(store, Run); window.LoadProjectSnapshot(project);
        void Core(string method, params object[] values) => ((Task)typeof(MainWindow).GetMethod(method, BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window, values)!).GetAwaiter().GetResult();
        void Button(string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Core("SeparateCascadeCore");
        Assert(calls.SequenceEqual(new[] { "bs_roformer_sw", "drumsep_mdx23c" }), "Fresh cascade runs both stages"); calls.Clear();
        var originalParts = project.DrumParts; var originalStems = project.Stems;
        project.Stems[0].Gain = -8; project.Stems[0].Mute = true;
        Button("SeparateStemsButton"); Button("SeparatePartsButton"); Button("CascadeButton");
        Assert(calls.Count == 0 && ReferenceEquals(originalParts,project.DrumParts) && ReferenceEquals(originalStems,project.Stems) && project.Stems[0].Gain == -8, "All three buttons skip existing results and preserve mixer");
        var modelBox = (ComboBox)window.FindName("ModelBox"); modelBox.SelectedIndex = 1;
        Assert(((Button)window.FindName("SeparateStemsButton")).Content.ToString() == "Tách stems", "Different six-stem model is not treated as same result");
        modelBox.SelectedIndex = 2; ((ComboBox)window.FindName("DeviceBox")).SelectedIndex = 1;
        Button("SeparateStemsButton"); Assert(calls.Count == 0, "Switching back / changing device reuses model result");
        var restored = new AppStore(store.DatabasePath).LoadSession(source)!;
        Assert(restored.StemSeparation!.Matches("bs_roformer_sw",source,restored.Stems), "Actual model metadata survives SQLite");
        window.LoadProjectSnapshot(restored); project = restored;
        Core("SeparateCascadeCore"); Assert(calls.Count == 0, "Restart preserves both stages");
        File.Delete(project.DrumParts[0].Path); Core("SeparateCascadeCore");
        Assert(calls.SequenceEqual(new[] { "drumsep_mdx23c" }), "Missing part repeats only stage 2"); calls.Clear();
        File.AppendAllText(project.DrumParts[0].Path, "modified"); Core("SeparatePartsCore");
        Assert(calls.SequenceEqual(new[] { "drumsep_mdx23c" }), "Modified output is not reused"); calls.Clear();
        File.Delete(project.Stems.First(s=>s.Name=="bass").Path);
        window.LoadProjectSnapshot(project);
        Core("SeparateCascadeCore"); Assert(calls.SequenceEqual(new[] { "bs_roformer_sw", "drumsep_mdx23c" }), "Missing stem can reopen and regenerates dependent stage"); calls.Clear();
        File.AppendAllText(source,"changed"); Core("SeparateCascadeCore");
        Assert(calls.SequenceEqual(new[] { "bs_roformer_sw", "drumsep_mdx23c" }), "Changed source invalidates stages"); calls.Clear();
        Core("SeparateStemsCore", "htdemucs");
        Assert(project.Stems.Count==4 && project.DrumParts.Count==0 && project.DrumSeparation is null, "New model clears child results"); calls.Clear();
        failParts=true;
        try { Core("SeparateCascadeCore"); throw new Exception("Expected failure"); } catch (InvalidOperationException) { }
        Assert(project.StemSeparation!.Model=="bs_roformer_sw" && calls.SequenceEqual(new[]{"bs_roformer_sw","drumsep_mdx23c"}), "Completed first stage persists after failure");
        calls.Clear(); failParts=false; Core("SeparateCascadeCore");
        Assert(calls.SequenceEqual(new[]{"drumsep_mdx23c"}), "Retry resumes second stage only");
        var legacy=JsonSerializer.Deserialize<Project>(JsonSerializer.Serialize(project,Settings.JsonOptions),Settings.JsonOptions)!;
        legacy.StemSeparation=null; legacy.DrumSeparation=null; legacy.SeparationTrackingVersion=0;
        SeparationResult.AdoptLegacy(legacy);
        Assert(legacy.StemSeparation?.Model=="bs_roformer_sw" && legacy.DrumSeparation is not null, "Legacy complete results adopted once");
        legacy.Model="htdemucs_6s"; SeparationResult.AdoptLegacy(legacy);
        Assert(legacy.StemSeparation!.Model=="bs_roformer_sw", "Changing picker doesn't rewrite actual model");
        Assert(SeparationResult.Capture("htdemucs", source, project.Stems)==null, "Wrong lane set rejected");
        var part=project.DrumParts[0]; File.WriteAllBytes(part.Path, []);
        Assert(!project.DrumSeparation!.Matches("drumsep_mdx23c",project.DrumPartsSource,project.DrumParts), "Empty file invalidates result");
        window.Close(); app.Shutdown();
        Console.WriteLine("PASS: all separation buttons skip, model/device identity, SQLite restart, missing/changed files, stage-2 resume, old-project migration and mixer preservation.");
    }
    static void WriteAudio(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36+400); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)2);
        writer.Write(44100); writer.Write(176400); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(400); writer.Write(new byte[400]);
    }
    static void Assert(bool condition,string message) { if(!condition) throw new Exception(message); }
}
