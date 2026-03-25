import 'dart:async';
import 'package:flutter/material.dart';
import '../services/agent_api.dart';
import '../models/agent_state.dart';
import 'agent_order_detail_screen.dart';

class AgentIncomingOrdersScreen extends StatefulWidget {
  final AgentApi api;
  final AgentState state;

  const AgentIncomingOrdersScreen({
    super.key,
    required this.api,
    required this.state,
  });

  @override
  State<AgentIncomingOrdersScreen> createState() => _AgentIncomingOrdersScreenState();
}

class _AgentIncomingOrdersScreenState extends State<AgentIncomingOrdersScreen> {
  List<dynamic> _pendingOrders = [];
  bool _loading = true;
  Timer? _refreshTimer;
  Timer? _countdownTimer;
  int _lastRefreshSeq = -1;

  @override
  void initState() {
    super.initState();
    _loadPendingOrders();
    
    _refreshTimer = Timer.periodic(const Duration(seconds: 30), (_) => _loadPendingOrders());
    
    _countdownTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) {
        // تحقق من تحديث realtime
        final seq = widget.state.orderRefreshSeq;
        if (seq != _lastRefreshSeq) {
          _lastRefreshSeq = seq;
          _loadPendingOrders();
        }
        setState(() {});
      }
    });
  }

  @override
  void dispose() {
    _refreshTimer?.cancel();
    _countdownTimer?.cancel();
    super.dispose();
  }

  Future<void> _loadPendingOrders() async {
    try {
      final orders = await widget.api.getPendingOrders(widget.state.token ?? "");
      if (mounted) setState(() {
        _pendingOrders = orders;
        _loading = false;
      });
    } catch (e) {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _acceptOrder(int orderId) async {
    try {
      await widget.api.acceptOrder(widget.state.token ?? "", orderId);
      _loadPendingOrders();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('✅ تم قبول الطلب'), backgroundColor: Colors.green),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('خطأ: $e'), backgroundColor: Colors.red),
        );
      }
    }
  }

  Future<void> _rejectOrder(int orderId) async {
    final reasonController = TextEditingController();
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('رفض الطلب'),
        content: TextField(
          controller: reasonController,
          decoration: const InputDecoration(
            labelText: 'سبب الرفض',
            hintText: 'أدخل سبب الرفض...',
            border: OutlineInputBorder(),
          ),
          maxLines: 3,
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('إلغاء')),
          TextButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('رفض', style: TextStyle(color: Colors.red)),
          ),
        ],
      ),
    );

    if (confirmed != true) return;
    
    try {
      await widget.api.rejectOrder(widget.state.token ?? "", orderId, reasonController.text.trim());
      _loadPendingOrders();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('تم رفض الطلب'), backgroundColor: const Color(0xFFD4AF37)),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('خطأ: $e'), backgroundColor: Colors.red),
        );
      }
    }
  }

  String _formatCountdown(int seconds) {
    if (seconds <= 0) return 'انتهى الوقت';
    final m = seconds ~/ 60;
    final s = seconds % 60;
    return '${m.toString().padLeft(2,'0')}:${s.toString().padLeft(2,'0')}';
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    
    return Scaffold(
      appBar: AppBar(
        title: Row(children: [
          const Text('الطلبات الواردة'),
          if (_pendingOrders.isNotEmpty) ...[
            const SizedBox(width: 8),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: Colors.red, borderRadius: BorderRadius.circular(12)),
              child: Text(
                '${_pendingOrders.length}',
                style: const TextStyle(color: Colors.white, fontSize: 12),
              ),
            ),
          ],
        ]),
        actions: [
          IconButton(icon: const Icon(Icons.refresh), onPressed: _loadPendingOrders),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _pendingOrders.isEmpty
              ? Center(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(Icons.inbox_outlined, size: 64, color: cs.onSurface.withOpacity(0.3)),
                      const SizedBox(height: 16),
                      Text('لا توجد طلبات واردة', style: TextStyle(color: cs.onSurface.withOpacity(0.5), fontSize: 16)),
                    ],
                  ),
                )
              : RefreshIndicator(
                  onRefresh: _loadPendingOrders,
                  child: ListView.builder(
                    padding: const EdgeInsets.all(16),
                    itemCount: _pendingOrders.length,
                    itemBuilder: (ctx, i) {
                      final order = _pendingOrders[i];
                      final seconds = (order['secondsRemaining'] as int? ?? 0);
                      final isUrgent = seconds < 300; 
                      
                      return Card(
                        margin: const EdgeInsets.only(bottom: 16),
                        elevation: 3,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(16),
                          side: BorderSide(
                            color: isUrgent ? Colors.red.withOpacity(0.5) : Colors.transparent,
                            width: 2,
                          ),
                        ),
                        child: InkWell(
                          borderRadius: BorderRadius.circular(16),
                          onTap: () {
                            Navigator.push(ctx, MaterialPageRoute(
                              builder: (_) => AgentOrderDetailScreen(
                                api: widget.api,
                                state: widget.state,
                                orderId: order['orderId'] as int,
                              ),
                            )).then((_) => _loadPendingOrders());
                          },
                          child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              
                              Row(
                                children: [
                                  Text(
                                    'طلب #${order['orderId']}',
                                    style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16),
                                  ),
                                  const Spacer(),
                                  Container(
                                    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                                    decoration: BoxDecoration(
                                      color: isUrgent ? Colors.red : Colors.amber,
                                      borderRadius: BorderRadius.circular(20),
                                    ),
                                    child: Row(children: [
                                      Icon(Icons.timer, size: 14, color: Colors.white),
                                      const SizedBox(width: 4),
                                      Text(
                                        _formatCountdown(seconds),
                                        style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 13),
                                      ),
                                    ]),
                                  ),
                                ],
                              ),
                              const SizedBox(height: 12),

                              ...((order['items'] as List? ?? []).map((item) => Padding(
                                padding: const EdgeInsets.symmetric(vertical: 3),
                                child: Row(children: [
                                  Icon(Icons.fiber_manual_record, size: 8, color: cs.primary),
                                  const SizedBox(width: 8),
                                  Expanded(child: Text(item['productNameSnapshot']?.toString() ?? '')),
                                  Text('× ${item['quantity']}', style: TextStyle(color: cs.primary, fontWeight: FontWeight.bold)),
                                ]),
                              ))),
                              
                              const Divider(height: 20),

                              Row(children: [
                                const Text('إجمالي حصتك:', style: TextStyle(fontWeight: FontWeight.w500)),
                                const Spacer(),
                                Text(
                                  '${order['agentSubtotal']} ل.س',
                                  style: TextStyle(fontWeight: FontWeight.bold, color: cs.primary, fontSize: 16),
                                ),
                              ]),
                              
                              const SizedBox(height: 16),

                              Row(children: [
                                Expanded(
                                  child: OutlinedButton.icon(
                                    onPressed: () => _rejectOrder(order['orderId'] as int),
                                    icon: const Icon(Icons.close, color: Colors.red),
                                    label: const Text('رفض', style: TextStyle(color: Colors.red)),
                                    style: OutlinedButton.styleFrom(
                                      side: const BorderSide(color: Colors.red),
                                      padding: const EdgeInsets.symmetric(vertical: 12),
                                    ),
                                  ),
                                ),
                                const SizedBox(width: 12),
                                Expanded(
                                  child: ElevatedButton.icon(
                                    onPressed: () => _acceptOrder(order['orderId'] as int),
                                    icon: const Icon(Icons.check, color: Colors.white),
                                    label: const Text('قبول', style: TextStyle(color: Colors.white)),
                                    style: ElevatedButton.styleFrom(
                                      backgroundColor: Colors.green,
                                      padding: const EdgeInsets.symmetric(vertical: 12),
                                    ),
                                  ),
                                ),
                              ]),
                            ],
                          ),
                        ),
                      ),  // InkWell closing
                      );
                    },
                  ),
                ),
    );
  }
}
