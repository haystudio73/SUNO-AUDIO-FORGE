using System.IO;

namespace StemForge;

// Preserve the model that actually produced the audio, independently of the UI selection.
public sealed record AudioFileStamp(string Path, long Length, long ModifiedUtc)
{
    public static AudioFileStamp? Read(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length > 44
                ? new(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
    public bool Matches(string path) => Read(path) is { } now &&
        string.Equals(Path, now.Path, StringComparison.OrdinalIgnoreCase) && Length == now.Length && ModifiedUtc == now.ModifiedUtc;
}

public sealed record SeparationResult(string Model, AudioFileStamp Source, Dictionary<string, AudioFileStamp> Outputs)
{
    public static string[] Names(string model) => model switch
    {
        "htdemucs" => ["drums", "bass", "other", "vocals"],
        "htdemucs_6s" or "bs_roformer_sw" => ["drums", "bass", "other", "vocals", "guitar", "piano"],
        "drumsep_mdx23c" => ["kick", "snare", "toms", "hat", "ride", "crash"],
        _ => []
    };

    public static SeparationResult? Capture(string model, string source, IEnumerable<Stem> stems)
    {
        var expected = Names(model); var lanes = stems.ToArray();
        if (expected.Length == 0 || lanes.Length != expected.Length ||
            !expected.ToHashSet(StringComparer.Ordinal).SetEquals(lanes.Select(s => s.Name)) ||
            AudioFileStamp.Read(source) is not { } input) return null;
        var files = new Dictionary<string, AudioFileStamp>();
        foreach (var lane in lanes)
        {
            if (AudioFileStamp.Read(lane.Path) is not { } file || file.Path.Equals(input.Path, StringComparison.OrdinalIgnoreCase) ||
                files.Values.Any(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase))) return null;
            files[lane.Name] = file;
        }
        return new(model, input, files);
    }

    public bool Matches(string model, string source, IEnumerable<Stem> stems)
    {
        var lanes = stems.ToArray(); var expected = Names(model);
        return Model == model && Source.Matches(source) && expected.Length > 0 && lanes.Length == expected.Length &&
            expected.ToHashSet(StringComparer.Ordinal).SetEquals(lanes.Select(s => s.Name)) && Outputs.Count == expected.Length &&
            lanes.All(s => Outputs.TryGetValue(s.Name, out var file) && file.Matches(s.Path));
    }

    // Older projects only recorded the selected model. Adopt that saved value once,
    // before UI changes can overwrite it; don't infer a model from stem count alone.
    public static void AdoptLegacy(Project project)
    {
        if (project.SeparationTrackingVersion > 0) return;
        project.SeparationTrackingVersion = 1;
        project.StemSeparation ??= Capture(project.Model, project.Source, project.Stems);
        var drum = project.Stems.FirstOrDefault(s => s.Name == "drums");
        if (drum is not null && AudioFileStamp.Read(drum.Path) is { } parent && parent.Matches(project.DrumPartsSource))
            project.DrumSeparation ??= Capture("drumsep_mdx23c", drum.Path, project.DrumParts);
    }
}
