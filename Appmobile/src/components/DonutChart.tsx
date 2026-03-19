import { useMemo } from "react";
import { StyleSheet, Text, View } from "react-native";
import Svg, { Circle, G } from "react-native-svg";
import { fonts, theme } from "../constants/theme";
import { formatNumber } from "../utils/format";

interface DonutChartProps {
  success: number;
  unavailable: number;
  pending: number;
}

interface Segment {
  key: string;
  color: string;
  value: number;
}

export function DonutChart({ success, unavailable, pending }: DonutChartProps) {
  const segments = useMemo<Segment[]>(
    () => [
      { key: "success", color: theme.colors.success, value: Math.max(0, success) },
      { key: "unavailable", color: theme.colors.danger, value: Math.max(0, unavailable) },
      { key: "pending", color: theme.colors.warning, value: Math.max(0, pending) }
    ],
    [pending, success, unavailable]
  );

  const total = useMemo(() => segments.reduce((acc, item) => acc + item.value, 0), [segments]);
  const successRate = total <= 0 ? 0 : Math.round((success / total) * 100);

  const size = 188;
  const stroke = 24;
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;

  const chartSlices = useMemo(() => {
    if (total <= 0) {
      return [] as Array<{ key: string; color: string; dashArray: string; dashOffset: number }>;
    }

    let cumulative = 0;
    const computed: Array<{ key: string; color: string; dashArray: string; dashOffset: number }> = [];

    for (const slice of segments) {
      if (slice.value <= 0) {
        continue;
      }

      const length = (slice.value / total) * circumference;
      computed.push({
        key: slice.key,
        color: slice.color,
        dashArray: `${length} ${circumference}`,
        dashOffset: -cumulative
      });
      cumulative += length;
    }

    return computed;
  }, [circumference, segments, total]);

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Distribuicao por status</Text>

      <View style={styles.chartWrap}>
        <Svg width={size} height={size}>
          <G rotation="-90" origin={`${size / 2}, ${size / 2}`}>
            <Circle
              cx={size / 2}
              cy={size / 2}
              r={radius}
              stroke="rgba(17, 39, 62, 0.16)"
              strokeWidth={stroke}
              fill="transparent"
            />

            {chartSlices.length > 0 ? (
              chartSlices.map((slice) => (
                <Circle
                  key={slice.key}
                  cx={size / 2}
                  cy={size / 2}
                  r={radius}
                  stroke={slice.color}
                  strokeWidth={stroke}
                  fill="transparent"
                  strokeDasharray={slice.dashArray}
                  strokeDashoffset={slice.dashOffset}
                  strokeLinecap="butt"
                />
              ))
            ) : (
              <Circle
                cx={size / 2}
                cy={size / 2}
                r={radius}
                stroke="#B8C9D8"
                strokeWidth={stroke}
                fill="transparent"
              />
            )}
          </G>
        </Svg>

        <View style={styles.centerLabel}>
          <Text style={styles.percent}>{successRate}%</Text>
          <Text style={styles.centerHint}>sucesso</Text>
        </View>
      </View>

      <View style={styles.legendWrap}>
        <LegendItem color={theme.colors.success} label="Enviadas com sucesso" value={success} />
        <LegendItem color={theme.colors.danger} label="Indisponiveis" value={unavailable} />
        <LegendItem color={theme.colors.warning} label="Pendentes" value={pending} />
      </View>
    </View>
  );
}

interface LegendItemProps {
  color: string;
  label: string;
  value: number;
}

function LegendItem({ color, label, value }: LegendItemProps) {
  return (
    <View style={styles.legendItem}>
      <View style={[styles.legendDot, { backgroundColor: color }]} />
      <Text style={styles.legendLabel}>{label}</Text>
      <Text style={styles.legendValue}>{formatNumber(value)}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    borderRadius: theme.radius.lg,
    backgroundColor: theme.colors.panelStrong,
    borderWidth: 1,
    borderColor: theme.colors.line,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.lg,
    ...theme.shadow.card
  },
  title: {
    fontFamily: fonts.semibold,
    color: theme.colors.textPrimary,
    fontSize: 17,
    marginBottom: theme.spacing.md
  },
  chartWrap: {
    alignItems: "center",
    justifyContent: "center",
    marginBottom: theme.spacing.md
  },
  centerLabel: {
    position: "absolute",
    alignItems: "center",
    justifyContent: "center"
  },
  percent: {
    fontFamily: fonts.bold,
    color: theme.colors.textPrimary,
    fontSize: 30,
    lineHeight: 32
  },
  centerHint: {
    fontFamily: fonts.medium,
    color: theme.colors.textMuted,
    fontSize: 12,
    letterSpacing: 0.6,
    textTransform: "uppercase"
  },
  legendWrap: {
    gap: 10
  },
  legendItem: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10
  },
  legendDot: {
    width: 10,
    height: 10,
    borderRadius: 10
  },
  legendLabel: {
    flex: 1,
    fontFamily: fonts.medium,
    color: theme.colors.textMuted,
    fontSize: 13
  },
  legendValue: {
    fontFamily: fonts.bold,
    color: theme.colors.textPrimary,
    fontSize: 14
  }
});
