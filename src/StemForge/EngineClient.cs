using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace StemForge;

public sealed class EngineClient
{
    public async Task<JsonElement> RunAsync(object request, Settings settings, Action<string> log, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(settings.Python)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "engine", "engine.py"));
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["SAF_LANGUAGE"] = L.Language;
        start.Environment["SAF_USER_LANGUAGES"] = L.UserCatalogDirectory;
        if (!string.IsNullOrWhiteSpace(settings.Ffmpeg)) start.Environment["STEMFORGE_FFMPEG"] = settings.Ffmpeg;
        using var process = new Process { StartInfo = start };
        process.Start();
        using var registration = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Settings.JsonOptions).Replace("\r", "").Replace("\n", ""));
        process.StandardInput.Close();
        string? error = null;
        JsonElement? result = null;
        var stderr = Task.Run(async () =>
        {
            var tail = new StringBuilder();
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                if (tail.Length > 5000) tail.Remove(0, tail.Length - 3000);
                tail.AppendLine(line);
                if (!string.IsNullOrWhiteSpace(line)) log(line);
            }
            return tail.ToString();
        });
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var item = doc.RootElement;
                if (!item.TryGetProperty("type", out var eventType)) { log(line); continue; }
                switch (eventType.GetString())
                {
                    case "progress": log(item.GetProperty("message").GetString() ?? ""); break;
                    case "result": result = item.Clone(); break;
                    case "error": error = item.GetProperty("message").GetString(); break;
                }
            }
            catch (JsonException) { log(line); }
        }
        await process.WaitForExitAsync(CancellationToken.None);
        var detail = await stderr;
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0 || result is null)
            throw new InvalidOperationException(error ?? (string.IsNullOrWhiteSpace(detail) ? L.T("Audio engine kết thúc mà không trả kết quả.") : detail));
        return result.Value;
    }
}
