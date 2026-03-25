import 'package:signalr_netcore/signalr_client.dart';

class AgentRealtimeClient {
  AgentRealtimeClient({required this.baseUrl});
  final String baseUrl;
  HubConnection? _conn;

  Future<void> connect({
    required int agentId,
    required Function(Map<String, dynamic>) onChatMessage,
    Function(Map<String, dynamic>)? onOrderAssigned,
    Function(Map<String, dynamic>)? onOrderStatus,
    Function(Map<String, dynamic>)? onNotification,
  }) async {
    await disconnect();
    final url = baseUrl.replaceFirst(RegExp(r'/*$'), '') + '/hubs/notify';
    final c = HubConnectionBuilder().withUrl(url).withAutomaticReconnect().build();

    c.on('new_chat_message', (args) {
      if (args != null && args.isNotEmpty) {
        onChatMessage(Map<String, dynamic>.from(args[0] as Map));
      }
    });

    c.on('order_assigned', (args) {
      if (args != null && args.isNotEmpty && onOrderAssigned != null) {
        onOrderAssigned(Map<String, dynamic>.from(args[0] as Map));
      }
    });

    c.on('order_status', (args) {
      if (args != null && args.isNotEmpty && onOrderStatus != null) {
        onOrderStatus(Map<String, dynamic>.from(args[0] as Map));
      }
    });

    c.on('pending_order_accepted', (args) {
      if (args != null && args.isNotEmpty && onOrderStatus != null) {
        onOrderStatus(args[0] is Map ? Map<String, dynamic>.from(args[0] as Map) : {});
      }
    });

    c.on('notification', (args) {
      if (args != null && args.isNotEmpty && onNotification != null) {
        onNotification(Map<String, dynamic>.from(args[0] as Map));
      }
    });

    await c.start();
    await c.invoke('JoinAgent', args: [agentId]);
    _conn = c;
  }

  Future<void> disconnect() async {
    try {
      await _conn?.stop();
    } catch (_) {}
    _conn = null;
  }
}
