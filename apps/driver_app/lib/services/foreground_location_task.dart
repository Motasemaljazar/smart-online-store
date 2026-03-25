import 'dart:async';
import 'dart:isolate';

import 'package:flutter_foreground_task/flutter_foreground_task.dart';
import 'package:geolocator/geolocator.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../app_config.dart';

import 'api.dart';
import 'location_sender.dart';

class DriverForegroundTaskHandler extends TaskHandler {
  SharedPreferences? _prefs;
  LocationSender? _sender;
  Timer? _timer;

  @override
  Future<void> onStart(DateTime timestamp, SendPort? sendPort) async {
    _prefs = await SharedPreferences.getInstance();
    final token = _prefs!.getString('driverToken');
    final baseUrl = kBackendBaseUrl;
    if (token == null || token.trim().isEmpty) return;

    final api = DriverApi(baseUrl: baseUrl);
    _sender = LocationSender(api: api, prefs: _prefs!, driverToken: token);
    _sender!.startAutoFlush();

    _timer = Timer.periodic(const Duration(seconds: 8), (_) async {
      try {
        final enabled = await Geolocator.isLocationServiceEnabled();
        if (!enabled) return;
        final perm = await Geolocator.checkPermission();
        if (perm == LocationPermission.denied || perm == LocationPermission.deniedForever) return;

        final pos = await Geolocator.getCurrentPosition(desiredAccuracy: LocationAccuracy.high)
            .timeout(const Duration(seconds: 8));
        final gpsMps = pos.speed.isFinite ? pos.speed : 0.0;
        await _sender!.sendOrQueue(
          lat: pos.latitude,
          lng: pos.longitude,
          speedMps: (gpsMps >= 0) ? gpsMps : 0,
          headingDeg: pos.heading.isFinite ? pos.heading : 0,
          accuracyMeters: pos.accuracy.isFinite ? pos.accuracy : 0,
        );

        await FlutterForegroundTask.updateService(
          notificationTitle: 'تتبع السائق يعمل',
          notificationText: 'آخر تحديث: ${DateTime.now().toLocal().toString().substring(11, 19)}',
        );
      } catch (_) {
        
      }
    });
  }

  @override
  Future<void> onRepeatEvent(DateTime timestamp, SendPort? sendPort) async {
    
  }

  @override
  Future<void> onDestroy(DateTime timestamp, SendPort? sendPort) async {
    _timer?.cancel();
    _sender?.stopAutoFlush();
  }
}

@pragma('vm:entry-point')
void startDriverForegroundService() {
  FlutterForegroundTask.setTaskHandler(DriverForegroundTaskHandler());
}
