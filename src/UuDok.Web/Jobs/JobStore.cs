using Microsoft.Data.Sqlite;

namespace UuDok.Web.Jobs;

public sealed record JobRow(string Id, DateTimeOffset Created, DateTimeOffset Updated, string Status, string? Stage, int Percent,
    string? Detail, string DisplayName, string Kind, string Options, string? Error, string? ErrorCode);

public static class JobStatus
{
    public const string Queued = "queued", Running = "running", Done = "done", Failed = "failed", Rejected = "rejected", Cancelled = "cancelled", Reviewing = "reviewing";
}

/// <summary>
/// Jobbdata i SQLite: status, fremdrift og innstillinger. Dokumentinnhold og nøkler lagres aldri her.
/// Filnavnet lagres for å kunne navngi nedlastingen; det logges ikke.
/// </summary>
public sealed class JobStore
{
    private readonly string _cs;

    public JobStore(string dbPath)
    {
        _cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
        using var c = Open();
        Exec(c, """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS jobs(
              id TEXT PRIMARY KEY, created TEXT NOT NULL, updated TEXT NOT NULL, status TEXT NOT NULL, stage TEXT, percent INTEGER NOT NULL DEFAULT 0,
              detail TEXT, display_name TEXT NOT NULL, kind TEXT NOT NULL, options TEXT NOT NULL, error TEXT, error_code TEXT);
            CREATE INDEX IF NOT EXISTS ix_jobs_created ON jobs(created);
            """);
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_cs); c.Open(); return c; }
    private static void Exec(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    public void Insert(JobRow j)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO jobs(id,created,updated,status,stage,percent,detail,display_name,kind,options,error,error_code) VALUES($id,$c,$u,$s,$st,$p,$d,$n,$k,$o,$e,$ec)";
        Bind(cmd, j); cmd.ExecuteNonQuery();
    }

    public void Update(string id, string status, string? stage = null, int? percent = null, string? detail = null, string? error = null, string? errorCode = null)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE jobs SET status=$s, stage=COALESCE($st,stage), percent=COALESCE($p,percent), detail=$d, error=$e, error_code=$ec, updated=$u WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$st", (object?)stage ?? DBNull.Value); cmd.Parameters.AddWithValue("$p", (object?)percent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$d", (object?)detail ?? DBNull.Value); cmd.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ec", (object?)errorCode ?? DBNull.Value); cmd.Parameters.AddWithValue("$u", DateTimeOffset.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public JobRow? Get(string id) => Query("SELECT * FROM jobs WHERE id=$id", ("$id", id)).FirstOrDefault();
    public List<JobRow> List(int max = 200) => Query($"SELECT * FROM jobs ORDER BY created DESC LIMIT {max}");
    public List<JobRow> WithStatus(params string[] statuses) =>
        Query($"SELECT * FROM jobs WHERE status IN ({string.Join(",", statuses.Select((_, i) => "$s" + i))}) ORDER BY created", statuses.Select((s, i) => ("$s" + i, s)).ToArray());
    public List<JobRow> OlderThan(DateTimeOffset t) => Query("SELECT * FROM jobs WHERE created < $t", ("$t", t.ToString("O")));

    public void Delete(string id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM jobs WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery();
    }

    private List<JobRow> Query(string sql, params (string, string)[] ps)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        using var r = cmd.ExecuteReader();
        var res = new List<JobRow>();
        while (r.Read())
            res.Add(new JobRow(r.GetString(0), DateTimeOffset.Parse(r.GetString(1)), DateTimeOffset.Parse(r.GetString(2)), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6), r.GetString(7), r.GetString(8),
                r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11)));
        return res;
    }

    private static void Bind(SqliteCommand cmd, JobRow j)
    {
        cmd.Parameters.AddWithValue("$id", j.Id); cmd.Parameters.AddWithValue("$c", j.Created.ToString("O")); cmd.Parameters.AddWithValue("$u", j.Updated.ToString("O"));
        cmd.Parameters.AddWithValue("$s", j.Status); cmd.Parameters.AddWithValue("$st", (object?)j.Stage ?? DBNull.Value); cmd.Parameters.AddWithValue("$p", j.Percent);
        cmd.Parameters.AddWithValue("$d", (object?)j.Detail ?? DBNull.Value); cmd.Parameters.AddWithValue("$n", j.DisplayName); cmd.Parameters.AddWithValue("$k", j.Kind);
        cmd.Parameters.AddWithValue("$o", j.Options); cmd.Parameters.AddWithValue("$e", (object?)j.Error ?? DBNull.Value); cmd.Parameters.AddWithValue("$ec", (object?)j.ErrorCode ?? DBNull.Value);
    }
}
