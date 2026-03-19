import { useEffect, useMemo, useRef } from "react";
import { Animated, Easing, StyleSheet, Text, View } from "react-native";
import { fonts, theme } from "../constants/theme";
import { RuntimeStatus } from "../types/dashboard";
import { getRuntimeVisual } from "../utils/format";

interface RuntimeClockCardProps {
  runtime: RuntimeStatus;
}

export function RuntimeClockCard({ runtime }: RuntimeClockCardProps) {
  const visual = useMemo(() => getRuntimeVisual(runtime), [runtime]);
  const minuteSpin = useRef(new Animated.Value(0)).current;
  const breathe = useRef(new Animated.Value(1)).current;

  useEffect(() => {
    minuteSpin.stopAnimation();
    breathe.stopAnimation();
    minuteSpin.setValue(0);
    breathe.setValue(1);

    let spinLoop: Animated.CompositeAnimation | null = null;
    let breatheLoop: Animated.CompositeAnimation | null = null;

    if (runtime.state === "running") {
      spinLoop = Animated.loop(
        Animated.timing(minuteSpin, {
          toValue: 1,
          duration: 5600,
          easing: Easing.linear,
          useNativeDriver: true
        })
      );

      breatheLoop = Animated.loop(
        Animated.sequence([
          Animated.timing(breathe, {
            toValue: 1.05,
            duration: 1100,
            useNativeDriver: true
          }),
          Animated.timing(breathe, {
            toValue: 0.98,
            duration: 1100,
            useNativeDriver: true
          })
        ])
      );

      spinLoop.start();
      breatheLoop.start();
    } else if (runtime.state === "waiting") {
      breatheLoop = Animated.loop(
        Animated.sequence([
          Animated.timing(breathe, {
            toValue: 1.03,
            duration: 1400,
            useNativeDriver: true
          }),
          Animated.timing(breathe, {
            toValue: 0.99,
            duration: 1400,
            useNativeDriver: true
          })
        ])
      );

      breatheLoop.start();
    }

    return () => {
      spinLoop?.stop();
      breatheLoop?.stop();
    };
  }, [breathe, minuteSpin, runtime.state]);

  const minuteRotation =
    runtime.state === "running"
      ? minuteSpin.interpolate({
          inputRange: [0, 1],
          outputRange: ["0deg", "360deg"]
        })
      : runtime.state === "waiting"
        ? "122deg"
        : "0deg";

  const hourRotation = runtime.state === "running" ? "46deg" : runtime.state === "waiting" ? "20deg" : "0deg";

  const minuteLength = 30;
  const hourLength = 20;

  return (
    <View style={[styles.card, styles[`${visual.tone}Card`]]}>
      <Animated.View style={[styles.clock, styles[`${visual.tone}Clock`], { transform: [{ scale: breathe }] }]}>
        <View style={styles.ring} />

        <Animated.View
          style={[
            styles.hand,
            styles.hourHand,
            {
              height: hourLength,
              transform: [{ translateY: hourLength / 2 }, { rotate: hourRotation }, { translateY: -hourLength / 2 }]
            }
          ]}
        />

        <Animated.View
          style={[
            styles.hand,
            styles.minuteHand,
            {
              height: minuteLength,
              transform: [{ translateY: minuteLength / 2 }, { rotate: minuteRotation }, { translateY: -minuteLength / 2 }]
            }
          ]}
        />

        <View style={styles.hub} />
        <Text style={styles.code}>{visual.badge}</Text>
      </Animated.View>

      <Text style={styles.title}>{visual.title}</Text>
      <Text style={styles.detail}>{visual.detail}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: theme.radius.lg,
    paddingVertical: theme.spacing.lg,
    paddingHorizontal: theme.spacing.md,
    alignItems: "center",
    borderWidth: 1,
    ...theme.shadow.card
  },
  runningCard: {
    borderColor: "rgba(27, 127, 69, 0.36)",
    backgroundColor: "rgba(234, 249, 240, 0.92)"
  },
  waitingCard: {
    borderColor: "rgba(181, 110, 0, 0.4)",
    backgroundColor: "rgba(255, 245, 230, 0.94)"
  },
  offlineCard: {
    borderColor: "rgba(122, 146, 170, 0.35)",
    backgroundColor: "rgba(236, 244, 252, 0.94)"
  },
  clock: {
    width: 128,
    height: 128,
    borderRadius: 80,
    alignItems: "center",
    justifyContent: "center",
    marginBottom: theme.spacing.md,
    overflow: "hidden"
  },
  runningClock: {
    backgroundColor: "#DAF3E4"
  },
  waitingClock: {
    backgroundColor: "#FFF0D3"
  },
  offlineClock: {
    backgroundColor: "#E6EEF8"
  },
  ring: {
    position: "absolute",
    width: 110,
    height: 110,
    borderRadius: 80,
    borderWidth: 1,
    borderColor: "rgba(17, 39, 62, 0.16)"
  },
  hand: {
    position: "absolute",
    left: "50%",
    marginLeft: -2,
    bottom: "50%",
    borderRadius: 4
  },
  hourHand: {
    width: 4,
    backgroundColor: "#1B4568"
  },
  minuteHand: {
    width: 3,
    backgroundColor: "#2D6CA0"
  },
  hub: {
    width: 12,
    height: 12,
    borderRadius: 12,
    backgroundColor: "#11273E",
    borderWidth: 2,
    borderColor: "#FFFFFF"
  },
  code: {
    position: "absolute",
    bottom: 16,
    fontFamily: fonts.bold,
    fontSize: 11,
    letterSpacing: 1,
    color: "#11273E"
  },
  title: {
    fontFamily: fonts.semibold,
    color: theme.colors.textPrimary,
    fontSize: 17,
    marginBottom: 6
  },
  detail: {
    fontFamily: fonts.regular,
    color: theme.colors.textMuted,
    fontSize: 12,
    textAlign: "center",
    lineHeight: 17
  }
});
