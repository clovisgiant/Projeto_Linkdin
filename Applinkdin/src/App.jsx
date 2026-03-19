import { useEffect, useMemo, useState } from "react";
import CrawlerStatus from "./CrawlerStatus.jsx";
import "./styles.css";

const REFRESH_INTERVAL_MS = 30000;
const numberFormatter = new Intl.NumberFormat("pt-BR");
const percentFormatter = new Intl.NumberFormat("pt-BR", {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1
});
const API_FETCH_OPTIONS = {
  cache: "no-store",
  headers: {
    "Cache-Control": "no-cache",
    Pragma: "no-cache"
  }
};

const emptyDashboard = {
  generatedAt: null,
  summary: {
    total: 0,
    sucesso: 0,
    enviada_sem_confirmacao: 0,
    indisponiveis: 0,
    pendentes: 0
  },
  successfulJobs: [],
  unavailableJobs: [],
  pendingConfirmation: [],
  recentSteps: [],
  timeline: []
};

const emptyRuntime = {
  state: "offline",
  running: false,
  heartbeatState: null,
  heartbeatDetail: "",
  minutesSinceHeartbeat: null
};

function normalizeRuntimeStatus(payload) {
  if (!payload || typeof payload !== "object") {
    return emptyRuntime;
  }

  const state = String(payload.state || "").trim().toLowerCase();
  const heartbeatState = String(payload.heartbeatState || "").trim().toLowerCase();
  const rawMinutes = Number(payload.minutesSinceHeartbeat);

  return {
    state: state || "offline",
    running: Boolean(payload.running),
    heartbeatState: heartbeatState || null,
    heartbeatDetail: String(payload.heartbeatDetail || "").replace(/\s+/g, " ").trim(),
    minutesSinceHeartbeat: Number.isFinite(rawMinutes) ? rawMinutes : null
  };
}

function buildApiUrl(pathname) {
  const separator = pathname.includes("?") ? "&" : "?";
  return `${pathname}${separator}_=${Date.now()}`;
}

async function fetchJson(pathname) {
  const response = await fetch(buildApiUrl(pathname), API_FETCH_OPTIONS);

  if (!response.ok) {
    throw new Error(`Falha ao carregar ${pathname}: ${response.status}`);
  }

  return response.json();
}

function normalizeDashboard(payload) {
  if (!payload || typeof payload !== "object") {
    return emptyDashboard;
  }

  return {
    generatedAt: payload.generatedAt ?? null,
    summary: {
      ...emptyDashboard.summary,
      ...(payload.summary ?? {})
    },
    successfulJobs: Array.isArray(payload.successfulJobs) ? payload.successfulJobs : [],
    unavailableJobs: Array.isArray(payload.unavailableJobs) ? payload.unavailableJobs : [],
    pendingConfirmation: Array.isArray(payload.pendingConfirmation) ? payload.pendingConfirmation : [],
    recentSteps: Array.isArray(payload.recentSteps) ? payload.recentSteps : [],
    timeline: normalizeTimeline(payload.timeline)
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
        label: date.toLocaleDateString("pt-BR", {
          day: "2-digit",
          month: "2-digit"
        }),
        total: Number(row?.total || 0),
        sucesso: Number(row?.sucesso || 0),
        indisponiveis: Number(row?.indisponiveis || 0),
        pendentes: Number(row?.pendentes ?? (Number(row?.total || 0) - Number(row?.sucesso || 0) - Number(row?.indisponiveis || 0)))
      };
    })
    .filter(Boolean)
    .sort((a, b) => new Date(a.day).getTime() - new Date(b.day).getTime());
}

function formatDate(value) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "-";
  }

  return date.toLocaleString("pt-BR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit"
  });
}

function buildJobUrl(rawLink) {
  const text = String(rawLink || "").trim();
  if (!text) {
    return "";
  }

  if (/^https?:\/\//i.test(text)) {
    return text;
  }

  if (text.startsWith("/")) {
    return `https://www.linkedin.com${text}`;
  }

  return `https://${text}`;
}

