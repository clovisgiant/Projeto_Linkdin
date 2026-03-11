import { useCallback, useEffect, useState } from "react";

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

export default function CrawlerStatus() {
  const [status, setStatus] = useState(null);

  const fetch_ = useCallback(async () => {
    try {
      const resp = await fetch("/api/crawler-status");
      if (resp.ok) {
        const data = await resp.json();
        setStatus(data.state);
      }
    } catch {
      setStatus("offline");
    }
  }, []);

  useEffect(() => {
    fetch_();
    const timer = setInterval(fetch_, 15000);
    return () => clearInterval(timer);
  }, [fetch_]);

  if (!status) {
    return null;
  }

  const cfg = STATUS_CONFIG[status] ?? STATUS_CONFIG.offline;

  return (
    <span className={`crawler-badge ${cfg.tone}`}>
      <span className={`crawler-dot ${cfg.pulse ? "crawler-dot--pulse" : ""}`} />
      {cfg.label}
    </span>
  );
}
