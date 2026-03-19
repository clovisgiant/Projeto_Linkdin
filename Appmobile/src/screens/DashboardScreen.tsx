import { LinearGradient } from "expo-linear-gradient";
import { useMemo } from "react";
import {
  ActivityIndicator,
  Pressable,
  RefreshControl,
  SafeAreaView,
  ScrollView,
  StyleSheet,
  Text,
  View
} from "react-native";
import { DonutChart } from "../components/DonutChart";
import { JobListSection, JobRow } from "../components/JobListSection";
import { RuntimeClockCard } from "../components/RuntimeClockCard";
import { StatusBadge } from "../components/StatusBadge";
import { SummaryCard } from "../components/SummaryCard";
import { fonts, theme } from "../constants/theme";
import { useDashboardData } from "../hooks/useDashboardData";
import { formatDateTime } from "../utils/format";

function textOrFallback(value: string | null | undefined, fallback: string): string {
  const normalized = String(value ?? "").replace(/\s+/g, " ").trim();
  return normalized || fallback;
}

function mountCompanyLocation(company: string | null | undefined, location: string | null | undefined): string {
  const normalizedCompany = textOrFallback(company, "Empresa nao informada");
  const normalizedLocation = textOrFallback(location, "Local nao informado");

  if (!normalizedLocation || normalizedLocation === "Local nao informado") {
    return normalizedCompany;
  }

  return `${normalizedCompany} - ${normalizedLocation}`;
}

