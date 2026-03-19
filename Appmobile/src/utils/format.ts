import { RuntimeStatus } from "../types/dashboard";

const numberFormatter = new Intl.NumberFormat("pt-BR");

export interface RuntimeVisual {
  tone: "running" | "waiting" | "offline";
  badge: "ON" | "WAIT" | "OFF";
  title: string;
  detail: string;
}

export function formatNumber(value: number): string {
  return numberFormatter.format(Number.isFinite(value) ? value : 0);
}

export function formatDateTime(value?: string | null): string {
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

export function getRuntimeVisual(runtime: RuntimeStatus): RuntimeVisual {
  if (runtime.state === "running") {
    return {
      tone: "running",
      badge: "ON",
      title: "Robo executando",
      detail: runtime.heartbeatDetail || "Heartbeat ativo registrado no PostgreSQL."
    };
  }

  if (runtime.state === "waiting") {
    return {
      tone: "waiting",
      badge: "WAIT",
      title: "Aguardando proximo ciclo",
      detail: runtime.heartbeatDetail || "Execucao em pausa entre os ciclos."
    };
  }

  const minuteText =
    runtime.minutesSinceHeartbeat === null
      ? ""
      : ` Ultimo heartbeat: ${runtime.minutesSinceHeartbeat} min.`;

  return {
    tone: "offline",
    badge: "OFF",
    title: "Robo offline",
    detail: `Sem heartbeat recente no PostgreSQL.${minuteText}`.trim()
  };
}
