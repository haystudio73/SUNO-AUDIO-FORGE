using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace StemForge;

/// <summary>Source-key catalogs (gettext style). Never translates user content or machine IDs.</summary>
public static class L
{
    public sealed record LanguageInfo(string Code, string Name)
    {
        public override string ToString() => Name;
    }
    public sealed class Catalog
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public Dictionary<string, string> Strings { get; set; } = new();
    }
    static Dictionary<string, string> strings = new();
    public static string Language { get; private set; } = "vi";
    public static string CatalogDirectory => Path.Combine(AppContext.BaseDirectory, "Languages");
    // A writable override directory remains available when installed under Program Files.
    public static string UserCatalogDirectory => Path.Combine(Settings.DirectoryPath, "Languages");
    static Catalog? Read(Stream stream)
    {
        try { return JsonSerializer.Deserialize<Catalog>(stream, Settings.JsonOptions); }
        catch (JsonException) { return null; }
    }
    static Dictionary<string, Catalog> Catalogs()
    {
        var catalogs = new Dictionary<string, Catalog>(StringComparer.OrdinalIgnoreCase);
        foreach (string code in new[] { "vi", "en" })
        {
            using var stream = typeof(L).Assembly.GetManifestResourceStream($"StemForge.Languages.{code}.json");
            if (stream is not null && Read(stream) is { } item) catalogs[code] = item;
        }
        foreach (string folder in new[] { CatalogDirectory, UserCatalogDirectory })
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                foreach (string path in Directory.EnumerateFiles(folder, "*.json").Order())
                {
                    try
                    {
                        if (new FileInfo(path).Length > 4_000_000) continue;
                        using var stream = File.OpenRead(path);
                        var item = Read(stream);
                        if (item is null || string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.Name) || item.Strings is null) continue;
                        if (catalogs.TryGetValue(item.Code, out var existing))
                        {
                            foreach (var entry in item.Strings) if (!string.IsNullOrEmpty(entry.Value)) existing.Strings[entry.Key] = entry.Value;
                            existing.Name = item.Name;
                        }
                        else catalogs[item.Code] = item;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return catalogs;
    }
    public static IReadOnlyList<LanguageInfo> AvailableLanguages() => Catalogs().Values
        .OrderBy(c => c.Code == "vi" ? 0 : c.Code == "en" ? 1 : 2).ThenBy(c => c.Name)
        .Select(c => new LanguageInfo(c.Code, c.Name)).ToArray();
    public static string ResourceKey(string source) => "i18n." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
    public static void Initialize(string code)
    {
        var catalogs = Catalogs();
        Language = !string.IsNullOrWhiteSpace(code) && catalogs.ContainsKey(code) ? code : "vi";
        strings = new(catalogs["vi"].Strings);
        foreach (var entry in catalogs[Language].Strings) if (!string.IsNullOrEmpty(entry.Value)) strings[entry.Key] = entry.Value;
        if (Application.Current is { } app)
            foreach (var entry in strings) app.Resources[ResourceKey(entry.Key)] = entry.Value;
    }
    public static string T(string source) => strings.GetValueOrDefault(source, source);
    public static string F(FormattableString value)
    {
        try { return string.Format(CultureInfo.CurrentCulture, T(value.Format), value.GetArguments()); }
        catch (FormatException) { return value.ToString(CultureInfo.CurrentCulture); }
    }
}