export function DashboardScreen() {
  const { dashboard, runtime, loading, refreshing, error, lastUpdated, refresh } = useDashboardData();

  const successRate = useMemo(() => {
    const total = Number(dashboard.summary.total || 0);
    const success = Number(dashboard.summary.sucesso || 0);

    if (total <= 0) {
      return 0;
    }

    return Math.round((success / total) * 100);
  }, [dashboard.summary]);

  const chartCounts = useMemo(
    () => ({
      success: Number(dashboard.summary.sucesso || 0),
      unavailable: Number(dashboard.summary.indisponiveis || 0),
      pending: Number(dashboard.summary.pendentes || 0)
    }),
    [dashboard.summary]
  );

  const performance = useMemo(() => {
    if (successRate >= 50) {
      return {
        label: "Performance forte",
        tone: "success" as const
      };
    }

    if (successRate >= 20) {
      return {
        label: "Performance media",
        tone: "warning" as const
      };
    }

    return {
      label: "Performance baixa",
      tone: "danger" as const
    };
  }, [successRate]);

  const successfulRows = useMemo<JobRow[]>(
    () =>
      dashboard.successfulJobs.slice(0, 8).map((job) => ({
        primary: textOrFallback(job.titulo, "Vaga sem titulo"),
        secondary: mountCompanyLocation(job.empresa, job.localizacao),
        meta: `Enviada em ${formatDateTime(job.data_envio_sucesso)}`,
        tone: "success"
      })),
    [dashboard.successfulJobs]
  );

  const unavailableRows = useMemo<JobRow[]>(
    () =>
      dashboard.unavailableJobs.slice(0, 8).map((job) => ({
        primary: textOrFallback(job.titulo, "Vaga sem titulo"),
        secondary: textOrFallback(job.empresa, "Empresa nao informada"),
        meta: `${textOrFallback(job.motivo_indisponibilidade, "Sem motivo")}. Registro: ${formatDateTime(job.data_indisponibilidade)}`,
        tone: "danger"
      })),
    [dashboard.unavailableJobs]
  );

  const pendingRows = useMemo<JobRow[]>(
    () =>
      dashboard.pendingConfirmation.slice(0, 8).map((job) => ({
        primary: textOrFallback(job.titulo, "Vaga sem titulo"),
        secondary: mountCompanyLocation(job.empresa, job.localizacao),
        meta: `Candidatura em ${formatDateTime(job.data_candidatura)}`,
        tone: "neutral"
      })),
    [dashboard.pendingConfirmation]
  );

  const recentRows = useMemo<JobRow[]>(
    () =>
      dashboard.recentSteps.slice(0, 8).map((step) => ({
        primary: textOrFallback(step.etapa, "Etapa nao informada"),
        secondary: textOrFallback(step.detalhe, "Sem detalhe adicional"),
        meta: `${step.sucesso ? "Sucesso" : "Falha"} em ${formatDateTime(step.criado_em)}`,
        tone: step.sucesso ? "success" : "danger"
      })),
    [dashboard.recentSteps]
  );

  const hasData = useMemo(() => {
    return (
      dashboard.summary.total > 0 ||
      dashboard.successfulJobs.length > 0 ||
      dashboard.unavailableJobs.length > 0 ||
      dashboard.pendingConfirmation.length > 0 ||
      dashboard.recentSteps.length > 0
    );
  }, [dashboard]);

  const lastRead = useMemo(() => {
    if (lastUpdated) {
      return formatDateTime(lastUpdated.toISOString());
    }

    return formatDateTime(dashboard.generatedAt);
  }, [dashboard.generatedAt, lastUpdated]);

  return (
    <SafeAreaView style={styles.safeArea}>
      <LinearGradient colors={[theme.colors.bgTop, theme.colors.bgBottom]} style={styles.gradient}>
        <View style={styles.atmoOne} />
        <View style={styles.atmoTwo} />

        <ScrollView
          showsVerticalScrollIndicator={false}
          style={styles.scroll}
          contentContainerStyle={styles.content}
          refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void refresh()} tintColor="#FFFFFF" />}
        >
          <View style={styles.header}>
            <Text style={styles.eyebrow}>monitoramento em tempo real</Text>
            <Text style={styles.title}>AppLinkdin Mobile</Text>
            <Text style={styles.subtitle}>Fluxo mobile das candidaturas com leitura direta do PostgreSQL.</Text>
            <StatusBadge runtime={runtime} style={styles.statusSpacing} />

            <View style={styles.headerActions}>
              <Pressable
                style={({ pressed }) => [styles.refreshButton, pressed ? styles.refreshButtonPressed : null]}
                onPress={() => void refresh()}
              >
                <Text style={styles.refreshButtonText}>{refreshing ? "Atualizando..." : "Atualizar agora"}</Text>
              </Pressable>
              <Text style={styles.readText}>Ultima leitura: {lastRead}</Text>
            </View>
          </View>

          {error ? (
            <View style={styles.errorBanner}>
              <Text style={styles.errorText}>{error}</Text>
            </View>
          ) : null}

          {loading && !hasData ? (
            <View style={styles.loadingCard}>
              <ActivityIndicator size="small" color={theme.colors.panelStrong} />
              <Text style={styles.loadingText}>Carregando dados do dashboard...</Text>
            </View>
          ) : (
            <>
              <View style={styles.summaryGrid}>
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
                  hint="Bloqueio ou erro"
                  tone="danger"
                />
                <SummaryCard
                  title="Pendentes"
                  value={Number(dashboard.summary.pendentes || 0)}
                  hint="Ainda nao processadas"
                  tone="pending"
                />
              </View>

              <View style={styles.successCard}>
                <Text style={styles.successTitle}>Taxa de sucesso</Text>
                <Text style={styles.successSubtitle}>Distribuicao por status no ciclo atual.</Text>
                <View
                  style={[
                    styles.performancePill,
                    performance.tone === "success"
                      ? styles.performanceSuccess
                      : performance.tone === "warning"
                        ? styles.performanceWarning
                        : styles.performanceDanger
                  ]}
                >
                  <Text
                    style={[
                      styles.performanceText,
                      performance.tone === "success"
                        ? styles.performanceSuccessText
                        : performance.tone === "warning"
                          ? styles.performanceWarningText
                          : styles.performanceDangerText
                    ]}
                  >
                    {performance.label}
                  </Text>
                </View>
              </View>

              <RuntimeClockCard runtime={runtime} />

              <DonutChart
                success={chartCounts.success}
                unavailable={chartCounts.unavailable}
                pending={chartCounts.pending}
              />

              <JobListSection
                title="Candidaturas enviadas"
                subtitle="Ultimos registros com sucesso"
                emptyText="Nenhuma candidatura com sucesso no momento."
                rows={successfulRows}
              />

              <JobListSection
                title="Vagas indisponiveis"
                subtitle="Itens bloqueados ou encerrados"
                emptyText="Nenhuma vaga indisponivel registrada."
                rows={unavailableRows}
              />

              <JobListSection
                title="Sem confirmacao final"
                subtitle="Aplicacoes enviadas sem retorno conclusivo"
                emptyText="Sem vagas nesse estado."
                rows={pendingRows}
              />

              <JobListSection
                title="Ultimas etapas do fluxo"
                subtitle="Log resumido das ultimas acoes"
                emptyText="Nenhuma etapa registrada ainda."
                rows={recentRows}
              />

              <View style={styles.footer}>
                <Text style={styles.footerTitle}>AppLinkdin dashboard mobile</Text>
                <Text style={styles.footerSubtitle}>Atualizacao automatica a cada 30 segundos</Text>
              </View>
            </>
          )}
        </ScrollView>
      </LinearGradient>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: theme.colors.bgTop
  },
  gradient: {
    flex: 1
  },
  atmoOne: {
    position: "absolute",
    top: -130,
    right: -70,
    width: 280,
    height: 280,
    borderRadius: 160,
    backgroundColor: "rgba(10, 167, 166, 0.14)"
  },
  atmoTwo: {
    position: "absolute",
    top: 260,
    left: -80,
    width: 240,
    height: 240,
    borderRadius: 140,
    backgroundColor: "rgba(47, 111, 180, 0.18)"
  },
  scroll: {
    flex: 1
  },
  content: {
    paddingHorizontal: theme.spacing.md,
    paddingTop: theme.spacing.xxl,
    paddingBottom: 48,
    gap: theme.spacing.md
  },
  header: {
    borderRadius: theme.radius.lg,
    padding: theme.spacing.lg,
    backgroundColor: "rgba(7, 20, 39, 0.44)",
    borderWidth: 1,
    borderColor: "rgba(231, 244, 255, 0.16)",
    gap: 10
  },
  eyebrow: {
    color: "#A6C8E8",
    fontFamily: fonts.medium,
    fontSize: 11,
    letterSpacing: 1.2,
    textTransform: "uppercase"
  },
  title: {
    color: "#F3F9FF",
    fontFamily: fonts.bold,
    fontSize: 30,
    lineHeight: 32
  },
  subtitle: {
    color: "#BDD7ED",
    fontFamily: fonts.regular,
    fontSize: 14,
    lineHeight: 19
  },
  statusSpacing: {
    alignSelf: "flex-start",
    marginTop: 4
  },
  headerActions: {
    marginTop: 8,
    gap: 8
  },
  refreshButton: {
    alignSelf: "flex-start",
    borderRadius: theme.radius.full,
    paddingHorizontal: theme.spacing.lg,
    paddingVertical: 10,
    backgroundColor: "#F3FBFF"
  },
  refreshButtonPressed: {
    opacity: 0.85,
    transform: [{ scale: 0.98 }]
  },
  refreshButtonText: {
    fontFamily: fonts.semibold,
    color: theme.colors.textPrimary,
    fontSize: 13
  },
  readText: {
    color: "#A9C7E0",
    fontFamily: fonts.regular,
    fontSize: 12
  },
  errorBanner: {
    borderRadius: theme.radius.md,
    borderWidth: 1,
    borderColor: "rgba(177, 42, 66, 0.8)",
    backgroundColor: "rgba(177, 42, 66, 0.22)",
    padding: theme.spacing.md
  },
  errorText: {
    color: "#FFDCE4",
    fontFamily: fonts.medium,
    fontSize: 13,
    lineHeight: 19
  },
  loadingCard: {
    borderRadius: theme.radius.md,
    borderWidth: 1,
    borderColor: "rgba(231, 244, 255, 0.22)",
    backgroundColor: "rgba(7, 20, 39, 0.3)",
    padding: theme.spacing.lg,
    flexDirection: "row",
    alignItems: "center",
    gap: 10
  },
  loadingText: {
    color: "#E6F4FF",
    fontFamily: fonts.medium,
    fontSize: 13
  },
  summaryGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    justifyContent: "space-between",
    gap: theme.spacing.sm
  },
  successCard: {
    borderRadius: theme.radius.lg,
    borderWidth: 1,
    borderColor: theme.colors.line,
    backgroundColor: theme.colors.panelStrong,
    padding: theme.spacing.lg,
    ...theme.shadow.card
  },
  successTitle: {
    color: theme.colors.textPrimary,
    fontFamily: fonts.semibold,
    fontSize: 20,
    marginBottom: 6
  },
  successSubtitle: {
    color: theme.colors.textMuted,
    fontFamily: fonts.regular,
    fontSize: 13,
    lineHeight: 19,
    marginBottom: 12
  },
  performancePill: {
    alignSelf: "flex-start",
    paddingHorizontal: theme.spacing.md,
    paddingVertical: 8,
    borderRadius: theme.radius.full,
    borderWidth: 1
  },
  performanceText: {
    fontFamily: fonts.semibold,
    fontSize: 13
  },
  performanceSuccess: {
    backgroundColor: "#EAF9F0",
    borderColor: "rgba(27, 127, 69, 0.5)"
  },
  performanceWarning: {
    backgroundColor: "#FFF5E6",
    borderColor: "rgba(181, 110, 0, 0.5)"
  },
  performanceDanger: {
    backgroundColor: "#FFEFF3",
    borderColor: "rgba(177, 42, 66, 0.5)"
  },
  performanceSuccessText: {
    color: theme.colors.success
  },
  performanceWarningText: {
    color: theme.colors.warning
  },
  performanceDangerText: {
    color: theme.colors.danger
  },
  footer: {
    borderRadius: theme.radius.md,
    borderWidth: 1,
    borderColor: "rgba(231, 244, 255, 0.2)",
    backgroundColor: "rgba(7, 20, 39, 0.34)",
    padding: theme.spacing.md,
    alignItems: "center",
    gap: 4
  },
  footerTitle: {
    color: "#E8F4FF",
    fontFamily: fonts.semibold,
    fontSize: 13
  },
  footerSubtitle: {
    color: "#A6C4DD",
    fontFamily: fonts.regular,
    fontSize: 12
  }
});
