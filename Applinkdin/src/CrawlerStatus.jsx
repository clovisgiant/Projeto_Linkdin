import { useCallback, useEffect, useState } from "react";

const API_FETCH_OPTIONS = {
  cache: "no-store",
  headers: {
    "Cache-Control": "no-cache",
    Pragma: "no-cache"
  }
};

const STATUS_CONFIG = {
  running: {
    label: "Executando ciclo",
    pulse: true,
    tone: "status-running"
  },
  waiting: {
    label: "Aguardando próximo ciclo",
    pulse: false,
    tone: "status-waiting"
  },
  offline: {
    label: "Automação offline",
    pulse: false,
    tone: "status-offline"
  }
};

function normalizeText(value) {
  return String(value || "").replace(/\s+/g, " ").trim();
}

function dedupeImmediateRepeat(value) {
  const text = normalizeText(value);
  if (!text) {
    return "";
  }

  if (text.length % 2 !== 0) {
    return text;
  }

  const half = text.length / 2;
  const firstHalf = text.slice(0, half).trim();
  const secondHalf = text.slice(half).trim();
  return firstHalf === secondHalf ? firstHalf : text;
}

function getViewModel(payload) {
  const rawState = normalizeText(payload?.state).toLowerCase();
  const state = STATUS_CONFIG[rawState] ? rawState : "offline";
  const base = STATUS_CONFIG[state];
  const isRunning = Boolean(payload?.running);

  let label = base.label;
  let tone = base.tone;
  let pulse = base.pulse;

  if (isRunning && state === "waiting") {
    label = "Aguardando próximo ciclo";
    tone = "status-waiting";
    pulse = false;
  } else if (isRunning && state === "offline") {
    label = "Automação ativa";
    tone = "status-running";
    pulse = true;
  }

  return {
    ...base,
    tone,
    pulse,
    label: dedupeImmediateRepeat(label),
    detail: dedupeImmediateRepeat(payload?.heartbeatDetail)
  };
}

export default function CrawlerStatus() {
  const [view, setView] = useState(null);

  const fetch_ = useCallback(async () => {
    try {
      const resp = await fetch(`/api/crawler-status?_=${Date.now()}`, API_FETCH_OPTIONS);
      if (resp.ok) {
        const data = await resp.json();
        setView(getViewModel(data));
        return;
      }

      setView(getViewModel({ state: "offline", running: false }));
    } catch {
      setView(getViewModel({ state: "offline", running: false }));
    }
  }, []);

  useEffect(() => {
    fetch_();
    const timer = setInterval(fetch_, 15000);
    return () => clearInterval(timer);
  }, [fetch_]);

  if (!view) {
    return null;
  }

  return (
    <span className={`crawler-badge ${view.tone}`} title={view.detail || undefined}>
      <span className={`crawler-dot ${view.pulse ? "crawler-dot--pulse" : ""}`} />
      {view.label}
    </span>
  );
}
