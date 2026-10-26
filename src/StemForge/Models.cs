using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace StemForge;

public class Stem : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public double[] Waveform { get; set; } = [];
    double gain;
    bool mute, solo;
    public double Gain { get => gain; set { gain = value; Changed(); } }
    public bool Mute { get => mute; set { mute = value; Changed(); } }
    public bool Solo { get => solo; set { solo = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public class Project
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string DownloadKind { get; set; } = "";
    // Missing fields in existing projects preserve the old sound/routing.
    public string ProcessingMode { get; set; } = "legacy";
    public Dictionary<string, object> Stereo { get; set; } = [];
    public string Preview { get; set; } = "";
    public int SampleRate { get; set; }
    public double Duration { get; set; }
    public double[] Waveform { get; set; } = [];
    public MusicAnalysis? Analysis { get; set; }
    public LyricsDocument? Lyrics { get; set; }
    public ObservableCollection<Stem> Stems { get; set; } = [];
    public ObservableCollection<Stem> DrumParts { get; set; } = [];
    public string DrumPartsSource { get; set; } = "";
    public SeparationResult? StemSeparation { get; set; }
    public SeparationResult? DrumSeparation { get; set; }
    public int SeparationTrackingVersion { get; set; }
    public Dictionary<string, object> Refix { get; set; } = [];
    public Dictionary<string, object> Drums { get; set; } = [];
    public string Model { get; set; } = "htdemucs";
    public string Device { get; set; } = "auto";
    public int ExportRate { get; set; } = 48000;
    public string ExportBits { get; set; } = "24";
    public string ExportFormat { get; set; } = "WAV";
    public double Lufs { get; set; } = -14;
    public bool Normalize { get; set; } = true;
}

public class Settings
{
    public bool LivePreview { get; set; } = true;
    public string Language { get; set; } = "vi";
    public List<string> CollapsedSteps { get; set; } = [];
    public string Python { get; set; } = "python";
    public string Ffmpeg { get; set; } = "";
    public string Theme { get; set; } = "Dark";
    public bool Repeat { get; set; }
    public double PlaybackVolume { get; set; } = 50;
    public bool ShowOriginalWaveform { get; set; } = true;
    public bool ShowProcessedWaveform { get; set; } = true;
    public bool ShowDrumWaveform { get; set; } = true;
    public bool ShowProcessedDrumWaveform { get; set; } = true;
    public Dictionary<string, bool> DrumPartWaveforms { get; set; } = [];
    public bool RestoreLastSession { get; set; } = true;
    public string LastSessionSource { get; set; } = "";
    public string WhisperModel { get; set; } = "small";
    public string WhisperLanguage { get; set; } = "auto";
    public string WhisperDevice { get; set; } = "auto";
    public DrumSetup? DrumDefaults { get; set; }
    public List<DrumPreset> SavedDrumPresets { get; set; } = [];
    public static string DirectoryPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StemForge");
    public static JsonSerializerOptions JsonOptions { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static Settings Load() => AppStore.Default.LoadSettings();
    public void Save() => AppStore.Default.SaveSettings(this);
}

public sealed class MusicAnalysis
{
    public double? Bpm { get; set; }
    public string? Key { get; set; }
    public string? AlternateKey { get; set; }
    public double TempoStrength { get; set; }
    public double KeyCorrelation { get; set; }
    public double AnalyzedSeconds { get; set; }
    public string Method { get; set; } = "";
    public string Notes { get; set; } = "";
}
