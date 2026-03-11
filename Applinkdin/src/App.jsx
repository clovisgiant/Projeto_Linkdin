import { useEffect, useMemo, useState } from "react";
import CrawlerStatus from "./CrawlerStatus.jsx";
import "./styles.css";

const REFRESH_INTERVAL_MS = 30000;
const numberFormatter = new Intl.NumberFormat("pt-BR");

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
  recentSteps: []
};

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

function DataTable({ title, columns, rows, emptyText }) {
  return (
    <section className="panel">
      <div className="panel-header">
        <h2>{title}</h2>
      </div>

      {rows.length === 0 ? (
        <div className="empty-state">{emptyText}</div>
      ) : (
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
              {rows.map((row, index) => (
                <tr key={`${title}-${index}`}>
                  {columns.map((col) => (
                    <td key={col.key}>{col.render ? col.render(row[col.key], row) : row[col.key] ?? "-"}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

export default function App() {
  const [dashboard, setDashboard] = useState(emptyDashboard);
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

  async function fetchDashboard(isSilent = false) {
    try {
      if (isSilent) {
        setRefreshing(true);
      } else {
        setLoading(true);
      }

      const response = await fetch("/api/dashboard");
      if (!response.ok) {
        throw new Error(`Falha ao carregar dados: ${response.status}`);
      }

      const payload = await response.json();
      setDashboard(payload);
      setError("");
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
            <div>
              <h2>Taxa de sucesso</h2>
              <p className="rate">{successRate}%</p>
              <p className="subtitle">Baseado no total de vagas coletadas.</p>
            </div>
            <StatusPill
              kind={successRate >= 50 ? "success" : successRate >= 20 ? "warning" : "danger"}
              label={successRate >= 50 ? "Performance forte" : successRate >= 20 ? "Performance media" : "Performance baixa"}
            />
          </section>

          <DataTable
            title="Candidaturas enviadas com sucesso"
            emptyText="Nenhuma candidatura com sucesso no momento."
            rows={dashboard.successfulJobs}
            columns={[
              { key: "titulo", label: "Vaga" },
              { key: "empresa", label: "Empresa" },
              { key: "localizacao", label: "Local" },
              { key: "data_envio_sucesso", label: "Enviada em", render: (value) => formatDate(value) }
            ]}
          />

          <DataTable
            title="Vagas indisponiveis / bloqueadas"
            emptyText="Nenhuma vaga indisponivel registrada."
            rows={dashboard.unavailableJobs}
            columns={[
              { key: "titulo", label: "Vaga" },
              { key: "empresa", label: "Empresa" },
              { key: "motivo_indisponibilidade", label: "Motivo" },
              { key: "data_indisponibilidade", label: "Registrada em", render: (value) => formatDate(value) }
            ]}
          />

          <DataTable
            title="Enviadas sem confirmacao final"
            emptyText="Sem vagas nesse estado."
            rows={dashboard.pendingConfirmation}
            columns={[
              { key: "titulo", label: "Vaga" },
              { key: "empresa", label: "Empresa" },
              { key: "localizacao", label: "Local" },
              { key: "data_candidatura", label: "Candidatura em", render: (value) => formatDate(value) }
            ]}
          />

          <DataTable
            title="Ultimas etapas do fluxo"
            emptyText="Nenhuma etapa registrada ainda."
            rows={dashboard.recentSteps}
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
