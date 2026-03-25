import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_foreground_task/flutter_foreground_task.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'screens/driver_splash.dart';
import 'models/brand_state.dart';
import 'theme/driver_theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final prefs = await SharedPreferences.getInstance();

  if (!kIsWeb) {
    FlutterForegroundTask.init(
      androidNotificationOptions: AndroidNotificationOptions(
        channelId: 'driver_tracking_channel',
        channelName: 'Driver Tracking',
        channelDescription: 'تتبع السائق بالخلفية لضمان الاستقرار',
        channelImportance: NotificationChannelImportance.LOW,
        priority: NotificationPriority.LOW,
        enableVibration: false,
        playSound: false,
      ),
      iosNotificationOptions: const IOSNotificationOptions(
        showNotification: true,
        playSound: false,
      ),
      foregroundTaskOptions: ForegroundTaskOptions(
        interval: 5000,
        isOnceEvent: false,
        autoRunOnBoot: false,
        allowWakeLock: true,
        allowWifiLock: true,
      ),
    );
  }
  runApp(DriverApp(prefs: prefs));
}

class DriverApp extends StatelessWidget {
  const DriverApp({super.key, required this.prefs});
  final SharedPreferences prefs;

  @override
  Widget build(BuildContext context) {
    final brand = BrandState();
    brand.init(prefs);
    return AnimatedBuilder(
      animation: brand,
      builder: (context, _) {
        return MaterialApp(
          debugShowCheckedModeBanner: false,
          title: brand.storeName.isNotEmpty ? '${brand.storeName} — السائق' : 'السائق',
          locale: const Locale('ar'),
          theme: DriverTheme.lightTheme(DriverTheme.primaryRed),
          darkTheme: DriverTheme.darkTheme(DriverTheme.primaryRed),
          themeMode: brand.isDarkMode ? ThemeMode.dark : ThemeMode.light,
          builder: (context, child) {
            return WithForegroundTask(
              child: Directionality(textDirection: TextDirection.rtl, child: child ?? const SizedBox()),
            );
          },
          home: DriverSplash(prefs: prefs, brand: brand),
        );
      },
    );
  }
}
