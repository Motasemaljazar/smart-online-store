import 'dart:async';
import 'package:flutter/material.dart';
import '../services/agent_api.dart';
import '../models/agent_state.dart';

class AgentOrderDetailScreen extends StatefulWidget {
  final AgentApi api;
  final AgentState state;
  final int orderId;

  const AgentOrderDetailScreen({
    super.key,
    required this.api,
    required this.state,
    required this.orderId,
  });

  @override
  State<AgentOrderDetailScreen> createState() => _AgentOrderDetailScreenState();
}

class _AgentOrderDetailScreenState extends State<AgentOrderDetailScreen> {
  Map<String, dynamic>? _order;
  List<dynamic> _drivers = [];
  bool _loading = true;
  String? _error;
  Timer? _refreshTimer;

  static const Map<int, String> _statusLabels = {
    0: 'جديد',
    1: 'مؤكد',
    2: 'قيد المعالجة',
    3: 'جاهز للشحن',
    4: 'مع السائق',
    5: 'تم التسليم',
    6: 'ملغي',
    7: 'مقبول',
  };

  static const Map<int, Color> _statusColors = {
    0: Color(0xFF2196F3),
    1: Color(0xFF9C27B0),
    2: Color(0xFFFF9800),
    3: Color(0xFF00BCD4),
    4: Color(0xFF3F51B5),
    5: Color(0xFF4CAF50),
    6: Color(0xFFF44336),
    7: Color(0xFF8BC34A),
  };

  @override
  void initState() {
    super.initState();
    _load();
    _refreshTimer = Timer.periodic(const Duration(seconds: 15), (_) => _load(silent: true));
  }

  @override
  void dispose() {
    _refreshTimer?.cancel();
    super.dispose();
  }

  Future<void> _load({bool silent = false}) async {
    if (!silent) setState(() { _loading = true; _error = null; });
    try {
      final results = await Future.wait([
        widget.api.getOrderDetail(widget.state.token ?? '', widget.orderId),
        widget.api.getAvailableDrivers(widget.state.token ?? ''),
      ]);
      if (mounted) {
        setState(() {
          _order = results[0] as Map<String, dynamic>;
          _drivers = results[1] as List<dynamic>;
          _loading = false;
        });
      }
    } catch (e) {
      if (mounted) setState(() { _error = e.toString(); _loading = false; });
    }
  }

