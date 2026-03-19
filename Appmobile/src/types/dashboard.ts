export type RuntimeState = "running" | "waiting" | "offline";

export interface DashboardSummary {
  total: number;
  sucesso: number;
  enviada_sem_confirmacao: number;
  indisponiveis: number;
  pendentes: number;
}

export interface JobItem {
  titulo?: string | null;
  empresa?: string | null;
  localizacao?: string | null;
  motivo_indisponibilidade?: string | null;
  data_candidatura?: string | null;
  data_envio_sucesso?: string | null;
  data_indisponibilidade?: string | null;
}

export interface RecentStep {
  link?: string | null;
  etapa?: string | null;
  sucesso?: boolean | null;
  detalhe?: string | null;
  criado_em?: string | null;
}

export interface DashboardPayload {
  generatedAt: string | null;
  summary: DashboardSummary;
  successfulJobs: JobItem[];
  unavailableJobs: JobItem[];
  pendingConfirmation: JobItem[];
  recentSteps: RecentStep[];
}

export interface RuntimeStatus {
  state: RuntimeState;
  running: boolean;
  heartbeatState: string | null;
  heartbeatDetail: string;
  minutesSinceHeartbeat: number | null;
}
