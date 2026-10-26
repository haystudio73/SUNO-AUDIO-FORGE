namespace StemForge;

// Authored starting points for StemForge's controls, not measured genre targets.
// Defaults contain only controls; audio paths, MIDI mapping and routing belong to a project.
public sealed record DrumSetup(double Transient = 0, double Multiband = 0, double Parallel = 0,
    double Replacement = 0, double Sensitivity = 50, double ThresholdDb = -48, double RetriggerMs = 80,
    double Bleed = 0, double Dynamics = 100, string Kit = "Studio", string SampleMode = "roundRobin", string BlendMode = "layer")
{
    public bool IsValid => In(Transient, -100, 100) && In(Multiband, 0, 100) && In(Parallel, 0, 100) &&
        In(Replacement, 0, 100) && In(Sensitivity, 0, 100) && In(ThresholdDb, -80, 0) && In(RetriggerMs, 20, 250) &&
        In(Bleed, 0, 100) && In(Dynamics, 0, 100) && Kit is "Studio" or "Electronic" &&
        SampleMode is "roundRobin" or "velocity" && BlendMode is "layer" or "replace";
    static bool In(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
}

public sealed record DrumPreset(string Id, string Name, string Description, DrumSetup Setup)
{
    public override string ToString() => Name;
}

public static class DrumPresets
{
    public static IReadOnlyList<DrumPreset> All => new DrumPreset[]
    {
        new("neutral", L.T("Trung tính · giữ trống gốc"), L.T("Tắt hiệu ứng và sample để có điểm so sánh trước khi chỉnh."), new()),
        new("ballad", "Ballad / Bolero", L.T("Giữ độ mềm và lực đánh tự nhiên; chồng sample nhẹ. Nghe lại các tiếng trống nhỏ."),
            new(Transient: 10, Multiband: 10, Parallel: 10, Replacement: 20, Sensitivity: 65, ThresholdDb: -54, RetriggerMs: 70, SampleMode: "velocity")),
        new("pop", "Pop / V-Pop", L.T("Tăng độ rõ đầu tiếng vừa phải; giữ chỗ cho giọng hát trong bản mix."),
            new(Transient: 25, Multiband: 20, Parallel: 20, Replacement: 35, Sensitivity: 55, ThresholdDb: -48, RetriggerMs: 75)),
        new("acoustic", L.T("Acoustic / Jazz · tiếng nhỏ"), L.T("Nén nhẹ, ưu tiên mạnh nhẹ và roll nhanh. Sample ở mức thấp để giữ sắc thái; độ nhạy cao có thể bắt cả tiếng nền."),
            new(Transient: 5, Multiband: 5, Parallel: 10, Replacement: 15, Sensitivity: 75, ThresholdDb: -56, RetriggerMs: 40, SampleMode: "velocity")),
        new("rock", "Rock / Live", L.T("Thêm độ bật và độ dày; kiểm tra kick/snare có bị bắt lặp khi nhiều bleed."),
            new(Transient: 35, Multiband: 30, Parallel: 35, Replacement: 45, Sensitivity: 55, ThresholdDb: -44, RetriggerMs: 70, Dynamics: 85)),
        new("dance", "Dance / EDM", L.T("Đầu tiếng nổi và lực đánh đều hơn; kiểm tra mức sample để tránh trống quá dày."),
            new(Transient: 40, Multiband: 35, Parallel: 30, Replacement: 50, Sensitivity: 50, ThresholdDb: -42, RetriggerMs: 60, Dynamics: 70, Kit: "Electronic")),
        new("hiphop", "Hip-hop / R&B", L.T("Giữ độ nặng của kick, thêm thân snare; nghe lại các hi-hat nhanh trước khi xuất."),
            new(Transient: 25, Multiband: 25, Parallel: 30, Replacement: 40, Sensitivity: 60, ThresholdDb: -48, RetriggerMs: 45, Dynamics: 90)),
        new("rap-reference", L.T("Rap / Hip-hop · theo ảnh"), L.T("Đầu tiếng mạnh, thêm thân trống. Với hi-hat dày, thử giảm Transient xuống 40–50%. Chống lặp 70 ms có thể bỏ hi-hat cách nhau 62,5 ms (nốt 1/32 ở 120 BPM)."),
            new(Transient: 55, Multiband: 25, Parallel: 40, Replacement: 50, Sensitivity: 55, ThresholdDb: -42, RetriggerMs: 70, Bleed: 5, Dynamics: 90)),
        new("jazz-reference", L.T("Jazz · theo ảnh / hit rõ"), L.T("Nén nhẹ nhưng trigger ưu tiên tiếng rõ. −36 dBFS, độ nhạy 35% và chống lặp 120 ms có thể bỏ ghost note/roll. Muốn giữ tiếng nhỏ, thử Acoustic / Jazz · tiếng nhỏ rồi tinh chỉnh."),
            new(Transient: 25, Multiband: 10, Parallel: 15, Replacement: 25, Sensitivity: 35, ThresholdDb: -36, RetriggerMs: 120, Bleed: 15, Dynamics: 100))
    };
}