  Future<void> _changeStatus() async {
    final order = _order;
    if (order == null) return;
    final currentStatus = (order['currentStatus'] as num?)?.toInt() ?? 0;

    // الأوضاع المتاحة للمندوب بحسب الوضع الحالي
    List<int> available;
    if (currentStatus == 0 || currentStatus == 7) {
      // جديد أو مقبول -> يمكن نقله للتحضير
      available = [2, 3, 4, 5, 6];
    } else if (currentStatus == 1) {
      // مؤكد
      available = [2, 3, 4, 5, 6];
    } else if (currentStatus == 2) {
      // قيد المعالجة
      available = [3, 4, 5, 6];
    } else if (currentStatus == 3) {
      // جاهز للشحن
      available = [4, 5, 6];
    } else if (currentStatus == 4) {
      // مع السائق
      available = [5, 6];
    } else {
      available = [];
    }

    final chosen = await showDialog<int>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('تغيير حالة الطلب'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: available.map((s) => ListTile(
            leading: CircleAvatar(
              radius: 12,
              backgroundColor: _statusColors[s] ?? Colors.grey,
            ),
            title: Text(_statusLabels[s] ?? '$s'),
            onTap: () => Navigator.pop(ctx, s),
          )).toList(),
        ),
        actions: [TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('إلغاء'))],
      ),
    );

    if (chosen == null) return;

    // Comment dialog for some statuses
    String? comment;
    if (chosen == 6) { // Cancelled
      final ctrl = TextEditingController();
      final confirmed = await showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: const Text('سبب الإلغاء'),
          content: TextField(
            controller: ctrl,
            decoration: const InputDecoration(hintText: 'أدخل سبب الإلغاء...', border: OutlineInputBorder()),
            maxLines: 2,
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('إلغاء')),
            FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('تأكيد')),
          ],
        ),
      );
      if (confirmed != true) return;
      comment = ctrl.text.trim();
    }

    try {
      await widget.api.updateOrderStatus(widget.state.token ?? '', widget.orderId, chosen, comment: comment);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('✅ تم تغيير الحالة إلى: ${_statusLabels[chosen]}'),
            backgroundColor: Colors.green,
          ),
        );
        _load();
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('خطأ: $e'), backgroundColor: Colors.red),
        );
      }
    }
  }

  Future<void> _assignDriver() async {
    if (_drivers.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('لا يوجد سائقون متاحون'), backgroundColor: Colors.orange),
      );
      return;
    }

    final chosen = await showDialog<int>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('اختر السائق'),
        content: SizedBox(
          width: double.maxFinite,
          child: ListView.builder(
            shrinkWrap: true,
            itemCount: _drivers.length,
            itemBuilder: (_, i) {
              final d = _drivers[i] as Map<String, dynamic>;
              return ListTile(
                leading: CircleAvatar(
                  child: Icon(
                    d['vehicleType'] == 1 ? Icons.motorcycle_rounded : Icons.directions_car_rounded,
                  ),
                ),
                title: Text(d['name']?.toString() ?? ''),
                subtitle: Text(d['phone']?.toString() ?? ''),
                onTap: () => Navigator.pop(ctx, (d['id'] as num).toInt()),
              );
            },
          ),
        ),
        actions: [TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('إلغاء'))],
      ),
    );

    if (chosen == null) return;

    try {
      await widget.api.assignDriver(widget.state.token ?? '', widget.orderId, chosen);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('✅ تم تعيين السائق وإشعار الزبون'), backgroundColor: Colors.green),
        );
        _load();
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('خطأ: $e'), backgroundColor: Colors.red),
        );
      }
    }
  }

  Future<void> _setEta() async {
    final order = _order;
    if (order == null) return;

    final delCtrl = TextEditingController(
      text: order['deliveryEtaMinutes']?.toString() ?? '',
    );

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('تحديد وقت التوصيل'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: delCtrl,
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
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('إلغاء')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('حفظ وإشعار الزبون')),
        ],
      ),
    );

    if (confirmed != true) return;

    final del = int.tryParse(delCtrl.text.trim());

    try {
      await widget.api.setEta(
        widget.state.token ?? '',
        widget.orderId,
        deliveryEta: del,
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('✅ تم تحديث الوقت وإشعار الزبون'), backgroundColor: Colors.green),
        );
        _load();
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('خطأ: $e'), backgroundColor: Colors.red),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text('تفاصيل الطلب #${widget.orderId}'),
        actions: [
          IconButton(icon: const Icon(Icons.refresh_rounded), onPressed: _load),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? Center(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(Icons.error_outline_rounded, size: 48, color: cs.error),
                      const SizedBox(height: 12),
                      Text(_error!, textAlign: TextAlign.center),
                      const SizedBox(height: 12),
                      FilledButton(onPressed: _load, child: const Text('إعادة المحاولة')),
                    ],
                  ),
                )
              : _buildBody(theme, cs),
    );
  }

  Widget _buildBody(ThemeData theme, ColorScheme cs) {
    final order = _order!;
    final status = (order['currentStatus'] as num?)?.toInt() ?? 0;
    final statusLabel = _statusLabels[status] ?? '$status';
    final statusColor = _statusColors[status] ?? Colors.grey;
    final items = (order['items'] as List<dynamic>? ?? []).cast<Map<String, dynamic>>();
    final driver = order['driver'] as Map<String, dynamic>?;
    final isFinished = status == 5 || status == 6; // Delivered or Cancelled

    return SingleChildScrollView(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Status Card
          Card(
            elevation: 0,
            color: statusColor.withOpacity(0.12),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(16),
              side: BorderSide(color: statusColor.withOpacity(0.4)),
            ),
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Row(
                children: [
                  CircleAvatar(backgroundColor: statusColor, radius: 20,
                    child: const Icon(Icons.receipt_long_rounded, color: Colors.white, size: 20)),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('طلب #${widget.orderId}',
                            style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800)),
                        Text('الحالة: $statusLabel',
                            style: TextStyle(color: statusColor, fontWeight: FontWeight.w700)),
                      ],
                    ),
                  ),
                  if (!isFinished)
                    FilledButton.tonal(
                      onPressed: _changeStatus,
                      child: const Text('تغيير\nالحالة', textAlign: TextAlign.center, style: TextStyle(fontSize: 12)),
                    ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 12),

          // ETA + Driver Assignment
          if (!isFinished) ...[
            Row(children: [
              Expanded(
                child: _ActionCard(
                  icon: Icons.access_time_rounded,
                  label: 'تحديد الوقت',
                  sublabel: order['deliveryEtaMinutes'] != null
                      ? 'توصيل: ${order['deliveryEtaMinutes']} د'
                      : 'لم يُحدد بعد',
                  color: cs.primary,
                  onTap: _setEta,
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: _ActionCard(
                  icon: Icons.person_pin_circle_rounded,
                  label: 'تعيين سائق',
                  sublabel: driver != null
                      ? driver['name']?.toString() ?? 'سائق معين'
                      : 'لم يُعيَّن بعد',
                  color: Colors.green,
                  onTap: _assignDriver,
                ),
              ),
            ]),
            const SizedBox(height: 12),
          ],

          // Driver info
          if (driver != null) ...[
            Card(
              elevation: 0,
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Row(
                  children: [
                    CircleAvatar(
                      backgroundColor: cs.primaryContainer,
                      child: Icon(Icons.directions_car_rounded, color: cs.primary),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('السائق: ${driver['name']}',
                              style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700)),
                          Text('📞 ${driver['phone']}',
                              style: theme.textTheme.bodySmall?.copyWith(color: cs.onSurfaceVariant)),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 12),
          ],

          // Order Items
          Card(
            elevation: 0,
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('المنتجات', style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w800)),
                  const SizedBox(height: 8),
                  ...items.map((item) => Padding(
                    padding: const EdgeInsets.symmetric(vertical: 4),
                    child: Row(
                      children: [
                        Icon(Icons.fiber_manual_record, size: 8, color: cs.primary),
                        const SizedBox(width: 8),
                        Expanded(child: Text(item['productNameSnapshot']?.toString() ?? '')),
                        Text('× ${item['quantity']}',
                            style: TextStyle(color: cs.primary, fontWeight: FontWeight.bold)),
                        const SizedBox(width: 8),
                        Text('${item['unitPriceSnapshot']} ل.س',
                            style: TextStyle(color: cs.onSurfaceVariant, fontSize: 12)),
                      ],
                    ),
                  )),
                  const Divider(),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      const Text('إجمالي حصة المندوب:', style: TextStyle(fontWeight: FontWeight.w600)),
                      Text(
                        '${order['agentSubtotal'] ?? order['total'] ?? 0} ل.س',
                        style: TextStyle(fontWeight: FontWeight.w800, color: cs.primary, fontSize: 16),
                      ),
                    ],
                  ),
                  if ((order['commissionPercent'] as num?)?.toDouble() != null && (order['commissionPercent'] as num).toDouble() > 0) ...[
                    const SizedBox(height: 6),
                    Builder(builder: (ctx) {
                      final subtotal = (order['agentSubtotal'] as num?)?.toDouble() ?? 0.0;
                      final commPct = (order['commissionPercent'] as num?)?.toDouble() ?? 0.0;
                      final commAmt = (subtotal * commPct / 100).roundToDouble();
                      final netAmt = subtotal - commAmt;
                      return Column(
                        children: [
                          Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                            Text('عمولة المتجر ($commPct%):', style: TextStyle(color: cs.onSurfaceVariant, fontSize: 13)),
                            Text('- ${commAmt.toStringAsFixed(0)} ل.س', style: const TextStyle(color: Colors.orange, fontSize: 13, fontWeight: FontWeight.w600)),
                          ]),
                          const SizedBox(height: 4),
                          Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                            const Text('صافي دخلك:', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 14)),
                            Text('${netAmt.toStringAsFixed(0)} ل.س', style: TextStyle(fontWeight: FontWeight.w900, color: Colors.green.shade700, fontSize: 15)),
                          ]),
                        ],
                      );
                    }),
                  ],
                ],
              ),
            ),
          ),
          const SizedBox(height: 12),

          // Address
          if ((order['deliveryAddress'] ?? order['address']) != null)
            Card(
              elevation: 0,
              child: ListTile(
                leading: const Icon(Icons.location_on_rounded, color: Colors.red),
                title: const Text('عنوان التوصيل'),
                subtitle: Text(
                  (order['deliveryAddress'] ?? order['address'] ?? '').toString(),
                  textDirection: TextDirection.rtl,
                ),
              ),
            ),
        ],
      ),
    );
  }
}

class _ActionCard extends StatelessWidget {
  final IconData icon;
  final String label;
  final String sublabel;
  final Color color;
  final VoidCallback onTap;

  const _ActionCard({
    required this.icon,
    required this.label,
    required this.sublabel,
    required this.color,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return Material(
      color: color.withOpacity(0.08),
      borderRadius: BorderRadius.circular(14),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(14),
        child: Container(
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(14),
            border: Border.all(color: color.withOpacity(0.3)),
          ),
          child: Column(
            children: [
              Icon(icon, color: color, size: 28),
              const SizedBox(height: 6),
              Text(label, style: TextStyle(fontWeight: FontWeight.w700, color: color, fontSize: 13)),
              const SizedBox(height: 4),
              Text(sublabel,
                  textAlign: TextAlign.center,
                  style: TextStyle(fontSize: 11, color: color.withOpacity(0.7))),
            ],
          ),
        ),
      ),
    );
  }
}
