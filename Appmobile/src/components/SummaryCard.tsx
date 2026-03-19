import { StyleSheet, Text, View } from "react-native";
import { fonts, theme } from "../constants/theme";
import { formatNumber } from "../utils/format";

interface SummaryCardProps {
  title: string;
  value: number;
  hint: string;
  tone: "total" | "success" | "danger" | "pending";
}

const toneColors = {
  total: {
    border: "#2F6FB4",
    background: "#EDF5FF"
  },
  success: {
    border: "#1B7F45",
    background: "#EAF9F0"
  },
  danger: {
    border: "#B12A42",
    background: "#FFEFF3"
  },
  pending: {
    border: "#B56E00",
    background: "#FFF5E6"
  }
} as const;

export function SummaryCard({ title, value, hint, tone }: SummaryCardProps) {
  return (
    <View
      style={[
        styles.card,
        {
          borderColor: toneColors[tone].border,
          backgroundColor: toneColors[tone].background
        }
      ]}
    >
      <Text style={styles.title}>{title}</Text>
      <Text style={styles.value}>{formatNumber(value)}</Text>
      <Text style={styles.hint}>{hint}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    width: "48.2%",
    borderRadius: theme.radius.md,
    borderWidth: 1,
    padding: theme.spacing.md,
    ...theme.shadow.card
  },
  title: {
    color: theme.colors.textMuted,
    fontSize: 12,
    fontFamily: fonts.medium,
    marginBottom: 8
  },
  value: {
    color: theme.colors.textPrimary,
    fontSize: 28,
    lineHeight: 30,
    fontFamily: fonts.bold,
    marginBottom: 6
  },
  hint: {
    color: theme.colors.textMuted,
    fontSize: 12,
    fontFamily: fonts.regular
  }
});
