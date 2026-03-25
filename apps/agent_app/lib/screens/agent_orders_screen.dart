import 'dart:async';
import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import '../services/agent_api.dart';
import '../models/agent_state.dart';
import 'agent_order_detail_screen.dart';

class AgentOrdersScreen extends StatefulWidget {
  final AgentApi api;
  final AgentState state;

  const AgentOrdersScreen({
    super.key,
    required this.api,
    required this.state,
  });

  @override
  State<AgentOrdersScreen> createState() => _AgentOrdersScreenState();
}

class _AgentOrdersScreenState extends State<AgentOrdersScreen>
    with SingleTickerProviderStateMixin {
  late TabController _tabController;
  List<dynamic> _pendingOrders = [];
  List<dynamic> _activeOrders = [];
  List<dynamic> _historyOrders = [];
  bool _loading = true;
  Timer? _refreshTimer;
  Timer? _countdownTimer;
  int _lastRefreshSeq = -1;

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: 3, vsync: this);
    _loadAll();
    _refreshTimer = Timer.periodic(const Duration(seconds: 20), (_) => _loadAll(silent: true));
    _countdownTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) {
        // تحقق من تحديث realtime وأعد تحميل البيانات فوراً
        final seq = widget.state.orderRefreshSeq;
        if (seq != _lastRefreshSeq) {
          _lastRefreshSeq = seq;
          _loadAll(silent: true);
        }
        setState(() {});
      }
    });
  }

  @override
  void dispose() {
    _tabController.dispose();
    _refreshTimer?.cancel();
    _countdownTimer?.cancel();
    super.dispose();
  }

  Future<void> _loadAll({bool silent = false}) async {
    if (!silent && mounted) setState(() => _loading = true);
    try {
      final token = widget.state.token ?? '';
      final results = await Future.wait([
        widget.api.getPendingOrders(token),
        widget.api.getActiveOrders(token),
        widget.api.getOrderHistory(token),
      ]);
      if (mounted) {
        setState(() {
          _pendingOrders = results[0];
          _activeOrders = results[1];
          _historyOrders = results[2];
          _loading = false;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _acceptOrder(int orderId) async {
    try {
      await widget.api.acceptOrder(widget.state.token ?? '', orderId);
      await _loadAll(silent: true);
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
        title: Text('رفض الطلب', style: GoogleFonts.cairo(fontWeight: FontWeight.w700)),
        content: TextField(
          controller: reasonController,
          decoration: const InputDecoration(
            labelText: 'سبب الرفض',
            border: OutlineInputBorder(),
          ),
          maxLines: 3,
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: Text('إلغاء', style: GoogleFonts.cairo())),
          TextButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: Text('رفض', style: GoogleFonts.cairo(color: Colors.red)),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
    try {
      await widget.api.rejectOrder(widget.state.token ?? '', orderId, reasonController.text.trim());
      await _loadAll(silent: true);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('تم رفض الطلب')),
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

  Future<void> _showStatusDialog(int orderId) async {
    final statuses = [
      {'label': 'تم التأكيد', 'value': 1},
      {'label': 'قيد المعالجة', 'value': 2},
      {'label': 'جاهز للاستلام', 'value': 3},
      {'label': 'مع السائق', 'value': 4},
      {'label': 'تم التسليم', 'value': 5},
      {'label': 'ملغى', 'value': 6},
    ];

    final selected = await showDialog<int>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('تغيير حالة الطلب #$orderId', style: GoogleFonts.cairo(fontWeight: FontWeight.w700)),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: statuses.map((s) => ListTile(
            title: Text(s['label'] as String, style: GoogleFonts.cairo()),
            onTap: () => Navigator.pop(ctx, s['value'] as int),
          )).toList(),
        ),
      ),
    );
    if (selected == null) return;
    try {
      await widget.api.updateOrderStatus(widget.state.token ?? '', orderId, selected);
      await _loadAll(silent: true);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('✅ تم تحديث الحالة'), backgroundColor: Colors.green),
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

  Future<void> _showAssignDriverDialog(int orderId) async {
    List<dynamic> drivers = [];
    try {
      drivers = await widget.api.getAvailableDrivers(widget.state.token ?? '');
    } catch (_) {}

    if (!mounted) return;
    if (drivers.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('لا يوجد سائقون متاحون')),
      );
      return;
    }

    final selected = await showDialog<int>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('تعيين سائق للطلب #$orderId', style: GoogleFonts.cairo(fontWeight: FontWeight.w700)),
        content: SizedBox(
          width: double.maxFinite,
          child: ListView(
            shrinkWrap: true,
            children: drivers.map<Widget>((d) => ListTile(
              leading: const Icon(Icons.person_outline_rounded),
              title: Text(d['name']?.toString() ?? '', style: GoogleFonts.cairo()),
              subtitle: Text(d['phone']?.toString() ?? '', style: GoogleFonts.cairo()),
              onTap: () => Navigator.pop(ctx, d['id'] as int),
            )).toList(),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: Text('إلغاء', style: GoogleFonts.cairo()),
          ),
        ],
      ),
    );

    if (selected == null) return;
    try {
      await widget.api.assignDriver(widget.state.token ?? '', orderId, selected);
      await _loadAll(silent: true);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('✅ تم تعيين السائق'), backgroundColor: Colors.green),
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

  Future<void> _showEtaDialog(int orderId) async {
    final deliveryCtl = TextEditingController();
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('تحديد وقت التوصيل للطلب #$orderId', style: GoogleFonts.cairo(fontWeight: FontWeight.w700)),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: deliveryCtl,
              keyboardType: TextInputType.number,
              decoration: const InputDecoration(
                labelText: 'وقت التوصيل (دقيقة)',
                border: OutlineInputBorder(),
                suffixText: 'دقيقة',
              ),
            ),
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: Text('إلغاء', style: GoogleFonts.cairo())),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: Text('حفظ', style: GoogleFonts.cairo()),
          ),
        ],
      ),
    );

    if (confirmed != true) return;
    final deliveryEta = int.tryParse(deliveryCtl.text.trim());
    if (deliveryEta == null) return;

    try {
      await widget.api.setEta(
        widget.state.token ?? '',
        orderId,
        deliveryEta: deliveryEta,
      );
      await _loadAll(silent: true);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('✅ تم تحديث وقت التوصيل'), backgroundColor: Colors.green),
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
    return '${m.toString().padLeft(2, '0')}:${s.toString().padLeft(2, '0')}';
  }

  String _statusLabel(dynamic status) {
    if (status is int) {
      switch (status) {
        case 0: return 'جديد';
        case 1: return 'مؤكد';
        case 2: return 'قيد المعالجة';
        case 3: return 'جاهز للاستلام';
        case 4: return 'مع السائق';
        case 5: return 'تم التسليم';
        case 6: return 'ملغى';
        case 7: return 'مقبول';
      }
    }
    final s = status?.toString() ?? '';
    switch (s) {
      case 'New': return 'جديد';
      case 'Confirmed': return 'مؤكد';
      case 'Preparing': return 'قيد المعالجة';
      case 'ReadyForPickup': return 'جاهز للاستلام';
      case 'WithDriver': return 'مع السائق';
      case 'Delivered': return 'تم التسليم';
      case 'Cancelled': return 'ملغى';
      case 'Accepted': return 'مقبول';
      default: return s;
    }
  }

  Color _statusColor(dynamic status) {
    final s = status?.toString() ?? '';
    if (s == 'Delivered' || s == '5') return Colors.green;
    if (s == 'Cancelled' || s == '6') return Colors.red;
    if (s == 'WithDriver' || s == '4') return Colors.blue;
    if (s == 'ReadyForPickup' || s == '3') return Colors.orange;
    return Colors.amber;
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text('إدارة الطلبات', style: GoogleFonts.cairo(fontWeight: FontWeight.w800)),
        centerTitle: true,
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh_rounded),
            onPressed: () => _loadAll(),
            tooltip: 'تحديث',
          ),
        ],
        bottom: TabBar(
          controller: _tabController,
          tabs: [
            Tab(
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.inbox_outlined, size: 18),
                  const SizedBox(width: 4),
                  Text('واردة', style: GoogleFonts.cairo()),
                  if (_pendingOrders.isNotEmpty) ...[
                    const SizedBox(width: 4),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
                      decoration: BoxDecoration(color: Colors.red, borderRadius: BorderRadius.circular(10)),
                      child: Text('${_pendingOrders.length}', style: const TextStyle(color: Colors.white, fontSize: 11)),
                    ),
                  ],
                ],
              ),
            ),
            Tab(
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.pending_actions_outlined, size: 18),
                  const SizedBox(width: 4),
                  Text('نشطة', style: GoogleFonts.cairo()),
                  if (_activeOrders.isNotEmpty) ...[
                    const SizedBox(width: 4),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
                      decoration: BoxDecoration(color: cs.primary, borderRadius: BorderRadius.circular(10)),
                      child: Text('${_activeOrders.length}', style: const TextStyle(color: Colors.white, fontSize: 11)),
                    ),
                  ],
                ],
              ),
            ),
            Tab(
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.history_rounded, size: 18),
                  const SizedBox(width: 4),
                  Text('السجل', style: GoogleFonts.cairo()),
                ],
              ),
            ),
          ],
        ),
      ),
      body: _loading
          ? Center(child: CircularProgressIndicator(color: cs.primary))
          : TabBarView(
              controller: _tabController,
              children: [
                _buildPendingTab(),
                _buildActiveTab(),
                _buildHistoryTab(),
              ],
            ),
    );
  }

  // ─── تبويب الطلبات الواردة ─────────────────────────────────────────────────
  Widget _buildPendingTab() {
    final cs = Theme.of(context).colorScheme;
    if (_pendingOrders.isEmpty) {
      return Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.inbox_outlined, size: 64, color: cs.onSurface.withOpacity(0.25)),
            const SizedBox(height: 16),
            Text('لا توجد طلبات واردة', style: GoogleFonts.cairo(color: cs.onSurface.withOpacity(0.5), fontSize: 16)),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _loadAll,
      child: ListView.builder(
        padding: const EdgeInsets.all(16),
        itemCount: _pendingOrders.length,
        itemBuilder: (ctx, i) {
          final order = _pendingOrders[i];
          final seconds = (order['secondsRemaining'] as int? ?? 0);
          final isUrgent = seconds < 300;
          final orderId = order['orderId'] as int;

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
              onTap: () => Navigator.push(
                ctx,
                MaterialPageRoute(
                  builder: (_) => AgentOrderDetailScreen(
                    api: widget.api,
                    state: widget.state,
                    orderId: orderId,
                  ),
                ),
              ).then((_) => _loadAll(silent: true)),
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Text('طلب #$orderId',
                            style: GoogleFonts.cairo(fontWeight: FontWeight.bold, fontSize: 16)),
                        const Spacer(),
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                          decoration: BoxDecoration(
                            color: isUrgent ? Colors.red : Colors.amber,
                            borderRadius: BorderRadius.circular(20),
                          ),
                          child: Row(children: [
                            const Icon(Icons.timer, size: 14, color: Colors.white),
                            const SizedBox(width: 4),
                            Text(_formatCountdown(seconds),
                                style: GoogleFonts.cairo(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 13)),
                          ]),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),
                    ...((order['items'] as List? ?? []).map((item) => Padding(
                      padding: const EdgeInsets.symmetric(vertical: 3),
                      child: Row(children: [
                        Icon(Icons.fiber_manual_record, size: 8, color: Theme.of(context).colorScheme.primary),
                        const SizedBox(width: 8),
                        Expanded(child: Text(item['productNameSnapshot']?.toString() ?? '', style: GoogleFonts.cairo())),
                        Text('× ${item['quantity']}',
                            style: GoogleFonts.cairo(color: Theme.of(context).colorScheme.primary, fontWeight: FontWeight.bold)),
                      ]),
                    ))),
                    const Divider(height: 20),
                    Row(children: [
                      Text('إجمالي حصتك:', style: GoogleFonts.cairo(fontWeight: FontWeight.w500)),
                      const Spacer(),
                      Text('${order['agentSubtotal']} ل.س',
                          style: GoogleFonts.cairo(fontWeight: FontWeight.bold,
                              color: Theme.of(context).colorScheme.primary, fontSize: 16)),
                    ]),
                    const SizedBox(height: 16),
                    Row(children: [
                      Expanded(
                        child: OutlinedButton.icon(
                          onPressed: () => _rejectOrder(orderId),
                          icon: const Icon(Icons.close, color: Colors.red),
                          label: Text('رفض', style: GoogleFonts.cairo(color: Colors.red)),
                          style: OutlinedButton.styleFrom(
                            side: const BorderSide(color: Colors.red),
                            padding: const EdgeInsets.symmetric(vertical: 12),
                          ),
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: ElevatedButton.icon(
                          onPressed: () => _acceptOrder(orderId),
                          icon: const Icon(Icons.check, color: Colors.white),
                          label: Text('قبول', style: GoogleFonts.cairo(color: Colors.white)),
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
            ),
          );
        },
      ),
    );
  }

  // ─── تبويب الطلبات النشطة ──────────────────────────────────────────────────
  Widget _buildActiveTab() {
    final cs = Theme.of(context).colorScheme;
    if (_activeOrders.isEmpty) {
      return Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.pending_actions_outlined, size: 64, color: cs.onSurface.withOpacity(0.25)),
            const SizedBox(height: 16),
            Text('لا توجد طلبات نشطة', style: GoogleFonts.cairo(color: cs.onSurface.withOpacity(0.5), fontSize: 16)),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _loadAll,
      child: ListView.builder(
        padding: const EdgeInsets.all(16),
        itemCount: _activeOrders.length,
        itemBuilder: (ctx, i) {
          final order = _activeOrders[i];
          final orderId = order['orderId'] as int;
          final status = order['orderStatus'];
          final subtotal = order['agentSubtotal'];
          final createdAt = order['createdAt']?.toString() ?? '';

          return Card(
            margin: const EdgeInsets.only(bottom: 12),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
            child: InkWell(
              borderRadius: BorderRadius.circular(16),
              onTap: () => Navigator.push(
                ctx,
                MaterialPageRoute(
                  builder: (_) => AgentOrderDetailScreen(
                    api: widget.api,
                    state: widget.state,
                    orderId: orderId,
                  ),
                ),
              ).then((_) => _loadAll(silent: true)),
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Text('طلب #$orderId',
                            style: GoogleFonts.cairo(fontWeight: FontWeight.bold, fontSize: 16)),
                        const Spacer(),
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                          decoration: BoxDecoration(
                            color: _statusColor(status).withOpacity(0.15),
                            borderRadius: BorderRadius.circular(12),
                          ),
                          child: Text(
                            _statusLabel(status),
                            style: GoogleFonts.cairo(
                              color: _statusColor(status),
                              fontWeight: FontWeight.w700,
                              fontSize: 12,
                            ),
                          ),
                        ),
                      ],
                    ),
                    if (subtotal != null) ...[
                      const SizedBox(height: 6),
                      Text('المبلغ: $subtotal ل.س',
                          style: GoogleFonts.cairo(color: cs.primary, fontWeight: FontWeight.w600)),
                    ],
                    const SizedBox(height: 12),
                    // Action buttons
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: [
                        _actionButton(
                          icon: Icons.update_rounded,
                          label: 'تغيير الحالة',
                          color: cs.primary,
                          onPressed: () => _showStatusDialog(orderId),
                        ),
                        _actionButton(
                          icon: Icons.delivery_dining_rounded,
                          label: 'تعيين سائق',
                          color: Colors.blue,
                          onPressed: () => _showAssignDriverDialog(orderId),
                        ),
                        _actionButton(
                          icon: Icons.timer_outlined,
                          label: 'وقت التوصيل',
                          color: Colors.orange,
                          onPressed: () => _showEtaDialog(orderId),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          );
        },
      ),
    );
  }

  // ─── تبويب السجل ───────────────────────────────────────────────────────────
  Widget _buildHistoryTab() {
    final cs = Theme.of(context).colorScheme;
    if (_historyOrders.isEmpty) {
      return Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(Icons.history_rounded, size: 64, color: cs.onSurface.withOpacity(0.25)),
            const SizedBox(height: 16),
            Text('لا يوجد سجل طلبات بعد', style: GoogleFonts.cairo(color: cs.onSurface.withOpacity(0.5), fontSize: 16)),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _loadAll,
      child: ListView.builder(
        padding: const EdgeInsets.all(16),
        itemCount: _historyOrders.length,
        itemBuilder: (ctx, i) {
          final order = _historyOrders[i];
          final orderId = order['orderId'] as int;
          final status = order['orderStatus'];
          final subtotal = order['agentSubtotal'];

          return Card(
            margin: const EdgeInsets.only(bottom: 10),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
            child: ListTile(
              onTap: () => Navigator.push(
                ctx,
                MaterialPageRoute(
                  builder: (_) => AgentOrderDetailScreen(
                    api: widget.api,
                    state: widget.state,
                    orderId: orderId,
                  ),
                ),
              ).then((_) => _loadAll(silent: true)),
              leading: CircleAvatar(
                backgroundColor: _statusColor(status).withOpacity(0.15),
                child: Icon(
                  status?.toString() == 'Delivered' ? Icons.check_circle_rounded : Icons.cancel_rounded,
                  color: _statusColor(status),
                ),
              ),
              title: Text('طلب #$orderId', style: GoogleFonts.cairo(fontWeight: FontWeight.bold)),
              subtitle: Text(_statusLabel(status), style: GoogleFonts.cairo(color: _statusColor(status))),
              trailing: subtotal != null
                  ? Text('$subtotal ل.س', style: GoogleFonts.cairo(fontWeight: FontWeight.w700, color: cs.primary))
                  : null,
            ),
          );
        },
      ),
    );
  }

  Widget _actionButton({
    required IconData icon,
    required String label,
    required Color color,
    required VoidCallback onPressed,
  }) {
    return OutlinedButton.icon(
      onPressed: onPressed,
      icon: Icon(icon, size: 16, color: color),
      label: Text(label, style: GoogleFonts.cairo(fontSize: 12, color: color)),
      style: OutlinedButton.styleFrom(
        side: BorderSide(color: color.withOpacity(0.5)),
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
        minimumSize: Size.zero,
        tapTargetSize: MaterialTapTargetSize.shrinkWrap,
      ),
    );
  }
}
