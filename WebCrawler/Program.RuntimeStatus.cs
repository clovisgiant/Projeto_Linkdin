using System;
using System.Threading;
using Npgsql;

partial class Program
{
    private const string RuntimeStatusInstanceName = "default";
    private static readonly TimeSpan RuntimeHeartbeatPulseInterval = TimeSpan.FromMinutes(1);

    private static void EnsureCrawlerRuntimeStatusSchema(NpgsqlConnection conn)
    {
        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS crawler_runtime_status (
                instance_name TEXT PRIMARY KEY,
                state TEXT NOT NULL,
                detail TEXT NULL,
                is_running BOOLEAN NOT NULL DEFAULT TRUE,
                process_id INTEGER NULL,
                host_name TEXT NULL,
                started_at TIMESTAMP NOT NULL DEFAULT NOW(),
                last_heartbeat TIMESTAMP NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void UpdateCrawlerRuntimeStatus(string state, string? detail = null, bool isRunning = true)
    {
        if (!DatabaseEnabled || string.IsNullOrWhiteSpace(state))
        {
            return;
        }

        try
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();
            EnsureCrawlerRuntimeStatusSchema(conn);

            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            using var cmd = new NpgsqlCommand(@"
                INSERT INTO crawler_runtime_status (
                    instance_name,
                    state,
                    detail,
                    is_running,
                    process_id,
                    host_name,
                    started_at,
                    last_heartbeat,
                    updated_at
                )
                VALUES (
                    @instance_name,
                    @state,
                    @detail,
                    @is_running,
                    @process_id,
                    @host_name,
                    @now,
                    @now,
                    @now
                )
                ON CONFLICT (instance_name) DO UPDATE
                SET state = EXCLUDED.state,
                    detail = EXCLUDED.detail,
                    is_running = EXCLUDED.is_running,
                    process_id = EXCLUDED.process_id,
                    host_name = EXCLUDED.host_name,
                    started_at = CASE
                        WHEN crawler_runtime_status.process_id IS DISTINCT FROM EXCLUDED.process_id
                            THEN EXCLUDED.started_at
                        ELSE crawler_runtime_status.started_at
                    END,
                    last_heartbeat = EXCLUDED.last_heartbeat,
                    updated_at = EXCLUDED.updated_at;
            ", conn);

            cmd.Parameters.AddWithValue("instance_name", RuntimeStatusInstanceName);
            cmd.Parameters.AddWithValue("state", state.Trim().ToLowerInvariant());
            cmd.Parameters.AddWithValue("detail", (object?)detail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("is_running", isRunning);
            cmd.Parameters.AddWithValue("process_id", Environment.ProcessId);
            cmd.Parameters.AddWithValue("host_name", Environment.MachineName);
            cmd.Parameters.AddWithValue("now", now);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO] Falha ao atualizar heartbeat do crawler: {ex.Message}");
            PrintDatabaseAuthenticationHint(ex);
        }
    }

    private static void SleepWithRuntimeHeartbeat(TimeSpan duration, string state, string? detail = null)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var remaining = duration;
        while (remaining > TimeSpan.Zero)
        {
            UpdateCrawlerRuntimeStatus(state, detail, isRunning: true);

            var currentSlice = remaining > RuntimeHeartbeatPulseInterval
                ? RuntimeHeartbeatPulseInterval
                : remaining;

            Thread.Sleep(currentSlice);
            remaining -= currentSlice;
        }
    }
}