using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace StemForge;

public class LyricLine
{
    public string Start { get; set; } = "00:00:00.000";
    public string End { get; set; } = "00:00:01.000";
    public string Text { get; set; } = "";
}

public class LyricsDocument
{
    public string StemPath { get; set; } = "";
    public long StemModifiedTicks { get; set; }
    public string Language { get; set; } = "";
    public string Model { get; set; } = "small";
    public string Provider { get; set; } = "whisper";
    public string SourceUrl { get; set; } = "";
    public string Timing { get; set; } = "model";
    public ObservableCollection<LyricLine> Lines { get; set; } = [];
}

public static class LyricsFormat
{
    public static string Time(double seconds) => TimeSpan.FromMilliseconds(Math.Round(seconds*1000)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    public static double Seconds(string time)
    {
        if (!TimeSpan.TryParseExact(time.Trim(), [@"hh\:mm\:ss\.fff", @"hh\:mm\:ss", @"mm\:ss\.fff", @"mm\:ss"], CultureInfo.InvariantCulture, out var value) || value.TotalSeconds < 0)
            throw new InvalidOperationException(L.T("Thời gian phải có dạng 00:01:23.456 (giờ:phút:giây.mili giây)."));
        return value.TotalSeconds;
    }
    public static List<(double Start, double End, string Text)> Validate(IEnumerable<LyricLine> lines, double duration)
    {
        List<(double Start, double End, string Text)> result = [];
        int index = 0;
        foreach (var line in lines)
        {
            index++;
            if (string.IsNullOrWhiteSpace(line.Text)) continue;
            if (string.IsNullOrWhiteSpace(line.Start) || string.IsNullOrWhiteSpace(line.End))
                throw new InvalidOperationException(L.T("Lời chưa có mốc thời gian. Hãy điền Bắt đầu/Kết thúc trước khi nghe câu hoặc xuất LRC/SRT; có thể xuất TXT ngay."));
            var start = Seconds(line.Start); var end = Seconds(line.End);
            if (end <= start || end > duration+.05) throw new InvalidOperationException(L.F($"Dòng {index}: thời gian kết thúc phải lớn hơn bắt đầu và nằm trong audio."));
            if (!string.IsNullOrWhiteSpace(line.Text)) result.Add((start, end, line.Text.Trim().Normalize(NormalizationForm.FormC)));
        }
        return result.OrderBy(line => line.Start).ToList();
    }
    public static string Export(IEnumerable<LyricLine> lines, double duration, string format)
    {
        if (format == "txt")
        {
            var plain = string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).Select(l => l.Text.Trim().Normalize(NormalizationForm.FormC)));
            if (plain.Length == 0) throw new InvalidOperationException(L.T("Chưa có lời để xuất."));
            return plain + Environment.NewLine;
        }
        var entries = Validate(lines, duration);
        if (entries.Count == 0) throw new InvalidOperationException(L.T("Chưa có lời để xuất."));
        var text = new StringBuilder();
        if (format == "srt")
        {
            int index = 1;
            foreach (var line in entries)
            {
                text.AppendLine((index++).ToString(CultureInfo.InvariantCulture));
                text.AppendLine($"{Time(line.Start).Replace('.', ',')} --> {Time(line.End).Replace('.', ',')}");
                text.AppendLine(string.Join("\r\n", line.Text.Replace("\r", "").Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)))).AppendLine();
            }
        }
        else if (format == "lrc")
        {
            foreach (var line in entries)
            {
                long centiseconds = (long)Math.Round(line.Start*100, MidpointRounding.AwayFromZero);
                text.Append(CultureInfo.InvariantCulture, $"[{centiseconds/6000:00}:{centiseconds/100%60:00}.{centiseconds%100:00}]");
                text.AppendLine(line.Text.Replace('\r', ' ').Replace('\n', ' '));
            }
        }
        else throw new ArgumentException(L.T("Chọn LRC hoặc SRT."));
        return text.ToString();
    }
    public static void Write(string path, IEnumerable<LyricLine> lines, double duration, string format)
    {
        var content = Export(lines, duration, format);
        var temp = path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllText(temp, content, new UTF8Encoding(true)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
