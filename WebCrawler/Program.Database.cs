using System;
using System.Collections.Generic;
using Npgsql;

partial class Program
{
    private static void EnsureSuccessTrackingColumns(NpgsqlConnection conn)
    {
        using var cmd = new NpgsqlCommand(@"
            ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada_sucesso BOOLEAN DEFAULT FALSE;
            ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_envio_sucesso TIMESTAMP NULL;
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureUnavailableTrackingColumns(NpgsqlConnection conn)
    {
        using var cmd = new NpgsqlCommand(@"
            ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_indisponivel BOOLEAN DEFAULT FALSE;
            ALTER TABLE vagas ADD COLUMN IF NOT EXISTS motivo_indisponibilidade TEXT NULL;
            ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_indisponibilidade TIMESTAMP NULL;
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureApplicationTrackingSchema(NpgsqlConnection conn)
    {
        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS candidatura_etapas (
                id BIGSERIAL PRIMARY KEY,
                link TEXT NOT NULL,
                etapa TEXT NOT NULL,
                sucesso BOOLEAN NOT NULL,
                detalhe TEXT NULL,
                html_path TEXT NULL,
                screenshot_path TEXT NULL,
                criado_em TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_candidatura_etapas_link_criado_em
            ON candidatura_etapas (link, criado_em DESC);
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void LogApplicationStep(
        string link,
        string etapa,
        bool sucesso,
        string? detalhe = null,
        string? htmlPath = null,
        string? screenshotPath = null)
    {
        if (!DatabaseEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(etapa))
        {
            return;
        }

        try
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();
            EnsureApplicationTrackingSchema(conn);

            using var cmd = new NpgsqlCommand(
                @"INSERT INTO candidatura_etapas (link, etapa, sucesso, detalhe, html_path, screenshot_path)
                  VALUES (@link, @etapa, @sucesso, @detalhe, @html_path, @screenshot_path)",
                conn);

            cmd.Parameters.AddWithValue("link", link);
            cmd.Parameters.AddWithValue("etapa", etapa);
            cmd.Parameters.AddWithValue("sucesso", sucesso);
            cmd.Parameters.AddWithValue("detalhe", (object?)detalhe ?? DBNull.Value);
            cmd.Parameters.AddWithValue("html_path", (object?)htmlPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("screenshot_path", (object?)screenshotPath ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha ao registrar etapa '{etapa}' no banco: {ex.Message}");
        }
    }

    private static void SaveCollectedJobsToDatabase(List<(string Titulo, string Empresa, string Localizacao, string Link)> jobs)
    {
        try
        {
            Console.WriteLine("Salvando no PostgreSQL...");

            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();

            using (var ensureColumn = new NpgsqlCommand(
                "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_simplificada BOOLEAN;", conn))
            {
                ensureColumn.ExecuteNonQuery();
            }

            using (var ensureAppliedColumn = new NpgsqlCommand(
                "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada BOOLEAN DEFAULT FALSE;", conn))
            {
                ensureAppliedColumn.ExecuteNonQuery();
            }

            using (var ensureAppliedAtColumn = new NpgsqlCommand(
                "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_candidatura TIMESTAMP NULL;", conn))
            {
                ensureAppliedAtColumn.ExecuteNonQuery();
            }

            EnsureSuccessTrackingColumns(conn);

            EnsureUnavailableTrackingColumns(conn);

            EnsureApplicationTrackingSchema(conn);

            foreach (var job in jobs)
            {
                var normalizedLink = NormalizeLinkedInJobLink(job.Link);
                if (string.IsNullOrWhiteSpace(normalizedLink))
                {
                    continue;
                }

                using var cmd = new NpgsqlCommand(
                    "INSERT INTO vagas (titulo, empresa, localizacao, link, data_insercao, candidatura_simplificada, candidatura_enviada, candidatura_enviada_sucesso) VALUES (@titulo, @empresa, @localizacao, @link, @data_insercao, @candidatura_simplificada, @candidatura_enviada, @candidatura_enviada_sucesso) ON CONFLICT (link) DO NOTHING", conn);
                cmd.Parameters.AddWithValue("titulo", job.Titulo);
                cmd.Parameters.AddWithValue("empresa", job.Empresa);
                cmd.Parameters.AddWithValue("localizacao", job.Localizacao);
                cmd.Parameters.AddWithValue("link", normalizedLink);
                cmd.Parameters.AddWithValue("data_insercao", DateTime.Now);
                cmd.Parameters.AddWithValue("candidatura_simplificada", true);
                cmd.Parameters.AddWithValue("candidatura_enviada", false);
                cmd.Parameters.AddWithValue("candidatura_enviada_sucesso", false);
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine("Vagas salvas no banco PostgreSQL (sem duplicidade)!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao salvar no banco: {ex.Message}");
        }
    }

    private static List<string> GetSimplifiedJobLinksFromDatabase()
    {
        var links = new List<string>();
        var seenLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();

        using (var ensureAppliedColumn = new NpgsqlCommand(
            "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada BOOLEAN DEFAULT FALSE;",
            conn))
        {
            ensureAppliedColumn.ExecuteNonQuery();
        }

        using (var ensureAppliedAtColumn = new NpgsqlCommand(
            "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_candidatura TIMESTAMP NULL;",
            conn))
        {
            ensureAppliedAtColumn.ExecuteNonQuery();
        }

        EnsureUnavailableTrackingColumns(conn);
        EnsureSuccessTrackingColumns(conn);

        EnsureApplicationTrackingSchema(conn);

        using var cmd = new NpgsqlCommand(
            "SELECT DISTINCT link FROM vagas WHERE candidatura_simplificada = TRUE AND COALESCE(candidatura_enviada, FALSE) = FALSE AND COALESCE(candidatura_indisponivel, FALSE) = FALSE AND link IS NOT NULL AND link <> ''",
            conn);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var rawLink = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(rawLink))
            {
                continue;
            }

            if (IsBudgetExhaustedJobLink(rawLink))
            {
                continue;
            }

            var normalizedLink = NormalizeLinkedInJobLink(rawLink);
            if (string.IsNullOrWhiteSpace(normalizedLink))
            {
                continue;
            }

            if (seenLinks.Add(normalizedLink))
            {
                links.Add(normalizedLink);
            }
        }

        return links;
    }

    private static void MarkJobAsApplied(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        var normalizedLink = NormalizeLinkedInJobLink(link);
        if (string.IsNullOrWhiteSpace(normalizedLink))
        {
            normalizedLink = link;
        }

        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();
        EnsureUnavailableTrackingColumns(conn);
        EnsureSuccessTrackingColumns(conn);

        using var cmd = new NpgsqlCommand(
            @"UPDATE vagas
              SET candidatura_enviada = TRUE,
                  data_candidatura = @data_candidatura,
                  candidatura_enviada_sucesso = TRUE,
                  data_envio_sucesso = @data_candidatura,
                  candidatura_indisponivel = FALSE,
                  motivo_indisponibilidade = NULL,
                  data_indisponibilidade = NULL
              WHERE link = @link
                 OR link = @normalized_link
                 OR split_part(link, '?', 1) = split_part(@link, '?', 1)
                 OR split_part(link, '?', 1) = split_part(@normalized_link, '?', 1)",
            conn);
        cmd.Parameters.AddWithValue("data_candidatura", DateTime.Now);
        cmd.Parameters.AddWithValue("link", link);
        cmd.Parameters.AddWithValue("normalized_link", normalizedLink);
        cmd.ExecuteNonQuery();
    }

    private static void MarkJobAsUnavailable(string link, string reason)
    {
        if (!DatabaseEnabled || string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        var normalizedLink = NormalizeLinkedInJobLink(link);
        if (string.IsNullOrWhiteSpace(normalizedLink))
        {
            normalizedLink = link;
        }

        try
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();
            EnsureUnavailableTrackingColumns(conn);

            using var cmd = new NpgsqlCommand(
                @"UPDATE vagas
                  SET candidatura_indisponivel = TRUE,
                      motivo_indisponibilidade = @motivo,
                      data_indisponibilidade = @data_indisponibilidade
                  WHERE link = @link
                     OR link = @normalized_link
                     OR split_part(link, '?', 1) = split_part(@link, '?', 1)
                     OR split_part(link, '?', 1) = split_part(@normalized_link, '?', 1)",
                conn);
            cmd.Parameters.AddWithValue("motivo", string.IsNullOrWhiteSpace(reason) ? "indisponivel" : reason);
            cmd.Parameters.AddWithValue("data_indisponibilidade", DateTime.Now);
            cmd.Parameters.AddWithValue("link", link);
            cmd.Parameters.AddWithValue("normalized_link", normalizedLink);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha ao marcar vaga como indisponível: {ex.Message}");
        }
    }
}
