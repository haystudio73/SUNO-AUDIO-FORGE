using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace StemForge;

public static class FfmpegInstaller
{
    public const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    public static async Task<string> InstallAsync(string toolsDirectory, IProgress<string> progress, CancellationToken token, HttpClient? http = null)
    {
        if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException(L.T("Bản FFmpeg này cần Windows 64-bit."));
        Directory.CreateDirectory(toolsDirectory);
        var staging = Path.Combine(Path.GetFullPath(toolsDirectory), "download-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        using var ownedClient = http is null ? new HttpClient { Timeout = TimeSpan.FromMinutes(20) } : null;
        var client = http ?? ownedClient!;
        try
        {
            progress.Report(L.T("Đang lấy mã kiểm tra SHA-256 từ gyan.dev…"));
            var expected = await client.GetStringAsync(DownloadUrl+".sha256", token);
            var archive = Path.Combine(staging, "ffmpeg.zip");
            using (var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new IOException(L.T("Nguồn tải phải dùng HTTPS."));
                var total = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(archive);
                byte[] buffer = new byte[131072]; long received = 0; long lastMb = -1;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    received += read;
                    if (received > 400L*1024*1024) throw new IOException(L.T("Gói tải vượt giới hạn 400 MB."));
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    if (received/1048576 != lastMb)
                    {
                        lastMb = received/1048576;
                        progress.Report(total > 0 ? L.F($"Đang tải FFmpeg: {received*100/total}% ({lastMb} MB)") : L.F($"Đang tải FFmpeg: {lastMb} MB"));
                    }
                }
            }
            progress.Report(L.T("Kiểm tra SHA-256 và giải nén…"));
            var extracted = Path.Combine(staging, "package");
            var exe = await ExtractVerifiedAsync(archive, expected, extracted, token);
            progress.Report(L.T("Kiểm tra FFmpeg vừa cài…"));
            await VerifyAsync(exe, token);
            var destination = Path.Combine(Path.GetFullPath(toolsDirectory), "ffmpeg-"+Guid.NewGuid().ToString("N"));
            var relativeExe = Path.GetRelativePath(extracted, exe);
            token.ThrowIfCancellationRequested();
            Directory.Move(extracted, destination);
            return Path.Combine(destination, relativeExe);
        }
        finally { try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    public static async Task<string> ExtractVerifiedAsync(string archive, string expectedHash, string destination, CancellationToken token)
    {
        var expected = Regex.Match(expectedHash.Trim(), @"\A[0-9a-fA-F]{64}(?=\s|$)").Value;
        await using (var input = File.OpenRead(archive))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException(L.T("SHA-256 không khớp. Hãy tải lại FFmpeg."));
        }
        var root = Path.GetFullPath(destination)+Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 2000 || zip.Entries.Sum(e => e.Length) > 1024L*1024*1024) throw new IOException(L.T("Gói FFmpeg không hợp lệ."));
        // Validate every path before writing anything, including drive paths and ADS.
        foreach (var entry in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (entry.FullName.Contains(':') || !target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException(L.T("Đường dẫn trong gói tải không hợp lệ."));
        }
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew);
            await input.CopyToAsync(output, token);
        }
        var executables = Directory.GetFiles(root, "ffmpeg.exe", SearchOption.AllDirectories);
        if (executables.Length != 1) throw new IOException(L.T("Không tìm thấy duy nhất ffmpeg.exe trong gói tải."));
        return executables[0];
    }

    static async Task VerifyAsync(string path, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process { StartInfo = new(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.ArgumentList.Add("-version"); process.Start();
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(timeout.Token);
        var version = await output; await error;
        if (process.ExitCode != 0 || !version.StartsWith("ffmpeg version")) throw new IOException(L.T("FFmpeg tải về không chạy được trên máy này."));
    }
}
