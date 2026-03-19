export const theme = {
  colors: {
    bgTop: "#071427",
    bgBottom: "#122640",
    panel: "#F4F9FF",
    panelStrong: "#FFFFFF",
    line: "#D2E1F0",
    textPrimary: "#11273E",
    textMuted: "#5A748C",
    success: "#1B7F45",
    danger: "#B12A42",
    warning: "#B56E00",
    info: "#2F6FB4",
    waiting: "#E0A230",
    offline: "#8EA5BD",
    accent: "#0AA7A6",
    badgeText: "#F8FCFF"
  },
  spacing: {
    xs: 6,
    sm: 10,
    md: 14,
    lg: 18,
    xl: 24,
    xxl: 30
  },
  radius: {
    sm: 12,
    md: 16,
    lg: 22,
    full: 999
  },
  shadow: {
    card: {
      shadowColor: "#071427",
      shadowOpacity: 0.14,
      shadowOffset: { width: 0, height: 8 },
      shadowRadius: 16,
      elevation: 4
    }
  }
} as const;

export const fonts = {
  regular: "SpaceGrotesk_400Regular",
  medium: "SpaceGrotesk_500Medium",
  semibold: "SpaceGrotesk_600SemiBold",
  bold: "SpaceGrotesk_700Bold"
} as const;
