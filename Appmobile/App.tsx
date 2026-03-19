import {
  SpaceGrotesk_400Regular,
  SpaceGrotesk_500Medium,
  SpaceGrotesk_600SemiBold,
  SpaceGrotesk_700Bold
} from "@expo-google-fonts/space-grotesk";
import { useFonts } from "expo-font";
import { StatusBar } from "expo-status-bar";
import { ActivityIndicator, StyleSheet, View } from "react-native";
import { theme } from "./src/constants/theme";
import { DashboardScreen } from "./src/screens/DashboardScreen";

export default function App() {
  const [fontsLoaded] = useFonts({
    SpaceGrotesk_400Regular,
    SpaceGrotesk_500Medium,
    SpaceGrotesk_600SemiBold,
    SpaceGrotesk_700Bold
  });

  if (!fontsLoaded) {
    return (
      <View style={styles.loadingWrap}>
        <ActivityIndicator size="small" color={theme.colors.panelStrong} />
      </View>
    );
  }

  return (
    <>
      <StatusBar style="light" />
      <DashboardScreen />
    </>
  );
}

const styles = StyleSheet.create({
  loadingWrap: {
    flex: 1,
    backgroundColor: theme.colors.bgTop,
    alignItems: "center",
    justifyContent: "center"
  }
});
