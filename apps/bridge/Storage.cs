using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Booth;
public sealed class Storage
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string Root
    {
        get;
    }
    readonly string connection;
    readonly object saveGate = new();
    public Storage()
    {
        Root = Path.GetFullPath(Environment.GetEnvironmentVariable("PHOTOBOOTH_DATA_DIR") ?? Path.Combine(AppContext.BaseDirectory, "../../../../../data"));
        Directory.CreateDirectory(Root);
        foreach (var name in new[] { "templates", "sessions", "originals", "finals" })
            Directory.CreateDirectory(Path.Combine(Root, name));
        connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "booth.db"), DefaultTimeout = 10 }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS sessions(id TEXT PRIMARY KEY, json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS config(id TEXT PRIMARY KEY,json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS prints(id INTEGER PRIMARY KEY, sessionId TEXT, printer TEXT, time TEXT, copies INTEGER, status TEXT, error TEXT);";
        cmd.ExecuteNonQuery();
    }
    SqliteConnection Open()
    {
        var c = new SqliteConnection(connection);
        c.Open();
        return c;
    }
    public static void Atomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void WriteJson<T>(string path, T data) => Atomic(path, JsonSerializer.SerializeToUtf8Bytes(data, Json));
    public string SessionDir(Session s) => Path.Combine(Root, "sessions", s.CreatedAt.ToString("yyyy-MM-dd"), s.Id);
    public void Save(Session s)
    {
        lock (saveGate)
        {
            var persisted = FindOrNull(s.Id);
            if (persisted?.UploadStatus == "uploaded")
            {
                s.UploadStatus = persisted.UploadStatus;
                s.ShareUrl = persisted.ShareUrl;
            }
            WriteJson(Path.Combine(SessionDir(s), "session.json"), s);
            Put("sessions", s.Id, s);
        }
    }
    public void UpdateUpload(string id, string status, string? url)
    {
        lock (saveGate)
        {
            var latest = Find(id);
            latest.UploadStatus = status;
            latest.ShareUrl = url;
            Save(latest);
        }
    }
    public void RecoverPrintJobs()
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE prints SET status='uncertain',error='Bridge restarted during print submission' WHERE status='sending'";
        cmd.ExecuteNonQuery();
    }
    public void ResolvePrintJobs(string sessionId, string status)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE prints SET status=$status,error='Operator checked spooler and resolved uncertain outcome' WHERE sessionId=$id AND status='uncertain'";
        cmd.Parameters.AddWithValue("$id", sessionId); cmd.Parameters.AddWithValue("$status", status); cmd.ExecuteNonQuery();
    }
    void Put<T>(string table, string id, T value)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"INSERT INTO {table}(id,json) VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=$json";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(value, Json));
        cmd.ExecuteNonQuery();
    }
    public List<Session> Sessions()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT json FROM sessions ORDER BY rowid DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<Session>();
        while (r.Read())
            list.Add(JsonSerializer.Deserialize<Session>(r.GetString(0), Json)!);
        return list;
    }
    public Session Find(string id) => FindOrNull(id) ?? throw new KeyNotFoundException("Session not found");
    Session? FindOrNull(string id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT json FROM sessions WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<Session>(json, Json) : null;
    }
    public Settings LoadSettings()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT json FROM config WHERE id='settings'";
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<Settings>(json, Json)! : new Settings();
    }
    public void SaveSettings(Settings settings)
    {
        settings.Validate();
        Put("config", "settings", settings);
    }
    public long BeginPrint(Session s, Settings settings)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO prints(sessionId,printer,time,copies,status) VALUES($s,$p,$t,$c,'sending'); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$s", s.Id);
        cmd.Parameters.AddWithValue("$p", settings.Printer);
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$c", settings.Copies);
        return (long)cmd.ExecuteScalar()!;
    }
    public void EndPrint(long id, string status, string? error)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE prints SET status=$s,error=$e WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    public List<object> PrintHistory()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT sessionId,printer,time,copies,status,error FROM prints ORDER BY id DESC LIMIT 500";
        using var r = cmd.ExecuteReader();
        var rows = new List<object>();
        while (r.Read())
            rows.Add(new
            {
                sessionId = r.GetString(0),
                printer = r.GetString(1),
                time = r.GetString(2),
                copies = r.GetInt32(3),
                status = r.GetString(4),
                error = r.IsDBNull(5) ? null : r.GetString(5)
            });
        return rows;
    }
}
