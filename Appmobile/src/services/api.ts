import { Platform } from "react-native";
import { DashboardPayload, JobItem, RecentStep, RuntimeStatus } from "../types/dashboard";

const defaultBaseUrl = Platform.select({
  android: "http://10.0.2.2:4000",
  ios: "http://localhost:4000",
  default: "http://localhost:4000"
});

export const API_BASE_URL = (defaultBaseUrl ?? "http://localhost:4000").replace(/\/+$/, "");

export const emptyDashboard: DashboardPayload = {
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

export const emptyRuntime: RuntimeStatus = {
  state: "offline",
  running: false,
  heartbeatState: null,
  heartbeatDetail: "",
  minutesSinceHeartbeat: null
};

function asObject(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object") {
    return {};
  }

  return value as Record<string, unknown>;
}

function toNumber(value: unknown): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function toString(value: unknown): string {
  return String(value ?? "").replace(/\s+/g, " ").trim();
}

function toBoolean(value: unknown): boolean {
  return Boolean(value);
}

function normalizeJobList(value: unknown): JobItem[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.map((item) => asObject(item));
}

function normalizeRecentSteps(value: unknown): RecentStep[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.map((item) => asObject(item));
}

function normalizeDashboard(payload: unknown): DashboardPayload {
  const root = asObject(payload);
  const summary = asObject(root.summary);

  return {
    generatedAt: toString(root.generatedAt) || null,
    summary: {
      total: toNumber(summary.total),
      sucesso: toNumber(summary.sucesso),
      enviada_sem_confirmacao: toNumber(summary.enviada_sem_confirmacao),
      indisponiveis: toNumber(summary.indisponiveis),
      pendentes: toNumber(summary.pendentes)
    },
    successfulJobs: normalizeJobList(root.successfulJobs),
    unavailableJobs: normalizeJobList(root.unavailableJobs),
    pendingConfirmation: normalizeJobList(root.pendingConfirmation),
    recentSteps: normalizeRecentSteps(root.recentSteps)
  };
}

function normalizeRuntimeStatus(payload: unknown): RuntimeStatus {
  const root = asObject(payload);
  const rawState = toString(root.state).toLowerCase();
  const rawHeartbeatState = toString(root.heartbeatState).toLowerCase();
  const rawMinutes = Number(root.minutesSinceHeartbeat);

  return {
    state: rawState === "running" || rawState === "waiting" ? rawState : "offline",
    running: toBoolean(root.running),
    heartbeatState: rawHeartbeatState || null,
    heartbeatDetail: toString(root.heartbeatDetail),
    minutesSinceHeartbeat: Number.isFinite(rawMinutes) ? rawMinutes : null
  };
}

function buildUrl(path: string): string {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  return `${API_BASE_URL}${normalizedPath}`;
}

async function fetchJson(path: string): Promise<unknown> {
  const response = await fetch(buildUrl(path));
  if (!response.ok) {
    throw new Error(`Falha ao consultar ${path}: ${response.status}`);
  }

  return response.json();
}

export async function fetchDashboardData(): Promise<{ dashboard: DashboardPayload; runtime: RuntimeStatus }> {
  const [dashboardPayload, runtimePayload] = await Promise.all([
    fetchJson("/api/dashboard"),
    fetchJson("/api/crawler-status").catch(() => null)
  ]);

  return {
    dashboard: normalizeDashboard(dashboardPayload),
    runtime: runtimePayload ? normalizeRuntimeStatus(runtimePayload) : emptyRuntime
  };
}
