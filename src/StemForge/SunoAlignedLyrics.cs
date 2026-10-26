using System.Collections.ObjectModel;
using System.Text.Json;

namespace StemForge;

public static class SunoAlignedLyrics
{
    public static string SongId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new InvalidOperationException(L.T("Cần URL Suno đầy đủ dạng suno.com/song/<id> để lấy lời."));
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        string value = "";
        if ((uri.Host is "suno.com" or "www.suno.com" or "suno.ai" or "www.suno.ai") && parts.Length == 2 && parts[0] == "song") value = parts[1];
        if (uri.Host is "cdn1.suno.ai" or "cdn2.suno.ai" or "cdn1.suno.com" or "cdn2.suno.com") value = System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        if (!Guid.TryParseExact(value, "D", out var id)) throw new InvalidOperationException(L.T("Cần URL Suno đầy đủ dạng suno.com/song/<id> để lấy lời."));
        return id.ToString("D");
    }

    public static bool IsSunoOrigin(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Host == "suno.com";

    static string Text(JsonElement obj, string key) => obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
        ? new string(value.GetString()!.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Trim() : "";
    static double? Number(JsonElement obj, string key) => obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
    static bool Valid(double? start, double? end, double duration) => start.HasValue && end.HasValue && start >= 0 && end > start && end <= duration;

    public static ObservableCollection<LyricLine> Parse(JsonElement payload, double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidOperationException(L.T("Chưa có thời lượng audio hợp lệ để đọc lyrics."));
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Object) payload = nested;
        ObservableCollection<LyricLine> lines = [];
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("aligned_lyrics", out var rows) || rows.ValueKind != JsonValueKind.Array) return lines;
        foreach (var row in rows.EnumerateArray().Take(10000))
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            var text = Text(row, "text"); var start = Number(row, "start_s"); var end = Number(row, "end_s");
            if (row.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Array)
            {
                var items = words.EnumerateArray().ToArray();
                if (text.Length == 0) text = string.Join(" ", items.Where(w => w.ValueKind == JsonValueKind.Object)
                    .Select(w => Text(w, "text") is { Length: > 0 } word ? word : Text(w, "word"))).Trim();
                if (items.Length > 0 && items.All(w => w.ValueKind == JsonValueKind.Object && Valid(Number(w, "start_s"), Number(w, "end_s"), duration)))
                { start = items.Min(w => Number(w, "start_s")); end = items.Max(w => Number(w, "end_s")); }
            }
            if (text.Length == 0) continue;
            bool valid = Valid(start, end, duration);
            lines.Add(new() { Text = text, Start = valid ? LyricsFormat.Time(start!.Value) : "", End = valid ? LyricsFormat.Time(end!.Value) : "" });
        }
        return lines;
    }
}
