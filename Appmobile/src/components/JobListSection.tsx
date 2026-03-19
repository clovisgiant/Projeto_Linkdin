import { StyleSheet, Text, View } from "react-native";
import { fonts, theme } from "../constants/theme";

export interface JobRow {
  primary: string;
  secondary: string;
  meta: string;
  tone?: "neutral" | "success" | "danger";
}

interface JobListSectionProps {
  title: string;
  subtitle: string;
  emptyText: string;
  rows: JobRow[];
}

export function JobListSection({ title, subtitle, emptyText, rows }: JobListSectionProps) {
  return (
    <View style={styles.section}>
      <View style={styles.header}>
        <Text style={styles.title}>{title}</Text>
        <Text style={styles.count}>{rows.length}</Text>
      </View>
      <Text style={styles.subtitle}>{subtitle}</Text>

      {rows.length === 0 ? (
        <View style={styles.emptyWrap}>
          <Text style={styles.emptyText}>{emptyText}</Text>
        </View>
      ) : (
        <View style={styles.listWrap}>
          {rows.map((row, index) => (
            <View key={`${title}-${index}`} style={styles.rowItem}>
              <Text style={styles.rowPrimary} numberOfLines={2}>
                {row.primary}
              </Text>
              <Text style={styles.rowSecondary} numberOfLines={1}>
                {row.secondary}
              </Text>
              <Text style={[styles.rowMeta, row.tone ? styles[`${row.tone}Meta`] : null]} numberOfLines={2}>
                {row.meta}
              </Text>
            </View>
          ))}
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  section: {
    borderRadius: theme.radius.lg,
    backgroundColor: theme.colors.panelStrong,
    borderWidth: 1,
    borderColor: theme.colors.line,
    padding: theme.spacing.md,
    ...theme.shadow.card
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  title: {
    flex: 1,
    fontFamily: fonts.semibold,
    color: theme.colors.textPrimary,
    fontSize: 17,
    marginRight: 8
  },
  count: {
    minWidth: 28,
    borderRadius: theme.radius.full,
    paddingHorizontal: 10,
    paddingVertical: 5,
    textAlign: "center",
    fontFamily: fonts.bold,
    fontSize: 12,
    color: theme.colors.textPrimary,
    backgroundColor: "#E8F1FB"
  },
  subtitle: {
    fontFamily: fonts.regular,
    color: theme.colors.textMuted,
    fontSize: 12,
    marginTop: 5,
    marginBottom: theme.spacing.md
  },
  emptyWrap: {
    borderRadius: theme.radius.md,
    backgroundColor: "#F2F7FD",
    borderWidth: 1,
    borderColor: theme.colors.line,
    padding: theme.spacing.md
  },
  emptyText: {
    fontFamily: fonts.medium,
    color: theme.colors.textMuted,
    fontSize: 13
  },
  listWrap: {
    gap: 10
  },
  rowItem: {
    borderRadius: theme.radius.md,
    borderWidth: 1,
    borderColor: "#E0EBF5",
    backgroundColor: "#F7FBFF",
    padding: theme.spacing.md
  },
  rowPrimary: {
    fontFamily: fonts.semibold,
    color: theme.colors.textPrimary,
    fontSize: 14,
    marginBottom: 4
  },
  rowSecondary: {
    fontFamily: fonts.regular,
    color: theme.colors.textMuted,
    fontSize: 13,
    marginBottom: 4
  },
  rowMeta: {
    fontFamily: fonts.medium,
    color: theme.colors.info,
    fontSize: 12
  },
  neutralMeta: {
    color: theme.colors.info
  },
  successMeta: {
    color: theme.colors.success
  },
  dangerMeta: {
    color: theme.colors.danger
  }
});
