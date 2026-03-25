import 'dart:convert';
import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'api.dart';

class DriverPushService {
  DriverPushService({required this.api, required this.platformTag});
  final DriverApi api;
  final String platformTag;

  final FlutterLocalNotificationsPlugin _local = FlutterLocalNotificationsPlugin();
  bool _initialized = false;

  Future<void> initForDriver({required String driverToken}) async {
    if (kIsWeb || _initialized) return;

    try {
      
      const android = AndroidInitializationSettings('@mipmap/ic_launcher');
      const iOS = DarwinInitializationSettings(
        requestAlertPermission: true,
        requestBadgePermission: true,
        requestSoundPermission: true,
      );

      await _local.initialize(
        const InitializationSettings(android: android, iOS: iOS),
        onDidReceiveNotificationResponse: (response) {
          debugPrint('[Push] notification tapped: ${response.payload}');
        },
      );

      if (Platform.isAndroid) {
        final androidPlugin = _local.resolvePlatformSpecificImplementation<
            AndroidFlutterLocalNotificationsPlugin>();
        await androidPlugin?.requestNotificationsPermission();

        const channel = AndroidNotificationChannel(
          'orders',
          'طلبات التوصيل',
          description: 'إشعارات مهام التوصيل',
          importance: Importance.max,
          playSound: true,
          enableVibration: true,
          showBadge: true,
        );
        await androidPlugin?.createNotificationChannel(channel);
      }

      _initialized = true;
      debugPrint('[Push] initialized');
    } catch (e) {
      debugPrint('[Push] Init failed: $e');
    }
  }

  Future<void> showLocalNotification({
    required String title,
    required String body,
    Map<String, dynamic>? data,
  }) async {
    if (!_initialized) return;
    try {
      await _local.show(
        DateTime.now().millisecondsSinceEpoch & 0x7FFFFFFF,
        title,
        body,
        const NotificationDetails(
          android: AndroidNotificationDetails(
            'orders',
            'طلبات التوصيل',
            importance: Importance.max,
            priority: Priority.high,
            playSound: true,
            enableVibration: true,
            icon: '@mipmap/ic_launcher',
          ),
          iOS: DarwinNotificationDetails(
            presentAlert: true,
            presentBadge: true,
            presentSound: true,
          ),
        ),
        payload: data != null ? jsonEncode(data) : null,
      );
    } catch (e) {
      debugPrint('[Push] Show notification failed: $e');
    }
  }

  Future<void> refreshToken(String driverToken) async {
    
    debugPrint('[Push] refreshToken: no-op (local mode)');
  }
}
