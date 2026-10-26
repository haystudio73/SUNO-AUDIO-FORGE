using Microsoft.Data.Sqlite;
using System.IO;
using System.Text.Json;

namespace StemForge;

public sealed record SessionEntry(string Source, string Name, string UpdatedUtc, string Json)
{
    public string UpdatedLocal => DateTimeOffset.Parse(UpdatedUtc).ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public bool Available => File.Exists(Source);
    public string Availability => Available ? L.T("Có audio") : L.T("Audio đã di chuyển / bị thiếu");
}
public sealed record HistoryEntry(long Id, string Kind, string Name, string Source, string Output, string Detail, string CreatedUtc)
{
    public string CreatedLocal => DateTimeOffset.Parse(CreatedUtc).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
}

/// <summary>Short-lived, parameterized SQLite transactions. Audio remains on disk.</summary>
public sealed class AppStore
{
    static readonly Lazy<AppStore> defaultStore = new(() => new(Path.Combine(Settings.DirectoryPath, "stemforge.db")));
    public static AppStore Default => defaultStore.Value;
    public string DatabasePath { get; }
    readonly string connectionString;

    public AppStore(string path)
    {
        SQLitePCL.Batteries_V2.Init();
        DatabasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5 }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS app_settings (key TEXT PRIMARY KEY, value_json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions (
                source TEXT PRIMARY KEY COLLATE NOCASE, name TEXT NOT NULL,
                project_json TEXT NOT NULL, updated_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS history (
                id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, name TEXT NOT NULL,
                source TEXT NOT NULL, output TEXT NOT NULL, detail TEXT NOT NULL, created_utc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS history_created ON history(id DESC);
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
    }
    SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
    public Settings LoadSettings()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value_json FROM app_settings WHERE key='application'";
        if (command.ExecuteScalar() is string json)
        {
            transaction.Commit();
            return JsonSerializer.Deserialize<Settings>(json, Settings.JsonOptions) ?? new();
        }
        // Import the previous format only once. Preserve the legacy file as backup.
        var legacy = Path.Combine(Path.GetDirectoryName(DatabasePath)!, "settings.json");
        var settings = File.Exists(legacy)
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(legacy), Settings.JsonOptions) ?? new()
            : new Settings();
        command.CommandText = "INSERT INTO app_settings(key,value_json) VALUES('application',$json)";
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings, Settings.JsonOptions));
        command.ExecuteNonQuery(); transaction.Commit();
        return settings;
    }
    public void SaveSettings(Settings settings)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO app_settings(key,value_json) VALUES('application',$json) ON CONFLICT(key) DO UPDATE SET value_json=excluded.value_json";
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings, Settings.JsonOptions));
        command.ExecuteNonQuery();
    }
    public void SaveSession(Project project, string? kind = null, string output = "", string detail = "")
    {
        if (string.IsNullOrWhiteSpace(project.Source)) return;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sessions(source,name,project_json,updated_utc) VALUES($source,$name,$json,$time)
            ON CONFLICT(source) DO UPDATE SET name=excluded.name,project_json=excluded.project_json,updated_utc=excluded.updated_utc
            """;
        command.Parameters.AddWithValue("$source", project.Source);
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(project, Settings.JsonOptions));
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        if (kind is not null) AddEvent(connection, transaction, kind, project.Name, project.Source, output, detail);
        transaction.Commit();
    }
    public void Record(string kind, Project project, string output = "", string detail = "")
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        AddEvent(connection, transaction, kind, project.Name, project.Source, output, detail);
        transaction.Commit();
    }
    static void AddEvent(SqliteConnection connection, SqliteTransaction transaction, string kind, string name, string source, string output, string detail)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO history(kind,name,source,output,detail,created_utc) VALUES($kind,$name,$source,$output,$detail,$time)";
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$output", output);
        command.Parameters.AddWithValue("$detail", detail);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
    public List<SessionEntry> Sessions()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source,name,updated_utc,project_json FROM sessions ORDER BY updated_utc DESC LIMIT 200";
        using var reader = command.ExecuteReader();
        List<SessionEntry> result = [];
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return result;
    }
    public Project? LoadSession(string source)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT project_json FROM sessions WHERE source=$source";
        command.Parameters.AddWithValue("$source", source);
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<Project>(json, Settings.JsonOptions) : null;
    }
    public List<HistoryEntry> History()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,kind,name,source,output,detail,created_utc FROM history ORDER BY id DESC LIMIT 500";
        using var reader = command.ExecuteReader();
        List<HistoryEntry> result = [];
        while (reader.Read()) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
        return result;
    }
}
