import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

class AgentState extends ChangeNotifier {
  SharedPreferences? _prefs;

  String? token;
  int? agentId;
  String agentName = '';
  String agentPhone = '';
  bool isDarkMode = false;
  String storeName = '';
  String primaryColorHex = '#1976D2';

  int _chatMessageSeq = 0;
  int get chatMessageSeq => _chatMessageSeq;
  Map<String, dynamic>? lastChatMessage;

  int _orderRefreshSeq = 0;
  int get orderRefreshSeq => _orderRefreshSeq;

  void onRealtimeChatMessage(Map<String, dynamic> payload) {
    lastChatMessage = payload;
    _chatMessageSeq++;
    notifyListeners();
  }

  void onRealtimeOrderStatus(Map<String, dynamic> payload) {
    _orderRefreshSeq++;
    notifyListeners();
  }

  Future<void> init(SharedPreferences prefs) async {
    _prefs = prefs;
    token = prefs.getString('agent_token');
    agentId = prefs.getInt('agent_id');
    agentName = prefs.getString('agent_name') ?? '';
    agentPhone = prefs.getString('agent_phone') ?? '';
    isDarkMode = prefs.getBool('agent_dark_mode') ?? false;
    storeName = prefs.getString('agent_store_name') ?? '';
    notifyListeners();
  }

  Future<void> saveSession({
    required String token,
    required int agentId,
    required String name,
    required String phone,
  }) async {
    this.token = token;
    this.agentId = agentId;
    agentName = name;
    agentPhone = phone;
    await _prefs?.setString('agent_token', token);
    await _prefs?.setInt('agent_id', agentId);
    await _prefs?.setString('agent_name', name);
    await _prefs?.setString('agent_phone', phone);
    notifyListeners();
  }

  Future<void> logout() async {
    token = null;
    agentId = null;
    agentName = '';
    agentPhone = '';
    await _prefs?.remove('agent_token');
    await _prefs?.remove('agent_id');
    await _prefs?.remove('agent_name');
    await _prefs?.remove('agent_phone');
    notifyListeners();
  }

  Future<void> toggleDarkMode() async {
    isDarkMode = !isDarkMode;
    await _prefs?.setBool('agent_dark_mode', isDarkMode);
    notifyListeners();
  }

  bool get isLoggedIn => token != null && token!.isNotEmpty;
}
