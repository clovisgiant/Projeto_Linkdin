import { useEffect, useMemo, useRef } from "react";
import { Animated, StyleSheet, Text, View, ViewStyle } from "react-native";
import { fonts, theme } from "../constants/theme";
import { RuntimeStatus } from "../types/dashboard";

interface StatusBadgeProps {
  runtime: RuntimeStatus;
  style?: ViewStyle;
}

function getBadgeState(runtime: RuntimeStatus): {
  label: string;
  tone: "running" | "waiting" | "offline";
  pulse: boolean;
} {
  if (runtime.state === "waiting") {
    return {
      label: "Aguardando proximo ciclo",
      tone: "waiting",
      pulse: false
    };
  }

  if (runtime.state === "running" || (runtime.running && runtime.state === "offline")) {
    return {
      label: "Executando ciclo",
      tone: "running",
      pulse: true
    };
  }

  return {
    label: "Automacao offline",
    tone: "offline",
    pulse: false
  };
}

export function StatusBadge({ runtime, style }: StatusBadgeProps) {
  const model = useMemo(() => getBadgeState(runtime), [runtime]);
  const pulse = useRef(new Animated.Value(1)).current;

  useEffect(() => {
    pulse.stopAnimation();
    pulse.setValue(1);

    if (!model.pulse) {
      return;
    }

    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(pulse, {
          toValue: 1.45,
          duration: 700,
          useNativeDriver: true
        }),
        Animated.timing(pulse, {
          toValue: 1,
          duration: 700,
          useNativeDriver: true
        })
      ])
    );

    loop.start();

    return () => {
      loop.stop();
      pulse.setValue(1);
    };
  }, [model.pulse, pulse]);

  return (
    <View style={[styles.badge, styles[`${model.tone}Badge`], style]}>
      <Animated.View style={[styles.dot, styles[`${model.tone}Dot`], { transform: [{ scale: pulse }] }]} />
      <Text style={styles.label}>{model.label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: {
    flexDirection: "row",
    alignItems: "center",
    gap: theme.spacing.sm,
    borderWidth: 1,
    borderRadius: theme.radius.full,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: 8
  },
  label: {
    color: theme.colors.badgeText,
    fontFamily: fonts.medium,
    fontSize: 13
  },
  dot: {
    width: 8,
    height: 8,
    borderRadius: 8
  },
  runningBadge: {
    backgroundColor: "rgba(27, 127, 69, 0.28)",
    borderColor: "rgba(27, 127, 69, 0.85)"
  },
  waitingBadge: {
    backgroundColor: "rgba(181, 110, 0, 0.28)",
    borderColor: "rgba(224, 162, 48, 0.95)"
  },
  offlineBadge: {
    backgroundColor: "rgba(94, 122, 149, 0.26)",
    borderColor: "rgba(142, 165, 189, 0.9)"
  },
  runningDot: {
    backgroundColor: "#66E39E"
  },
  waitingDot: {
    backgroundColor: "#FFD074"
  },
  offlineDot: {
    backgroundColor: "#C9D9E7"
  }
});
