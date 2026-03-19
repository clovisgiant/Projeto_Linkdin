import { useCallback, useEffect, useState } from "react";
import { emptyDashboard, emptyRuntime, fetchDashboardData } from "../services/api";
import { DashboardPayload, RuntimeStatus } from "../types/dashboard";

const REFRESH_INTERVAL_MS = 30000;

export interface DashboardState {
  dashboard: DashboardPayload;
  runtime: RuntimeStatus;
  loading: boolean;
  refreshing: boolean;
  error: string;
  lastUpdated: Date | null;
  refresh: () => Promise<void>;
}

export function useDashboardData(): DashboardState {
  const [dashboard, setDashboard] = useState<DashboardPayload>(emptyDashboard);
  const [runtime, setRuntime] = useState<RuntimeStatus>(emptyRuntime);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState("");
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  const load = useCallback(async (silent: boolean) => {
    try {
      if (silent) {
        setRefreshing(true);
      } else {
        setLoading(true);
      }

      const payload = await fetchDashboardData();
      setDashboard(payload.dashboard);
      setRuntime(payload.runtime);
      setLastUpdated(new Date());
      setError("");
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : "Erro inesperado ao carregar o dashboard.";
      setError(message);
      setRuntime(emptyRuntime);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    void load(false);

    const timer = setInterval(() => {
      void load(true);
    }, REFRESH_INTERVAL_MS);

    return () => clearInterval(timer);
  }, [load]);

  const refresh = useCallback(async () => {
    await load(true);
  }, [load]);

  return {
    dashboard,
    runtime,
    loading,
    refreshing,
    error,
    lastUpdated,
    refresh
  };
}
