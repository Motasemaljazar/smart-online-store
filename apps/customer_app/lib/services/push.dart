import 'package:flutter/foundation.dart';
import 'api.dart';

class PushService {
  PushService({required this.api, required this.platformTag});
  final ApiClient api;
  final String platformTag;

  Future<void> initForCustomer({required int customerId}) async {
    debugPrint('ℹ️ [Push] Using SignalR for real-time notifications.');
  }

  Future<void> refreshToken(int customerId) async {}
}