async function copyTextToClipboard(text) {
  if (!text) {
    return false;
  }

  try {
    if (navigator?.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // Fallback abaixo para navegadores com API de clipboard restrita.
  }

  try {
    const input = document.createElement("textarea");
    input.value = text;
    input.setAttribute("readonly", "");
    input.style.position = "fixed";
    input.style.top = "-1000px";
    input.style.opacity = "0";
    document.body.appendChild(input);
    input.focus();
    input.select();
    const copied = document.execCommand("copy");
    document.body.removeChild(input);
    return copied;
  } catch {
    return false;
  }
}

function JobActionButton({ link }) {
  const targetUrl = buildJobUrl(link);
  const [copyState, setCopyState] = useState("idle");

  useEffect(() => {
    if (copyState === "idle") {
      return undefined;
    }

    const timer = setTimeout(() => setCopyState("idle"), 1800);
    return () => clearTimeout(timer);
  }, [copyState]);

  async function handleCopy() {
    const copied = await copyTextToClipboard(targetUrl);
    setCopyState(copied ? "success" : "error");
  }

  if (!targetUrl) {
    return <StatusPill kind="neutral" label="Sem link" />;
  }

  return (
    <div className="job-action-group">
      <button
        type="button"
        className="job-action-btn"
        onClick={() => window.open(targetUrl, "_blank", "noopener,noreferrer")}
      >
        Candidatar
      </button>
      <button
        type="button"
        className={`job-copy-btn ${copyState === "success" ? "is-success" : ""} ${copyState === "error" ? "is-error" : ""}`.trim()}
        onClick={handleCopy}
      >
        {copyState === "success" ? "Copiado" : copyState === "error" ? "Falha" : "Copiar link"}
      </button>
    </div>
  );
}

function StatusPill({ label, kind = "neutral" }) {
  return <span className={`pill pill-${kind}`}>{label}</span>;
}

function SummaryCard({ title, value, hint, tone }) {
  return (
    <article className={`summary-card tone-${tone}`}>
      <p className="summary-title">{title}</p>
      <strong className="summary-value">{numberFormatter.format(value)}</strong>
      <p className="summary-hint">{hint}</p>
    </article>
  );
}

function buildLinePath(points) {
  if (points.length === 0) {
    return "";
  }

  return points
    .map((point, index) => `${index === 0 ? "M" : "L"} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`)
    .join(" ");
}

function buildAreaPath(points, baselineY) {
  if (points.length === 0) {
    return "";
  }

  const firstPoint = points[0];
  const lastPoint = points[points.length - 1];
  return `${buildLinePath(points)} L ${lastPoint.x.toFixed(2)} ${baselineY.toFixed(2)} L ${firstPoint.x.toFixed(2)} ${baselineY.toFixed(2)} Z`;
}

function StatusLineChart({ points }) {
  const [period, setPeriod] = useState(14);
  const [hoveredIndex, setHoveredIndex] = useState(null);
  const periodOptions = [7, 14, 30];

  const fallbackPoint = {
    day: new Date().toISOString(),
    label: new Date().toLocaleDateString("pt-BR", {
      day: "2-digit",
      month: "2-digit"
    }),
    total: 0,
    sucesso: 0,
    indisponiveis: 0,
    pendentes: 0
  };

  const sourcePoints = Array.isArray(points) ? points : [];
  const periodPoints = sourcePoints.slice(-period);
  const chartPoints = periodPoints.length > 0 ? periodPoints : [fallbackPoint];
  const visibleCount = Math.min(period, sourcePoints.length || period);

  const snapshotValue = (index, key) => {
    if (index < 0 || index >= sourcePoints.length) {
      return 0;
    }

    return Number(sourcePoints[index]?.[key] || 0);
  };

  const growthInPeriod = (endIndex, baselineIndex, key) => {
    if (endIndex < 0) {
      return 0;
    }

    return Math.max(0, snapshotValue(endIndex, key) - snapshotValue(baselineIndex, key));
  };

  const currentEndIndex = sourcePoints.length - 1;
  const currentBaselineIndex = currentEndIndex - period;
  const previousEndIndex = currentBaselineIndex;
  const previousBaselineIndex = previousEndIndex - period;

  const comparisonItems = [
    { key: "total", label: "Total" },
    { key: "sucesso", label: "Sucesso" },
    { key: "indisponiveis", label: "Indisponiveis" },
    { key: "pendentes", label: "Pendentes" }
  ].map((metric) => {
    const currentGrowth = growthInPeriod(currentEndIndex, currentBaselineIndex, metric.key);
    const hasPrevious = previousEndIndex >= 0;
    const previousGrowth = hasPrevious ? growthInPeriod(previousEndIndex, previousBaselineIndex, metric.key) : null;

    let deltaPercent = null;
    if (hasPrevious && previousGrowth !== null) {
      if (previousGrowth === 0) {
        deltaPercent = currentGrowth === 0 ? 0 : null;
      } else {
        deltaPercent = ((currentGrowth - previousGrowth) / Math.abs(previousGrowth)) * 100;
      }
    }

    const deltaLabel = deltaPercent === null
      ? "sem base"
      : `${deltaPercent > 0 ? "+" : ""}${percentFormatter.format(deltaPercent)}%`;

    const deltaClass = deltaPercent === null
      ? "delta-na"
      : deltaPercent > 0
        ? "delta-up"
        : deltaPercent < 0
          ? "delta-down"
          : "delta-flat";

    return {
      ...metric,
      currentGrowth,
      deltaLabel,
      deltaClass
    };
  });

  useEffect(() => {
    setHoveredIndex((prev) => {
      if (prev === null) {
        return null;
      }

      return Math.min(prev, chartPoints.length - 1);
    });
  }, [chartPoints.length]);

  const chartSize = {
    width: 840,
    height: 320,
    padding: {
      top: 18,
      right: 20,
      bottom: 44,
      left: 48
    }
  };

  const drawableWidth = chartSize.width - chartSize.padding.left - chartSize.padding.right;
  const drawableHeight = chartSize.height - chartSize.padding.top - chartSize.padding.bottom;
  const maxValue = Math.max(
    1,
    ...chartPoints.flatMap((point) => [point.total, point.sucesso, point.indisponiveis, point.pendentes])
  );

  const yTicks = [1, 0.75, 0.5, 0.25, 0].map((factor) => Math.round(maxValue * factor));
  const xStep = chartPoints.length > 1 ? drawableWidth / (chartPoints.length - 1) : 0;
  const hoverBandWidth = chartPoints.length > 1 ? Math.max(16, xStep) : drawableWidth;
  const xLabelEvery = chartPoints.length <= 8 ? 1 : Math.ceil(chartPoints.length / 8);

  const valueToY = (value) => {
    const ratio = value / maxValue;
    return chartSize.padding.top + drawableHeight - ratio * drawableHeight;
  };

  const indexToX = (index) => chartSize.padding.left + index * xStep;
  const baselineY = chartSize.padding.top + drawableHeight;

  const series = [
    {
      key: "total",
      label: "Total de vagas",
      pathClass: "line-path series-total",
      pointClass: "line-point series-total-point",
      legendClass: "legend-line legend-total"
    },
    {
      key: "sucesso",
      label: "Enviadas com sucesso",
      pathClass: "line-path series-success",
      pointClass: "line-point series-success-point",
      legendClass: "legend-line legend-success"
    },
    {
      key: "indisponiveis",
      label: "Indisponiveis",
      pathClass: "line-path series-unavailable",
      pointClass: "line-point series-unavailable-point",
      legendClass: "legend-line legend-unavailable"
    },
    {
      key: "pendentes",
      label: "Pendentes",
      pathClass: "line-path series-pending",
      pointClass: "line-point series-pending-point",
      legendClass: "legend-line legend-pending"
    }
  ];

  const pointsBySeries = series.map((seriesConfig) => {
    const chartSeriesPoints = chartPoints.map((point, index) => ({
      x: indexToX(index),
      y: valueToY(Number(point[seriesConfig.key] || 0)),
      value: Number(point[seriesConfig.key] || 0)
    }));

    return {
      ...seriesConfig,
      points: chartSeriesPoints,
      lastValue: chartSeriesPoints[chartSeriesPoints.length - 1]?.value || 0
    };
  });

  const safeHoveredIndex = hoveredIndex !== null && hoveredIndex < chartPoints.length ? hoveredIndex : null;

  const hoverInfo = safeHoveredIndex === null
    ? null
    : {
      index: safeHoveredIndex,
      data: chartPoints[safeHoveredIndex],
      x: indexToX(safeHoveredIndex),
      y: Math.min(...pointsBySeries.map((seriesConfig) => seriesConfig.points[safeHoveredIndex].y))
    };

  const tooltipWidth = 196;
  const tooltipHeight = 110;

  const tooltipX = hoverInfo
    ? Math.min(
      Math.max(hoverInfo.x + 12, chartSize.padding.left + 6),
      chartSize.width - chartSize.padding.right - tooltipWidth
    )
    : 0;

  const tooltipY = hoverInfo
    ? Math.max(chartSize.padding.top + 6, hoverInfo.y - tooltipHeight - 10)
    : 0;

  return (
    <section className="trend-panel">
      <div className="trend-header">
        <h2>Evolucao das candidaturas</h2>
        <p className="trend-subtitle">
          Serie diaria acumulada com comparativo de crescimento no periodo selecionado vs {period} dias anteriores.
        </p>
        <div className="trend-controls" role="group" aria-label="Filtro de periodo do grafico">
          {periodOptions.map((option) => (
            <button
              key={`period-${option}`}
              type="button"
              className={`trend-range-btn ${period === option ? "is-active" : ""}`.trim()}
              onClick={() => setPeriod(option)}
            >
              {option} dias
            </button>
          ))}
        </div>
      </div>

      <div className="trend-comparison-grid" role="list" aria-label="Comparativo percentual por periodo">
        {comparisonItems.map((item) => (
          <article key={`comparison-${item.key}`} className="trend-comparison-card" role="listitem">
            <p className="trend-metric-label">{item.label}</p>
            <strong className="trend-metric-value">{numberFormatter.format(item.currentGrowth)}</strong>
            <p className={`trend-metric-delta ${item.deltaClass}`.trim()}>vs {period}d anteriores: {item.deltaLabel}</p>
          </article>
        ))}
      </div>

      <p className="trend-footnote">Exibindo {numberFormatter.format(visibleCount)} de {numberFormatter.format(Math.max(sourcePoints.length, visibleCount))} dias no grafico.</p>

      <div className="line-chart-wrap" onMouseLeave={() => setHoveredIndex(null)}>
        <svg className="line-chart" viewBox={`0 0 ${chartSize.width} ${chartSize.height}`} role="img" aria-label="Grafico de linha de vagas totais, enviadas com sucesso, indisponiveis e pendentes por data">
          <defs>
            <linearGradient id="total-area-fill" x1="0" x2="0" y1="0" y2="1">
              <stop offset="0%" stopColor="rgba(11, 114, 133, 0.34)" />
              <stop offset="100%" stopColor="rgba(11, 114, 133, 0.02)" />
            </linearGradient>
          </defs>

          {yTicks.map((tick, index) => {
            const y = valueToY(tick);
            return (
              <g key={`y-grid-${index}`}>
                <line className="chart-grid-line" x1={chartSize.padding.left} y1={y} x2={chartSize.width - chartSize.padding.right} y2={y} />
                <text className="chart-grid-label" x={chartSize.padding.left - 10} y={y + 4} textAnchor="end">
                  {numberFormatter.format(tick)}
                </text>
              </g>
            );
          })}

          <path className="chart-total-area" d={buildAreaPath(pointsBySeries[0].points, baselineY)} />

          {pointsBySeries.map((seriesConfig) => (
            <path key={`${seriesConfig.key}-path`} className={seriesConfig.pathClass} d={buildLinePath(seriesConfig.points)} />
          ))}

          {pointsBySeries.map((seriesConfig) =>
            seriesConfig.points.map((point, index) => (
              <circle
                key={`${seriesConfig.key}-point-${index}`}
                className={seriesConfig.pointClass}
                cx={point.x}
                cy={point.y}
                r="3"
              />
            ))
          )}

          {chartPoints.map((_, index) => {
            const rawBandX = chartPoints.length > 1
              ? indexToX(index) - hoverBandWidth / 2
              : chartSize.padding.left;

            const bandX = Math.max(
              chartSize.padding.left,
              Math.min(rawBandX, chartSize.width - chartSize.padding.right - hoverBandWidth)
            );

            return (
              <rect
                key={`hover-band-${index}`}
                className="chart-hover-target"
                x={bandX}
                y={chartSize.padding.top}
                width={hoverBandWidth}
                height={drawableHeight}
                onMouseEnter={() => setHoveredIndex(index)}
                onMouseMove={() => setHoveredIndex(index)}
              />
            );
          })}

          {hoverInfo ? (
            <g>
              <line
                className="chart-hover-line"
                x1={hoverInfo.x}
                y1={chartSize.padding.top}
                x2={hoverInfo.x}
                y2={baselineY}
              />

              {pointsBySeries.map((seriesConfig) => (
                <circle
                  key={`active-point-${seriesConfig.key}`}
                  className={`${seriesConfig.pointClass} chart-active-point`}
                  cx={seriesConfig.points[hoverInfo.index].x}
                  cy={seriesConfig.points[hoverInfo.index].y}
                  r="5"
                />
              ))}

              <g transform={`translate(${tooltipX} ${tooltipY})`}>
                <rect className="chart-tooltip-box" width={tooltipWidth} height={tooltipHeight} rx="10" />
                <text className="chart-tooltip-title" x="12" y="20">{hoverInfo.data.label}</text>
                <text className="chart-tooltip-row" x="12" y="38">Total: {numberFormatter.format(Number(hoverInfo.data.total || 0))}</text>
                <text className="chart-tooltip-row" x="12" y="54">Sucesso: {numberFormatter.format(Number(hoverInfo.data.sucesso || 0))}</text>
                <text className="chart-tooltip-row" x="12" y="70">Indisponiveis: {numberFormatter.format(Number(hoverInfo.data.indisponiveis || 0))}</text>
                <text className="chart-tooltip-row" x="12" y="86">Pendentes: {numberFormatter.format(Number(hoverInfo.data.pendentes || 0))}</text>
              </g>
            </g>
          ) : null}

          {chartPoints.map((point, index) => {
            const isFirst = index === 0;
            const isLast = index === chartPoints.length - 1;
            const shouldRender = isFirst || isLast || index % xLabelEvery === 0;

            if (!shouldRender) {
              return null;
            }

            return (
              <text
                key={`x-label-${index}`}
                className="chart-x-label"
                x={indexToX(index)}
                y={chartSize.height - 12}
                textAnchor={isFirst ? "start" : isLast ? "end" : "middle"}
              >
                {point.label}
              </text>
            );
          })}
        </svg>
      </div>

      <ul className="line-legend">
        {pointsBySeries.map((seriesConfig) => (
          <li key={`${seriesConfig.key}-legend`}>
            <span className={seriesConfig.legendClass} />
            <span>{seriesConfig.label}</span>
            <strong>{numberFormatter.format(seriesConfig.lastValue)}</strong>
          </li>
        ))}
      </ul>
    </section>
  );
}

function DataTable({ title, columns, rows, emptyText, enablePagination = false, pageSize = 20 }) {
  const [currentPage, setCurrentPage] = useState(1);

  const totalPages = useMemo(() => {
    if (!enablePagination) {
      return 1;
    }

    return Math.max(1, Math.ceil(rows.length / pageSize));
  }, [enablePagination, pageSize, rows.length]);

  useEffect(() => {
    setCurrentPage((prev) => Math.min(Math.max(prev, 1), totalPages));
  }, [totalPages]);

  const startIndex = enablePagination ? (currentPage - 1) * pageSize : 0;
  const endIndex = enablePagination ? Math.min(startIndex + pageSize, rows.length) : rows.length;
  const visibleRows = enablePagination ? rows.slice(startIndex, endIndex) : rows;
  const showPagination = enablePagination && rows.length > pageSize;

  return (
    <section className="panel">
      <div className="panel-header">
        <h2>{title}</h2>
        <span className="panel-meta">{numberFormatter.format(rows.length)} registros</span>
      </div>

      {rows.length === 0 ? (
        <div className="empty-state">{emptyText}</div>
      ) : (
        <>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  {columns.map((col) => (
                    <th key={col.key}>{col.label}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {visibleRows.map((row, index) => (
                  <tr key={`${title}-${startIndex + index}`}>
                    {columns.map((col) => (
                      <td key={col.key}>{col.render ? col.render(row[col.key], row) : row[col.key] ?? "-"}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {showPagination ? (
            <div className="table-pagination">
              <p className="pagination-range">
                Mostrando {numberFormatter.format(startIndex + 1)} - {numberFormatter.format(endIndex)} de {numberFormatter.format(rows.length)} registros
              </p>
              <div className="pagination-controls">
                <button
                  type="button"
                  className="pager-btn"
                  onClick={() => setCurrentPage((prev) => Math.max(1, prev - 1))}
                  disabled={currentPage === 1}
                >
                  Anterior
                </button>
                <span className="pagination-page">
                  Pagina {currentPage} de {totalPages}
                </span>
                <button
                  type="button"
                  className="pager-btn"
                  onClick={() => setCurrentPage((prev) => Math.min(totalPages, prev + 1))}
                  disabled={currentPage === totalPages}
                >
                  Proxima
                </button>
              </div>
            </div>
          ) : null}
        </>
      )}
    </section>
  );
}

export default function App() {
  const [dashboard, setDashboard] = useState(emptyDashboard);
  const [runtimeStatus, setRuntimeStatus] = useState(emptyRuntime);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState("");

  const successRate = useMemo(() => {
    const total = Number(dashboard.summary.total || 0);
    const success = Number(dashboard.summary.sucesso || 0);
    if (total === 0) {
      return 0;
    }

    return Math.round((success / total) * 100);
  }, [dashboard.summary]);

  const chartCounts = useMemo(() => {
    const success = Number(dashboard.summary.sucesso || 0);
    const unavailable = Number(dashboard.summary.indisponiveis || 0);
    const pending = Number(dashboard.summary.pendentes || 0);

    return {
      success,
      unavailable,
      pending,
      total: success + unavailable + pending
    };
  }, [dashboard.summary]);

  const pieChartStyle = useMemo(() => {
    if (chartCounts.total <= 0) {
      return {
        background: "conic-gradient(#b8c9d8 0deg 360deg)"
      };
    }

    const successDeg = (chartCounts.success / chartCounts.total) * 360;
    const unavailableDeg = (chartCounts.unavailable / chartCounts.total) * 360;
    const successEnd = successDeg;
    const unavailableEnd = successDeg + unavailableDeg;

    return {
      background: `conic-gradient(#1b7f45 0deg ${successEnd}deg, #b12a42 ${successEnd}deg ${unavailableEnd}deg, #b56e00 ${unavailableEnd}deg 360deg)`
    };
  }, [chartCounts]);

  const runtimeVisual = useMemo(() => {
    if (runtimeStatus.state === "running") {
      return {
        tone: "runtime-running",
        badge: "ON",
        title: "Robo executando",
        detail: runtimeStatus.heartbeatDetail || "Heartbeat ativo registrado no PostgreSQL."
      };
    }

    if (runtimeStatus.state === "waiting") {
      return {
        tone: "runtime-waiting",
        badge: "WAIT",
        title: "Aguardando proximo ciclo",
        detail: runtimeStatus.heartbeatDetail || "Execucao em pausa entre os ciclos."
      };
    }

    const minuteText = runtimeStatus.minutesSinceHeartbeat === null
      ? ""
      : ` Ultimo heartbeat: ${runtimeStatus.minutesSinceHeartbeat} min.`;

    return {
      tone: "runtime-offline",
      badge: "OFF",
      title: "Robo offline",
      detail: `Sem heartbeat recente no PostgreSQL.${minuteText}`.trim()
    };
  }, [runtimeStatus]);

  async function fetchDashboard(isSilent = false) {
    try {
      if (isSilent) {
        setRefreshing(true);
      } else {
        setLoading(true);
      }

      const [dashboardResult, runtimeResult] = await Promise.allSettled([
        fetchJson("/api/dashboard"),
        fetchJson("/api/crawler-status")
      ]);

      if (dashboardResult.status === "fulfilled") {
        setDashboard(normalizeDashboard(dashboardResult.value));
      }

      if (runtimeResult.status === "fulfilled") {
        setRuntimeStatus(normalizeRuntimeStatus(runtimeResult.value));
      }

      if (dashboardResult.status === "rejected" && runtimeResult.status === "rejected") {
        throw new Error("Nao foi possivel atualizar dashboard nem status do crawler.");
      }

      if (dashboardResult.status === "rejected") {
        setError(dashboardResult.reason?.message || "Falha ao carregar o dashboard.");
      } else if (runtimeResult.status === "rejected") {
        setError("Status do crawler indisponivel temporariamente. Mantendo ultimo estado conhecido.");
      } else {
        setError("");
      }
    } catch (fetchError) {
      setError(fetchError.message || "Erro inesperado ao consultar o dashboard.");
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }

  useEffect(() => {
    fetchDashboard();

    const timer = setInterval(() => {
      fetchDashboard(true);
    }, REFRESH_INTERVAL_MS);

    return () => clearInterval(timer);
  }, []);

  return (
    <div className="app-shell">
      <header className="top-header">
        <div>
          <p className="eyebrow">monitoramento em tempo real</p>
          <div className="header-title-row">
            <h1>AppLinkdin Dashboard</h1>
            <CrawlerStatus />
          </div>
          <p className="subtitle">Fluxo end-to-end das candidaturas com dados diretos do PostgreSQL.</p>
        </div>
        <div className="header-actions">
          <button type="button" onClick={() => fetchDashboard(true)} disabled={refreshing}>
            {refreshing ? "Atualizando..." : "Atualizar agora"}
          </button>
          <small>Ultima leitura: {formatDate(dashboard.generatedAt)}</small>
        </div>
      </header>

      {error ? <div className="error-banner">{error}</div> : null}

      {loading ? (
        <section className="loading-card">Carregando dados do banco...</section>
      ) : (
        <main className="content-grid">
          <section className="summary-grid">
            <SummaryCard
              title="Total de vagas"
              value={Number(dashboard.summary.total || 0)}
              hint="Volume atual no banco"
              tone="total"
            />
            <SummaryCard
              title="Enviadas com sucesso"
              value={Number(dashboard.summary.sucesso || 0)}
              hint="Aplicacoes finalizadas"
              tone="success"
            />
            <SummaryCard
              title="Indisponiveis"
              value={Number(dashboard.summary.indisponiveis || 0)}
              hint="Com bloqueio ou erro"
              tone="danger"
            />
            <SummaryCard
              title="Pendentes"
              value={Number(dashboard.summary.pendentes || 0)}
              hint="Ainda nao processadas"
              tone="pending"
            />
          </section>

          <section className="highlight-panel">
            <div className="highlight-copy">
              <h2>Taxa de sucesso</h2>
              <p className="highlight-subtitle">Distribuicao por status de vagas no ciclo.</p>
              <p className="summary-hint">Pie chart: Enviadas com sucesso, Indisponiveis e Pendentes.</p>
              <div className="highlight-pill-wrap">
                <StatusPill
                  kind={successRate >= 50 ? "success" : successRate >= 20 ? "warning" : "danger"}
                  label={successRate >= 50 ? "Performance forte" : successRate >= 20 ? "Performance media" : "Performance baixa"}
                />
              </div>
            </div>

            <div className={`runtime-indicator ${runtimeVisual.tone}`}>
              <div className="runtime-clock" role="img" aria-label={runtimeVisual.title}>
                <span className="runtime-clock-ring" />
                <span className="runtime-clock-hand runtime-clock-hand-hour" />
                <span className="runtime-clock-hand runtime-clock-hand-minute" />
                <span className="runtime-clock-hub" />
                <span className="runtime-clock-code">{runtimeVisual.badge}</span>
              </div>
              <p className="runtime-title">{runtimeVisual.title}</p>
              <p className="runtime-detail">{runtimeVisual.detail}</p>
            </div>

            <div className="chart-and-legend">
              <div className="pie-chart" style={pieChartStyle} role="img" aria-label="Distribuicao de status das vagas">
                <span className="pie-chart-center">{successRate}%</span>
              </div>

              <ul className="pie-legend">
                <li>
                  <span className="legend-dot legend-success" />
                  <span>Enviadas com sucesso</span>
                  <strong>{numberFormatter.format(chartCounts.success)}</strong>
                </li>
                <li>
                  <span className="legend-dot legend-unavailable" />
                  <span>Indisponiveis</span>
                  <strong>{numberFormatter.format(chartCounts.unavailable)}</strong>
                </li>
                <li>
                  <span className="legend-dot legend-pending" />
                  <span>Pendentes</span>
                  <strong>{numberFormatter.format(chartCounts.pending)}</strong>
                </li>
              </ul>
            </div>
          </section>

          <StatusLineChart points={dashboard.timeline} />

          <DataTable
            title="Candidaturas enviadas com sucesso"
            emptyText="Nenhuma candidatura com sucesso no momento."
            rows={dashboard.successfulJobs}
            enablePagination
            pageSize={20}
            columns={[
              { key: "titulo", label: "Vaga" },
              { key: "empresa", label: "Empresa" },
              { key: "localizacao", label: "Local" },
              { key: "data_envio_sucesso", label: "Enviada em", render: (value) => formatDate(value) },
              { key: "link", label: "Acao", render: (value) => <JobActionButton link={value} /> }
            ]}
          />

          <DataTable
            title="Vagas indisponiveis / bloqueadas"
            emptyText="Nenhuma vaga indisponivel registrada."
            rows={dashboard.unavailableJobs}
            enablePagination
            pageSize={20}
            columns={[
              { key: "titulo", label: "Vaga" },
              { key: "empresa", label: "Empresa" },
              { key: "motivo_indisponibilidade", label: "Motivo" },
              { key: "data_indisponibilidade", label: "Registrada em", render: (value) => formatDate(value) },
              { key: "link", label: "Acao", render: (value) => <JobActionButton link={value} /> }
            ]}
          />

          <DataTable
            title="Enviadas sem confirmacao final"
            emptyText="Sem vagas nesse estado."
            rows={dashboard.pendingConfirmation}
            enablePagination
            pageSize={20}
            columns={[
              { key: "titulo", label: "Vaga" },
              { key: "empresa", label: "Empresa" },
              { key: "localizacao", label: "Local" },
              { key: "data_candidatura", label: "Candidatura em", render: (value) => formatDate(value) },
              { key: "link", label: "Acao", render: (value) => <JobActionButton link={value} /> }
            ]}
          />

          <DataTable
            title="Ultimas etapas do fluxo"
            emptyText="Nenhuma etapa registrada ainda."
            rows={dashboard.recentSteps}
            enablePagination
            pageSize={20}
            columns={[
              { key: "etapa", label: "Etapa" },
              { key: "sucesso", label: "Status", render: (value) => (value ? <StatusPill kind="success" label="Sucesso" /> : <StatusPill kind="danger" label="Falha" />) },
              { key: "detalhe", label: "Detalhe" },
              { key: "criado_em", label: "Data", render: (value) => formatDate(value) }
            ]}
          />
        </main>
      )}

      <footer className="page-footer">
        <p>AppLinkdin dashboard</p>
        <p>Atualizacao automatica a cada 30 segundos</p>
      </footer>
    </div>
  );
}
