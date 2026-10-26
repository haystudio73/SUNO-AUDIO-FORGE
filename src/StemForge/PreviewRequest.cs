namespace StemForge;

public static class PreviewRequest
{
    public static object Create(Project project, string stage, string output, Stem? single = null, IEnumerable<Stem>? selectedParts = null)
    {
        IEnumerable<Stem> stems = project.Stems;
        if (stage == "drumsep")
        {
            stems = (selectedParts ?? []).Select(s => new Stem { Name = s.Name, Path = s.Path, Gain = s.Gain }).ToArray();
            if (!stems.Any()) throw new InvalidOperationException(L.T("Chọn ít nhất một kênh DrumSep để nghe."));
        }
        if (single is not null) stems = [new Stem { Name = single.Name, Path = single.Path, Gain = single.Gain }];
        if (stage == "drums")
        {
            var drum = project.Stems.FirstOrDefault(s => s.Name == "drums") ?? throw new InvalidOperationException(L.T("Tách stem drums trước."));
            stems = [new Stem { Name = drum.Name, Path = drum.Path, Gain = drum.Gain }];
        }
        return new
        {
            action = "render", source = project.Source, stems = stems.ToArray(), output,
            processingMode = stage == "mix" ? project.ProcessingMode : "legacy",
            stereo = stage == "mix" ? project.Stereo : new Dictionary<string, object>(),
            refix = stage == "mix" ? project.Refix : new Dictionary<string, object> { ["enabled"] = false },
            drums = stage is "mix" or "drums" ? project.Drums : new Dictionary<string, object>(),
            sampleRate = 48000, bits = "16", normalize = stage == "mix" && project.Normalize, lufs = project.Lufs
        };
    }
}
