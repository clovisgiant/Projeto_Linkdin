import cors from "cors";
import dotenv from "dotenv";
import express from "express";
import fs from "node:fs";
import { execSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import pg from "pg";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

dotenv.config({ path: path.resolve(__dirname, "..", ".env") });
dotenv.config({ path: path.resolve(__dirname, "..", "..", ".env"), override: false });

const rawConnectionValue =
  process.env.WEBCRAWLER_DB_CONNECTION ||
  "Host=localhost;Port=5432;Database=ProjetoDb;Username=postgres;Password=2748";

const apiPort = Number(process.env.APPLINKDIN_API_PORT || process.env.PORT || 4000);
const { Pool } = pg;
const clientDistPath = path.resolve(__dirname, "..", "dist");

function parseConnectionValue(value) {
  const trimmed = (value || "").trim();
  if (!trimmed) {
    return {
      host: "localhost",
      port: 5432,
      database: "ProjetoDb",
      user: "postgres",
      password: "2748"
    };
  }

  if (trimmed.startsWith("postgres://") || trimmed.startsWith("postgresql://")) {
    return { connectionString: trimmed };
  }

  const map = {};
  for (const part of trimmed.split(";")) {
    if (!part || !part.includes("=")) {
      continue;
    }

    const [rawKey, ...rest] = part.split("=");
    const key = rawKey.trim().toLowerCase();
    const val = rest.join("=").trim();
    map[key] = val;
  }

  return {
    host: map.host || "localhost",
    port: Number(map.port || 5432),
    database: map.database || map.initialcatalog || "ProjetoDb",
    user: map.username || map.user || "postgres",
    password: map.password || "",
    ssl: map.sslmode && map.sslmode.toLowerCase() !== "disable"
  };
}

const pool = new Pool(parseConnectionValue(rawConnectionValue));

const app = express();
app.use(cors());
app.use(express.json());

function normalizeSummary(row) {
  if (!row) {
    return {
      total: 0,
      sucesso: 0,
      enviada_sem_confirmacao: 0,
      indisponiveis: 0,
      pendentes: 0
    };
  }

  return {
    total: Number(row.total || 0),
    sucesso: Number(row.sucesso || 0),
    enviada_sem_confirmacao: Number(row.enviada_sem_confirmacao || 0),
    indisponiveis: Number(row.indisponiveis || 0),
    pendentes: Number(row.pendentes || 0)
  };
}

async function queryRows(sqlText, values = []) {
  const { rows } = await pool.query(sqlText, values);
  return rows;
}

function isCrawlerRunning() {
  try {
    const out = execSync('tasklist /FI "IMAGENAME eq WebCrawler.exe" /NH /FO CSV', { timeout: 5000, windowsHide: true }).toString();
    return out.toLowerCase().includes("webcrawler.exe");
  } catch {
    return false;
  }
}

app.get("/api/crawler-status", async (_req, res) => {
  try {
    const running = isCrawlerRunning();

    let lastActivity = null;
    try {
      const rows = await queryRows(`
        SELECT GREATEST(
          (SELECT MAX(criado_em) FROM candidatura_etapas),
          (SELECT MAX(data_insercao) FROM vagas)
        ) AS last_activity;
      `);
      lastActivity = rows[0]?.last_activity ?? null;
    } catch { /* banco pode estar inacessivel */ }

    const minutesSince = lastActivity
      ? (Date.now() - new Date(lastActivity).getTime()) / 60000
      : null;

    let state;
    if (!running) {
      state = "offline";
    } else if (minutesSince === null || minutesSince > 3) {
      state = "waiting";
    } else {
      state = "running";
    }

    res.json({ state, running, lastActivity, minutesSinceActivity: minutesSince ? Math.round(minutesSince) : null });
  } catch (error) {
    res.json({ state: "offline", running: false, error: error.message });
  }
});

app.get("/api/health", async (_req, res) => {
  try {
    await queryRows("SELECT 1;");
    res.json({ ok: true, database: "connected" });
  } catch (error) {
    res.status(500).json({ ok: false, error: error.message });
  }
});

app.get("/api/dashboard", async (_req, res) => {
  try {
    const [summaryRows, successfulJobs, unavailableJobs, pendingConfirmation, recentSteps] = await Promise.all([
      queryRows(`
        SELECT
          COUNT(*) AS total,
          SUM(CASE WHEN candidatura_enviada_sucesso THEN 1 ELSE 0 END) AS sucesso,
          SUM(
            CASE
              WHEN candidatura_enviada
                AND NOT candidatura_enviada_sucesso
                AND NOT candidatura_indisponivel
              THEN 1
              ELSE 0
            END
          ) AS enviada_sem_confirmacao,
          SUM(CASE WHEN candidatura_indisponivel THEN 1 ELSE 0 END) AS indisponiveis,
          SUM(
            CASE
              WHEN NOT candidatura_enviada
                AND NOT candidatura_indisponivel
              THEN 1
              ELSE 0
            END
          ) AS pendentes
        FROM vagas;
      `),
      queryRows(`
        SELECT titulo, empresa, localizacao, link, data_envio_sucesso
        FROM vagas
        WHERE candidatura_enviada_sucesso = TRUE
        ORDER BY data_envio_sucesso DESC
        LIMIT 50;
      `),
      queryRows(`
        SELECT titulo, empresa, localizacao, link, motivo_indisponibilidade, data_indisponibilidade
        FROM vagas
        WHERE candidatura_indisponivel = TRUE
        ORDER BY data_indisponibilidade DESC
        LIMIT 50;
      `),
      queryRows(`
        SELECT titulo, empresa, localizacao, link, data_candidatura
        FROM vagas
        WHERE candidatura_enviada = TRUE
          AND candidatura_enviada_sucesso = FALSE
          AND candidatura_indisponivel = FALSE
        ORDER BY data_candidatura DESC
        LIMIT 50;
      `),
      queryRows(`
        SELECT link, etapa, sucesso, detalhe, criado_em
        FROM candidatura_etapas
        ORDER BY criado_em DESC
        LIMIT 80;
      `)
    ]);

    res.json({
      generatedAt: new Date().toISOString(),
      summary: normalizeSummary(summaryRows[0]),
      successfulJobs,
      unavailableJobs,
      pendingConfirmation,
      recentSteps
    });
  } catch (error) {
    console.error("[API] dashboard error", error);
    res.status(500).json({
      message: "Nao foi possivel carregar o dashboard no momento.",
      error: error.message
    });
  }
});

if (fs.existsSync(clientDistPath)) {
  app.use(express.static(clientDistPath));

  app.get("*", (req, res, next) => {
    if (req.path.startsWith("/api/")) {
      next();
      return;
    }

    res.sendFile(path.join(clientDistPath, "index.html"));
  });
}

const server = app.listen(apiPort, () => {
  console.log(`[API] Dashboard API online em http://localhost:${apiPort}`);
});

async function closeGracefully(signal) {
  console.log(`[API] Encerrando por ${signal}...`);
  server.close(async () => {
    await pool.end();
    process.exit(0);
  });
}

process.on("SIGINT", () => closeGracefully("SIGINT"));
process.on("SIGTERM", () => closeGracefully("SIGTERM"));
