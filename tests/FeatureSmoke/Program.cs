using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using StemForge;

var root = Path.GetFullPath(args[0]);
var folder = Path.Combine(root, "artifacts", "features-v05", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
var lines = new[] {
    new LyricLine { Start = "00:01:00.000", End = "00:01:02.450", Text = "Giữ chặt anh, đừng rời xa!" },
    new LyricLine { Start = "00:00:01.230", End = "00:00:03.456", Text = "Tiếng Việt\n\n\n\nẤm áp — yêu thương" }
};
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
var lrc = LyricsFormat.Export(lines, 80, "lrc");
Assert(lrc.StartsWith("[00:01.23]Tiếng Việt") && lrc.Contains("[01:00.00]Giữ chặt anh"), "LRC sorting, Vietnamese, centiseconds, minute rollover");
var srt = LyricsFormat.Export(lines, 80, "srt");
Assert(srt.StartsWith("1\r\n00:00:01,230 --> 00:00:03,456") && srt.Contains("2\r\n00:01:00,000 --> 00:01:02,450"), "SRT indices and time precision independent of locale");
Assert(!srt.Contains("Tiếng Việt\r\n\r\n"), "Blank lines inside cues do not split SRT blocks");
foreach (var format in new[] { "lrc", "srt" })
{
    var file = Path.Combine(folder, "lời hát."+format);
    LyricsFormat.Write(file, lines, 80, format);
    var bytes = File.ReadAllBytes(file);
    Assert(bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "UTF-8 BOM for Windows players");
    Assert(File.ReadAllText(file).Contains("Giữ chặt anh, đừng rời xa!"), "Vietnamese roundtrip");
}
foreach (var bad in new[] { new LyricLine { Start = "abc" }, new LyricLine { Start = "00:00:03.000", End = "00:00:02.000" }, new LyricLine { End = "01:00:00.000" } })
{
    bad.Text = "Câu thử có thời gian không hợp lệ";
    try { LyricsFormat.Validate([bad], 80); throw new Exception("Invalid timestamp accepted"); }
    catch (InvalidOperationException) { }
}
var store = new AppStore(Path.Combine(folder, "lyrics.db"));
var project = new Project { Source = "test.wav", Name = "Tiếng Việt", Lyrics = new() { StemPath = "vocals.wav", Language = "vi", Lines = new(lines) } };
store.SaveSession(project);
Assert(new AppStore(store.DatabasePath).LoadSession("test.wav")!.Lyrics!.Lines[0].Text == lines[0].Text, "SQLite transcript edits survive restart");

async Task<string> Archive(string entryName, string name)
{
    var path = Path.Combine(folder, name+".zip");
    using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
    {
        using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
        await writer.WriteAsync("fixture (not executed)");
    }
    return path;
}
var valid = await Archive("ffmpeg/bin/ffmpeg.exe", "valid");
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(valid)));
var exe = await FfmpegInstaller.ExtractVerifiedAsync(valid, hash.ToLowerInvariant()+"  file.zip", Path.Combine(folder, "good"), CancellationToken.None);
Assert(File.Exists(exe), "Verified package extraction");
try { await FfmpegInstaller.ExtractVerifiedAsync(valid, new string('0', 64), Path.Combine(folder, "hash-fail"), CancellationToken.None); throw new Exception("Hash mismatch accepted"); }
catch (IOException) { Assert(!Directory.Exists(Path.Combine(folder, "hash-fail")), "Hash mismatch writes nothing"); }
foreach (var name in new[] { "../escaped.exe", "C:/escaped.exe", "ffmpeg/bin/tool.exe:stream" })
{
    var bad = await Archive(name, Guid.NewGuid().ToString("N"));
    var badHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(bad)));
    try { await FfmpegInstaller.ExtractVerifiedAsync(bad, badHash, Path.Combine(folder, "unsafe"), CancellationToken.None); throw new Exception("Unsafe archive accepted"); }
    catch (IOException) { }
}
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    try { await FfmpegInstaller.ExtractVerifiedAsync(valid, hash, Path.Combine(folder, "cancelled"), cancellation.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { }
}
Console.WriteLine("PASS: LRC/SRT timing, edits, Vietnamese UTF-8, SQLite, FFmpeg hash, archive paths, cancellation.");
if (args.Contains("--live"))
{
    var installed = await FfmpegInstaller.InstallAsync(Path.Combine(root, "artifacts", "ffmpeg-install-test"), new Progress<string>(Console.WriteLine), CancellationToken.None);
    File.WriteAllText(Path.Combine(root, "artifacts", "ffmpeg-install-test", "verified-path.txt"), installed);
    Console.WriteLine("PASS live download / SHA-256 / extraction / ffmpeg -version: "+installed);
}
