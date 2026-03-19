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
const isRunningInDocker = process.platform === "linux" && fs.existsSync("/.dockerenv");

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

function isLoopbackHost(host) {
  const normalized = (host || "").trim().toLowerCase();
  return !normalized || normalized === "localhost" || normalized === "127.0.0.1" || normalized === "::1";
}

function withDockerHostFallback(config) {
  if (!isRunningInDocker || !config) {
    return config;
  }

  if (config.connectionString) {
    try {
      const url = new URL(config.connectionString);
      if (isLoopbackHost(url.hostname)) {
        url.hostname = "host.docker.internal";
        return {
          ...config,
          connectionString: url.toString()
        };
      }
    } catch {
      return config;
    }

    return config;
  }

  if (!isLoopbackHost(config.host)) {
    return config;
  }

  return {
    ...config,
    host: "host.docker.internal"
  };
}

const pool = new Pool(withDockerHostFallback(parseConnectionValue(rawConnectionValue)));

const app = express();
app.use(cors());
app.use(express.json());
app.use("/api", (_req, res, next) => {
  res.set({
    "Cache-Control": "no-store, no-cache, must-revalidate, proxy-revalidate",
    Pragma: "no-cache",
    Expires: "0",
    "Surrogate-Control": "no-store"
  });
  next();
});

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

function normalizeTimeline(rows) {
  if (!Array.isArray(rows)) {
    return [];
  }

  return rows
    .map((row) => {
      const date = new Date(row?.day ?? "");
      if (Number.isNaN(date.getTime())) {
        return null;
      }

      return {
        day: date.toISOString(),
        total: Number(row?.total || 0),
        sucesso: Number(row?.sucesso || 0),
        indisponiveis: Number(row?.indisponiveis || 0),
        pendentes: Number(row?.pendentes || 0)
      };
    })
    .filter(Boolean)
    .sort((a, b) => new Date(a.day).getTime() - new Date(b.day).getTime());
}

async function queryRows(sqlText, values = []) {
  const { rows } = await pool.query(sqlText, values);
  return rows;
}

function isCrawlerRunning() {
  if (process.platform !== "win32") {
    return false;
  }

  try {
    const out = execSync('tasklist /FI "IMAGENAME eq WebCrawler.exe" /NH /FO CSV', { timeout: 5000, windowsHide: true }).toString();
    return out.toLowerCase().includes("webcrawler.exe");
  } catch {
    return false;
  }
}

function normalizeCrawlerState(value) {
  const normalized = (value || "").trim().toLowerCase();
  if (normalized === "waiting") {
    return "waiting";
  }

  if (normalized === "running") {
    return "running";
  }

  return null;
}

async function readCrawlerHeartbeat() {
  try {
    const rows = await queryRows(`
      SELECT state, detail, is_running, process_id, host_name, started_at, last_heartbeat, updated_at
      FROM crawler_runtime_status
      WHERE instance_name = 'default'
      LIMIT 1;
    `);

    return rows[0] ?? null;
  } catch {
    return null;
  }
}

app.get("/api/crawler-status", async (_req, res) => {
  try {
    const tasklistRunning = isCrawlerRunning();

    let lastActivity = null;
    try {
      const rows = await queryRows(`
        WITH activity_events AS (
          SELECT MAX(criado_em) AS happened_at FROM candidatura_etapas
          UNION ALL
          SELECT MAX(data_insercao) AS happened_at FROM vagas
          UNION ALL
          SELECT MAX(last_heartbeat) AS happened_at
          FROM crawler_runtime_status
          WHERE instance_name = 'default'
        )
        SELECT MAX(happened_at) AS last_activity
        FROM activity_events;
      `);
      lastActivity = rows[0]?.last_activity ?? null;
    } catch { /* banco pode estar inacessivel */ }

    const heartbeat = await readCrawlerHeartbeat();

    const minutesSince = lastActivity
      ? (Date.now() - new Date(lastActivity).getTime()) / 60000
      : null;

    const heartbeatState = normalizeCrawlerState(heartbeat?.state);
    const heartbeatMinutesSince = heartbeat?.last_heartbeat
      ? (Date.now() - new Date(heartbeat.last_heartbeat).getTime()) / 60000
      : null;

    const heartbeatFresh = heartbeatMinutesSince !== null && heartbeatMinutesSince <= 2.5;

    let state;
    if (heartbeatFresh && heartbeatState) {
      state = heartbeatState;
    } else if (tasklistRunning) {
      state = minutesSince !== null && minutesSince <= 3 ? "running" : "waiting";
    } else if (minutesSince !== null && minutesSince <= 3) {
      state = "running";
    } else if (minutesSince !== null && minutesSince <= 60) {
      state = "waiting";
    } else {
      state = "offline";
    }

    res.json({
      state,
      running: tasklistRunning || heartbeatFresh,
      lastActivity,
      minutesSinceActivity: minutesSince !== null ? Math.round(minutesSince) : null,
      heartbeatState,
      heartbeatDetail: heartbeat?.detail ?? null,
      heartbeatAt: heartbeat?.last_heartbeat ?? null,
      minutesSinceHeartbeat: heartbeatMinutesSince !== null ? Math.round(heartbeatMinutesSince) : null
    });
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
    const [summaryRows, successfulJobs, unavailableJobs, pendingConfirmation, recentSteps, timelineRows] = await Promise.all([
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
      `),
      queryRows(`
        WITH bounds AS (
          SELECT
            COALESCE(DATE_TRUNC('day', MIN(data_insercao)), DATE_TRUNC('day', NOW()))::date AS first_day,
            DATE_TRUNC('day', NOW())::date AS last_day
          FROM vagas
        ),
        series AS (
          SELECT generate_series(
            GREATEST((SELECT first_day FROM bounds), ((SELECT last_day FROM bounds) - INTERVAL '59 day')::date),
            (SELECT last_day FROM bounds),
            INTERVAL '1 day'
          )::date AS day
        )
        SELECT
          series.day,
          (
            SELECT COUNT(*)
            FROM vagas
            WHERE DATE(data_insercao) <= series.day
          ) AS total,
          (
            SELECT COUNT(*)
            FROM vagas
            WHERE candidatura_enviada_sucesso = TRUE
              AND DATE(COALESCE(data_envio_sucesso, data_candidatura, data_insercao)) <= series.day
          ) AS sucesso,
          (
            SELECT COUNT(*)
            FROM vagas
            WHERE candidatura_indisponivel = TRUE
              AND DATE(COALESCE(data_indisponibilidade, data_candidatura, data_insercao)) <= series.day
          ) AS indisponiveis,
          (
            SELECT COUNT(*)
            FROM vagas
            WHERE DATE(data_insercao) <= series.day
              AND NOT candidatura_indisponivel
              AND (
                NOT candidatura_enviada_sucesso
                OR DATE(COALESCE(data_envio_sucesso, data_candidatura, data_insercao)) > series.day
              )
          ) AS pendentes
        FROM series
        ORDER BY series.day;
      `)
    ]);

    res.json({
      generatedAt: new Date().toISOString(),
      summary: normalizeSummary(summaryRows[0]),
      successfulJobs,
      unavailableJobs,
      pendingConfirmation,
      recentSteps,
      timeline: normalizeTimeline(timelineRows)
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
