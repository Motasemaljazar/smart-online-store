import 'package:flutter/material.dart';
import 'dart:convert';
import '../services/api.dart';
import '../models/app_state.dart';
import '../widgets/brand_title.dart';

import '../widgets/support_card.dart';
import 'cart_screen.dart';
import 'complaints_screen.dart';

class OrderDetailsScreen extends StatefulWidget {
  const OrderDetailsScreen({super.key, required this.api, required this.state, required this.orderId});
  final ApiClient api;
  final AppState state;
  final int orderId;

  @override
  State<OrderDetailsScreen> createState() => _OrderDetailsScreenState();
}

class _OrderDetailsScreenState extends State<OrderDetailsScreen> {
  bool loading = true;
  String? error;
  Map<String, dynamic>? order;



  bool _cancelSending = false;

  // تقييم المتجر والسائق المحلي
  int _localStoreRating = 0;
  bool _storeSending = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() { loading = true; error = null; });
    try {
      final o = await widget.api.getOrder(widget.orderId);
      order = o;

      // نافذة التقييم التلقائية أُزيلت - التقييم يتم عبر أزرار المتجر في صفحة الطلب
      
      if (o['expectedDeliveryAtUtc'] != null) {
        widget.state.upsertOrderEta({
          'orderId': widget.orderId,
          'expectedDeliveryAtUtc': o['expectedDeliveryAtUtc'],
          'prepEtaMinutes': o['prepEtaMinutes'],
          'deliveryEtaMinutes': o['deliveryEtaMinutes'],
          'lastEtaUpdatedAtUtc': o['lastEtaUpdatedAtUtc'],
        });
      }
    } catch (e) {
      error = e.toString();
    } finally {
      if (mounted) setState(() { loading = false; });
    }
  }

  Future<void> _cancelOrder() async {
    final o = order ?? <String, dynamic>{};
    final cid = widget.state.customerId;
    if (cid == null) return;
    if (_cancelSending) return;

    try {
      if (o['createdAtUtc'] != null) {
        final created = DateTime.parse(o['createdAtUtc'].toString()).toUtc();
        if (DateTime.now().toUtc().difference(created) > const Duration(minutes: 2)) {
          if (!mounted) return;
          showDialog<void>(
            context: context,
            builder: (ctx) => AlertDialog(
              title: const Text('انتهت مدة الإلغاء'),
              content: const Text('لم يعد بإمكانك إلغاء الطلب.\nراجع الإدارة في قسم الدردشة أو اتصال.'),
              actions: [
                TextButton(
                  onPressed: () {
                    Navigator.of(ctx).pop();
                    Navigator.of(context).push(MaterialPageRoute(builder: (_) => ComplaintsScreen(api: widget.api, state: widget.state)));
                  },
                  child: const Text('الدردشة'),
                ),
                TextButton(onPressed: () => Navigator.of(ctx).pop(), child: const Text('إغلاق')),
              ],
            ),
          );
          return;
        }
      }
    } catch (_) {}

    String reason = '';
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx2, setSt) => AlertDialog(
          title: const Text('إلغاء الطلب'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Text('يرجى كتابة سبب الإلغاء (إجباري).'),
              const SizedBox(height: 10),
              TextField(
                decoration: const InputDecoration(
                  labelText: 'سبب الإلغاء',
                  border: OutlineInputBorder(),
                ),
                maxLength: 160,
                onChanged: (v) => setSt(() => reason = v),
              ),
            ],
          ),
          actions: [
            TextButton(onPressed: () => Navigator.of(ctx2).pop(false), child: const Text('رجوع')),
            ElevatedButton(
              style: ElevatedButton.styleFrom(backgroundColor: Colors.redAccent),
              onPressed: reason.trim().isEmpty ? null : () => Navigator.of(ctx2).pop(true),
              child: const Text('تأكيد الإلغاء'),
            ),
          ],
        ),
      ),
    );
    if (ok != true) return;

    setState(() => _cancelSending = true);
    try {
      await widget.api.cancelOrder(orderId: widget.orderId, customerId: cid, reason: reason.trim());
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('تم إلغاء الطلب ✅')));
      await _load();
    } catch (e) {
      if (!mounted) return;
      final msg = e.toString().contains('cancel_window_closed')
          ? 'لم يعد بإمكانك إلغاء الطلب. راجع الإدارة في قسم الدردشة أو اتصال.'
          : 'فشل إلغاء الطلب: $e';
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
    } finally {
      if (mounted) setState(() => _cancelSending = false);
    }
  }

  String statusName(int s) {
    const names = ['جديد','تم التأكيد','قيد التجهيز','جاهز للشحن','مع السائق','تم التسليم','ملغي'];
    if (s >= 0 && s < names.length) return names[s];
    if (s == 7) return 'تم القبول';
    return '$s';
  }

  Widget _buildOrderTracker(int status, ColorScheme cs, ThemeData theme) {
    if (status == 6) {
      return Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: Colors.red.shade50,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: Colors.red.shade200),
        ),
        child: Row(children: [
          Icon(Icons.cancel, color: Colors.red.shade600),
          const SizedBox(width: 10),
          Text('الطلب ملغي', style: TextStyle(color: Colors.red.shade700, fontWeight: FontWeight.bold)),
        ]),
      );
    }
    
    final steps = [
      {'icon': Icons.add_circle_outline, 'label': 'جديد', 'step': 0},
      {'icon': Icons.kitchen_outlined, 'label': 'قيد المعالجة', 'step': 2},
      {'icon': Icons.delivery_dining, 'label': 'مع السائق', 'step': 4},
      {'icon': Icons.check_circle_outline, 'label': 'تم التسليم', 'step': 5},
    ];

    int activeIndex = 0;
    if (status >= 5) activeIndex = 3;
    else if (status >= 4) activeIndex = 2;
    else if (status >= 1) activeIndex = 1;
    else activeIndex = 0;

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: cs.surfaceContainerLow.withOpacity(0.7),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: cs.outlineVariant.withOpacity(0.4)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('تتبع طلبك', style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.bold)),
          const SizedBox(height: 14),
          Row(
            children: List.generate(steps.length * 2 - 1, (i) {
              if (i.isOdd) {
                
                final passed = (i ~/ 2) < activeIndex;
                return Expanded(
                  child: Container(
                    height: 3,
                    color: passed ? cs.primary : cs.outlineVariant,
                  ),
                );
              }
              final idx = i ~/ 2;
              final step = steps[idx];
              final isDone = idx < activeIndex;
              final isActive = idx == activeIndex;
              return Column(
                children: [
                  Container(
                    width: 36,
                    height: 36,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      color: isDone || isActive ? cs.primary : cs.surfaceContainerHighest,
                      border: Border.all(
                        color: isDone || isActive ? cs.primary : cs.outlineVariant,
                        width: 2,
                      ),
                    ),
                    child: Icon(
                      step['icon'] as IconData,
                      size: 18,
                      color: isDone || isActive ? Colors.white : cs.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    step['label'] as String,
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: isDone || isActive ? cs.primary : cs.onSurfaceVariant,
                      fontWeight: isActive ? FontWeight.bold : FontWeight.normal,
                    ),
                    textAlign: TextAlign.center,
                  ),
                ],
              );
            }),
          ),
        ],
      ),
    );
  }

  String _humanOptions(String? snapshot) {
    final s = (snapshot ?? '').trim();
    if (s.isEmpty) return '';
    try {
      final m = jsonDecode(s);
      if (m is Map) {
        final parts = <String>[];
        final vn = m['variantName']?.toString();
        if (vn != null && vn.trim().isNotEmpty) parts.add(vn);
        final addons = m['addons'];
        if (addons is List && addons.isNotEmpty) {
          final names = addons.map((a) => (a is Map) ? a['name']?.toString() : null).where((x) => x != null && x.trim().isNotEmpty).cast<String>().toList();
          if (names.isNotEmpty) parts.add('إضافات: ${names.join('، ')}');
        }
        final note = m['note']?.toString();
        if (note != null && note.trim().isNotEmpty) parts.add('ملاحظة: $note');
        return parts.join(' • ');
      }
    } catch (_) {}
    return s;
  }

  Widget _etaCard(Map<String, dynamic> o) {
    final prep = (o['prepEtaMinutes'] is num) ? (o['prepEtaMinutes'] as num).toInt() : null;
    final del = (o['deliveryEtaMinutes'] is num) ? (o['deliveryEtaMinutes'] as num).toInt() : null;
    final expected = o['expectedDeliveryAtUtc']?.toString();

    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    if ((prep == null || prep == 0) && (del == null || del == 0) && (expected == null || expected.isEmpty)) {
      return Container(
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: cs.surfaceContainerHighest.withOpacity(0.5),
          borderRadius: BorderRadius.circular(16),
        ),
        child: Row(
          children: [
            Icon(Icons.schedule_rounded, color: cs.primary, size: 24),
            const SizedBox(width: 14),
            Expanded(
              child: Text(
                'لم يتم تحديد الوقت المتوقع بعد. سيظهر هنا عند تحديده من الإدارة.',
                style: theme.textTheme.bodyMedium?.copyWith(color: cs.onSurfaceVariant),
              ),
            ),
          ],
        ),
      );
    }

    DateTime? expectedDt;
    int? remaining;
    if (expected != null) {
      try {
        expectedDt = DateTime.parse(expected).toLocal();
        remaining = expectedDt.difference(DateTime.now()).inMinutes;
      } catch (_) {}
    }

    final total = (prep ?? 0) + (del ?? 0);

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: cs.surfaceContainerHighest.withOpacity(0.5),
        borderRadius: BorderRadius.circular(16),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.access_time_rounded, color: cs.primary, size: 22),
              const SizedBox(width: 10),
              Text(
                'الوقت المتوقع لوصول الطلب',
                style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700, color: cs.onSurface),
              ),
            ],
          ),
          const SizedBox(height: 10),
          if (total > 0)
            Text('التجهيز: ${prep ?? 0} د  •  التوصيل: ${del ?? 0} د  •  المجموع: $total د'),
          if (expectedDt != null) ...[
            const SizedBox(height: 6),
            Text('موعد الوصول التقريبي: ${expectedDt.hour.toString().padLeft(2,'0')}:${expectedDt.minute.toString().padLeft(2,'0')}'),
          ],
          if (remaining != null) ...[
            const SizedBox(height: 8),
            Row(
              children: [
                Expanded(
                  child: LinearProgressIndicator(
                    value: (total <= 0 || remaining <= 0) ? null : (1 - (remaining / total)).clamp(0.0, 1.0),
                    minHeight: 8,
                    borderRadius: BorderRadius.circular(20),
                  ),
                ),
                const SizedBox(width: 10),
                Text(remaining <= 0 ? 'قيد التحديث' : '${remaining}د'),
              ],
            ),
          ],
        ],
      ),
    );
  }

  @override
  void dispose() {
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: BrandTitle(state: widget.state, suffix: 'تفاصيل الطلب #${widget.orderId}'),
        actions: [
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ],
      ),
      body: loading
          ? const Center(child: CircularProgressIndicator())
          : (error != null)
              ? Center(child: Text(error!))
              : _body(),
    );
  }

  Widget _body() {
    final o = order ?? <String, dynamic>{};
    final items = (o['items'] is List) ? (o['items'] as List) : const [];
    final history = (o['history'] is List) ? (o['history'] as List) : const [];
    final agentItems = (o['agentItems'] is List) ? (o['agentItems'] as List) : const [];
    final rejectedAgentItems = agentItems.where((ai) => (ai['agentStatus'] as int? ?? 0) == 2).toList();

    final status = (o['currentStatus'] ?? 0) as int;
    final rating = o['orderRating'];
    final storeStars = (rating is Map) ? rating['storeRate'] : null;
    // 0 يعني لم يُقيَّم بعد (قيمة افتراضية عند تقييم الطرف الآخر أولاً)

    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final totalStr = (o['total'] as num?)?.toDouble().toStringAsFixed(2) ?? '0.00';
    final statusStr = statusName((o['currentStatus'] ?? 0) as int);
    final paymentMethod = (o['paymentMethod'] as int?) ?? 0;
    final paymentLabel = ['كاش عند الاستلام', 'بطاقة ائتمانية', 'تحويل بنكي'][paymentMethod.clamp(0, 2)];
    final paymentIcon = [Icons.payments_outlined, Icons.credit_card_outlined, Icons.account_balance_outlined][paymentMethod.clamp(0, 2)];

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Container(
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: cs.surfaceContainerLow.withOpacity(0.7),
            borderRadius: BorderRadius.circular(16),
            border: Border.all(color: cs.outlineVariant.withOpacity(0.5)),
          ),
          child: Row(
            children: [
              Container(
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  color: cs.primaryContainer.withOpacity(0.6),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Icon(Icons.receipt_long_rounded, color: cs.primary, size: 26),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'الحالة: $statusStr',
                      style: theme.textTheme.titleMedium?.copyWith(
                        fontWeight: FontWeight.w800,
                        color: cs.onSurface,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      'الإجمالي: $totalStr',
                      style: theme.textTheme.titleLarge?.copyWith(
                        fontWeight: FontWeight.w900,
                        color: cs.primary,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),

        const SizedBox(height: 12),

        _buildOrderTracker(status, cs, theme),

        const SizedBox(height: 12),

        Container(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          decoration: BoxDecoration(
            color: cs.surfaceContainerLow.withOpacity(0.7),
            borderRadius: BorderRadius.circular(14),
            border: Border.all(color: cs.outlineVariant.withOpacity(0.4)),
          ),
          child: Row(
            children: [
              Icon(paymentIcon, color: cs.primary, size: 22),
              const SizedBox(width: 12),
              Text('طريقة الدفع:', style: theme.textTheme.bodyMedium?.copyWith(color: cs.onSurfaceVariant)),
              const SizedBox(width: 8),
              Text(paymentLabel, style: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.w700)),
            ],
          ),
        ),

        const SizedBox(height: 12),

        if (rejectedAgentItems.isNotEmpty)
          Container(
            margin: const EdgeInsets.only(bottom: 12),
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(
              color: const Color(0xFFF8F4E8),
              borderRadius: BorderRadius.circular(14),
              border: Border.all(color: const Color(0xFFD4AF37)),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(children: [
                  Icon(Icons.warning_amber_rounded, color: const Color(0xFFB8860B), size: 20),
                  const SizedBox(width: 8),
                  Text('أصناف غير متوفرة', style: TextStyle(fontWeight: FontWeight.w700, color: const Color(0xFF8B6914))),
                ]),
                const SizedBox(height: 8),
                Text(
                  'تم رفض بعض الأصناف من قبل البائع. قد يكون المبلغ الإجمالي قد تعدّل.',
                  style: TextStyle(color: const Color(0xFFB8860B), fontSize: 13),
                ),
              ],
            ),
          ),

        SupportCard(state: widget.state),
        const SizedBox(height: 12),

        if ((o['canEdit'] == true && o['orderEditableUntilUtc'] != null) || (o['canCancel'] == true))
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: cs.surfaceContainerHighest.withOpacity(0.5),
              borderRadius: BorderRadius.circular(16),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Icon(Icons.edit_note_rounded, color: cs.primary, size: 24),
                    const SizedBox(width: 10),
                    Text(
                      'إدارة الطلب',
                      style: theme.textTheme.titleMedium?.copyWith(
                        fontWeight: FontWeight.w800,
                        color: cs.onSurface,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                Text(
                  'تعديل خلال 5 دقائق • إلغاء خلال دقيقة واحدة من الإرسال.',
                  style: theme.textTheme.bodyMedium?.copyWith(color: cs.onSurfaceVariant),
                ),
                const SizedBox(height: 14),
                Row(
                  mainAxisAlignment: MainAxisAlignment.end,
                  children: [
                    if (o['canEdit'] == true && o['orderEditableUntilUtc'] != null)
                      FilledButton.icon(
                        onPressed: () {
                  try {
                    final until = DateTime.parse(o['orderEditableUntilUtc'].toString());

                    if (DateTime.now().toUtc().isAfter(until.toUtc())) {
                      showDialog<void>(
                        context: context,
                        builder: (ctx) => AlertDialog(
                          title: const Text('انتهت مدة تعديل الطلب'),
                          content: const Text('لم يعد بإمكانك تعديل الطلب.\nراجع الإدارة في قسم الدردشة أو اتصال.'),
                          actions: [
                            TextButton(
                              onPressed: () {
                                Navigator.of(ctx).pop();
                                Navigator.of(context).push(
                                  MaterialPageRoute(builder: (_) => ComplaintsScreen(api: widget.api, state: widget.state)),
                                );
                              },
                              child: const Text('الدردشة'),
                            ),
                            TextButton(onPressed: () => Navigator.of(ctx).pop(), child: const Text('إغلاق')),
                          ],
                        ),
                      );
                      return;
                    }

                    final lat = (o['deliveryLat'] is num) ? (o['deliveryLat'] as num).toDouble() : null;
                    final lng = (o['deliveryLng'] is num) ? (o['deliveryLng'] as num).toDouble() : null;
                    final addr = (o['deliveryAddress'] ?? '').toString();
                    final cartItems = items.map((x) {
                      final m = Map<String, dynamic>.from(x as Map);
                      return CartItem(
                        key: '${m['productId']}-${m['optionsSnapshot'] ?? ''}-${DateTime.now().millisecondsSinceEpoch}',
                        productId: (m['productId'] ?? 0) as int,
                        name: (m['productNameSnapshot'] ?? '').toString(),
                        unitPrice: ((m['unitPriceSnapshot'] as num?)?.toDouble() ?? 0),
                        qty: (m['quantity'] ?? 1) as int,
                        optionsSnapshot: (m['optionsSnapshot'] ?? '').toString(),
                        optionsLabel: 'خيارات محفوظة',
                      );
                    }).toList();

                    widget.state.beginEditOrder(
                      orderId: widget.orderId,
                      untilUtc: until.toUtc(),
                      items: cartItems,
                      notes: (o['notes'] ?? '').toString(),
                    );

                    if (lat != null && lng != null) {
                      widget.state.setDeliveryLocation(
                        lat: lat,
                        lng: lng,
                        address: addr.trim().isEmpty ? widget.state.defaultAddress : addr,
                      );
                    }

                    Navigator.of(context).push(
                      MaterialPageRoute(builder: (_) => CartScreen(api: widget.api, state: widget.state)),
                    );
                  } catch (_) {}
                        },
                        icon: const Icon(Icons.edit_rounded, size: 20),
                        label: const Text('تعديل'),
                        style: FilledButton.styleFrom(
                          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        ),
                      ),
                    if (o['canEdit'] == true && o['canCancel'] == true) const SizedBox(width: 10),
                    if (o['canCancel'] == true)
                      OutlinedButton.icon(
                        onPressed: _cancelSending ? null : _cancelOrder,
                        icon: _cancelSending
                            ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
                            : const Icon(Icons.cancel_outlined, size: 20),
                        label: Text(_cancelSending ? 'جاري...' : 'إلغاء'),
                        style: OutlinedButton.styleFrom(
                          foregroundColor: Theme.of(context).colorScheme.error,
                          side: BorderSide(color: Theme.of(context).colorScheme.error),
                          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                        ),
                      ),
                  ],
                ),
              ],
            ),
          ),

        if (o['canEdit'] != true && o['orderEditableUntilUtc'] != null)
          Container(
            margin: const EdgeInsets.only(bottom: 12),
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: cs.surfaceContainerHighest.withOpacity(0.5),
              borderRadius: BorderRadius.circular(16),
            ),
            child: Row(
              children: [
                Icon(Icons.lock_clock_rounded, color: cs.onSurfaceVariant, size: 24),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'انتهت مدة تعديل الطلب',
                        style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        'يمكنك التواصل مع الإدارة عبر الدردشة أو الاتصال لتعديل الطلب.',
                        style: theme.textTheme.bodySmall?.copyWith(color: cs.onSurfaceVariant),
                      ),
                    ],
                  ),
                ),
                TextButton(
                  onPressed: () {
                    Navigator.of(context).push(
                      MaterialPageRoute(builder: (_) => ComplaintsScreen(api: widget.api, state: widget.state)),
                    );
                  },
                  child: const Text('الدردشة'),
                ),
              ],
            ),
          ),

        if (o['canCancel'] != true)
          Container(
            margin: const EdgeInsets.only(bottom: 12),
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: cs.surfaceContainerHighest.withOpacity(0.5),
              borderRadius: BorderRadius.circular(16),
            ),
            child: Row(
              children: [
                Icon(Icons.info_outline_rounded, color: cs.onSurfaceVariant, size: 24),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'الإلغاء متاح لمدة دقيقة واحدة فقط',
                        style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        'إذا احتجت إلغاء بعد انتهاء المهلة، تواصل مع الإدارة عبر الدردشة أو الاتصال.',
                        style: theme.textTheme.bodySmall?.copyWith(color: cs.onSurfaceVariant),
                      ),
                    ],
                  ),
                ),
                TextButton(
                  onPressed: () {
                    Navigator.of(context).push(MaterialPageRoute(builder: (_) => ComplaintsScreen(api: widget.api, state: widget.state)));
                  },
                  child: const Text('الدردشة'),
                ),
              ],
            ),
          ),

        _etaCard(o),
        const SizedBox(height: 12),
        if ((o['deliveryAddress'] ?? '').toString().trim().isNotEmpty)
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: cs.surfaceContainerHighest.withOpacity(0.5),
              borderRadius: BorderRadius.circular(16),
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.location_on_rounded, color: cs.primary, size: 22),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'موقع التوصيل',
                        style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700, color: cs.onSurface),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        o['deliveryAddress'].toString(),
                        style: theme.textTheme.bodyMedium?.copyWith(color: cs.onSurfaceVariant),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),

        const SizedBox(height: 20),
        Text(
          'الأصناف',
          style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800, color: cs.onSurface),
        ),
        const SizedBox(height: 10),
        ...items.map((it) {
          final m = Map<String, dynamic>.from(it as Map);
          final qty = (m['quantity'] as num?)?.toInt() ?? 1;
          final name = (m['productNameSnapshot'] ?? '').toString();
          final price = (m['unitPriceSnapshot'] as num?)?.toDouble() ?? 0;
          final opts = _humanOptions(m['optionsSnapshot']?.toString()).trim();
          final productId = (m['productId'] as num?)?.toInt() ?? 0;
          final canRateProduct = status >= 5 && productId > 0 && widget.state.customerId != null;
          return Container(
            margin: const EdgeInsets.only(bottom: 8),
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(
              color: cs.surfaceContainerLow.withOpacity(0.7),
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: cs.outlineVariant.withOpacity(0.4)),
            ),
            child: Column(
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            name,
                            style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700),
                          ),
                          if (opts.isNotEmpty) ...[
                            const SizedBox(height: 4),
                            Text(
                              opts,
                              style: theme.textTheme.bodySmall?.copyWith(color: cs.onSurfaceVariant),
                            ),
                          ],
                        ],
                      ),
                    ),
                    Text(
                      'x$qty  •  ${(price * qty).toStringAsFixed(0)} ل.س',
                      style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w800, color: cs.primary),
                    ),
                  ],
                ),
                
                if (canRateProduct) ...[
                  const SizedBox(height: 8),
                  _buildStoreRatingRow(orderId: widget.orderId, cs: cs, theme: theme, storeStars: storeStars),
                ],
              ],
            ),
          );
        }),

        const SizedBox(height: 20),
        Text(
          'سجل الحالة',
          style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800, color: cs.onSurface),
        ),
        const SizedBox(height: 10),
        if (history.isEmpty)
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: cs.surfaceContainerHighest.withOpacity(0.5),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Text(
              'لا يوجد سجل بعد',
              style: theme.textTheme.bodyMedium?.copyWith(color: cs.onSurfaceVariant),
            ),
          )
        else
          ...history.map((h) {
            final m = Map<String, dynamic>.from(h as Map);
            final st = (m['status'] ?? 0) as int;
            final when = m['changedAtUtc']?.toString();
            String timeText = '';
            if (when != null) {
              try {
                final dt = DateTime.parse(when).toLocal();
                timeText = '${dt.hour.toString().padLeft(2,'0')}:${dt.minute.toString().padLeft(2,'0')}';
              } catch (_) {}
            }
            final comment = (m['comment'] ?? '').toString();
            return Container(
              margin: const EdgeInsets.only(bottom: 8),
              padding: const EdgeInsets.all(14),
              decoration: BoxDecoration(
                color: cs.surfaceContainerHighest.withOpacity(0.5),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Row(
                children: [
                  Icon(Icons.history_rounded, color: cs.primary, size: 20),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          statusName(st),
                          style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700),
                        ),
                        if (comment.trim().isNotEmpty) ...[
                          const SizedBox(height: 2),
                          Text(
                            comment,
                            style: theme.textTheme.bodySmall?.copyWith(color: cs.onSurfaceVariant),
                          ),
                        ],
                      ],
                    ),
                  ),
                  Text(
                    timeText,
                    style: theme.textTheme.bodySmall?.copyWith(color: cs.onSurfaceVariant, fontWeight: FontWeight.w600),
                  ),
                ],
              ),
            );
          }),

        const SizedBox(height: 20),
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // قسم التقييم (يظهر فقط بعد التسليم)
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        if (status >= 5) ...[
          Text(
            'تقييمات الطلب',
            style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800, color: cs.onSurface),
          ),
          const SizedBox(height: 10),

          // ── تقييم المتجر ──
          _buildOrderRatingCard(
            icon: Icons.storefront_rounded,
            title: 'تقييم المتجر',
            subtitle: 'كيف كانت تجربتك مع خدمة المتجر؟',
            currentStars: (storeStars is int && (storeStars as int) > 0)
                ? (storeStars as int)
                : _localStoreRating,
            isSaved: (storeStars is int && (storeStars as int) > 0),
            isSending: _storeSending,
            savedComment: rating != null ? (rating['storeComment'] ?? '').toString() : '',
            cs: cs,
            theme: theme,
            onRate: (widget.state.customerId == null || (storeStars is int && (storeStars as int) > 0))
                ? null
                : (star) async {
                    setState(() { _localStoreRating = star; _storeSending = true; });
                    try {
                      await widget.api.rateStore(
                        orderId: widget.orderId,
                        customerId: widget.state.customerId!,
                        stars: star,
                      );
                      await _load();
                      if (mounted) {
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(content: Text('✅ تم حفظ تقييم المتجر'), duration: Duration(seconds: 2)),
                        );
                      }
                    } catch (_) {
                      if (mounted) {
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(content: Text('❌ فشل حفظ التقييم، حاول مجدداً'), duration: Duration(seconds: 2)),
                        );
                      }
                    } finally {
                      if (mounted) setState(() { _storeSending = false; });
                    }
                  },
          ),


        ],
      ],
    );
  }

  Widget _buildOrderRatingCard({
    required IconData icon,
    required String title,
    required String subtitle,
    required int currentStars,
    required bool isSaved,
    required bool isSending,
    required String savedComment,
    required ColorScheme cs,
    required ThemeData theme,
    required void Function(int star)? onRate,
  }) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: isSaved
            ? Colors.green.shade50.withOpacity(0.7)
            : cs.surfaceContainerHighest.withOpacity(0.5),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: isSaved ? Colors.green.shade200 : cs.outlineVariant.withOpacity(0.4),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(icon, color: isSaved ? Colors.green : cs.primary, size: 20),
              const SizedBox(width: 8),
              Text(
                title,
                style: theme.textTheme.titleSmall?.copyWith(
                  fontWeight: FontWeight.w700,
                  color: isSaved ? Colors.green.shade700 : cs.onSurface,
                ),
              ),
              if (isSaved) ...[
                const SizedBox(width: 6),
                Icon(Icons.check_circle, size: 16, color: Colors.green.shade600),
              ],
            ],
          ),
          if (!isSaved) ...[
            const SizedBox(height: 4),
            Text(
              subtitle,
              style: theme.textTheme.bodySmall?.copyWith(color: cs.onSurfaceVariant),
            ),
          ],
          const SizedBox(height: 10),
          if (isSending)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 4),
              child: SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2)),
            )
          else
            Row(
              children: List.generate(5, (i) {
                final star = i + 1;
                return GestureDetector(
                  onTap: onRate != null ? () => onRate(star) : null,
                  child: Icon(
                    star <= currentStars ? Icons.star_rounded : Icons.star_border_rounded,
                    color: star <= currentStars
                        ? Colors.amber
                        : (onRate == null ? cs.outlineVariant.withOpacity(0.4) : cs.outlineVariant),
                    size: 30,
                  ),
                );
              }),
            ),
          if (isSaved && savedComment.trim().isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(
              savedComment.trim(),
              style: theme.textTheme.bodySmall?.copyWith(color: cs.onSurfaceVariant),
            ),
          ],
          if (isSaved)
            Padding(
              padding: const EdgeInsets.only(top: 6),
              child: Text(
                'تم التقييم بـ $currentStars / 5 ⭐',
                style: theme.textTheme.labelSmall?.copyWith(color: Colors.green.shade600, fontWeight: FontWeight.w600),
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildStoreRatingRow({
    required int orderId,
    required ColorScheme cs,
    required ThemeData theme,
    required dynamic storeStars,
  }) {
    final isSaved = storeStars is int && (storeStars as int) > 0;
    final current = isSaved ? (storeStars as int) : _localStoreRating;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Divider(color: cs.outlineVariant.withOpacity(0.5), height: 8),
        Row(
          children: [
            Icon(Icons.storefront_rounded, size: 14, color: isSaved ? Colors.green.shade600 : cs.secondary),
            const SizedBox(width: 4),
            Text(
              isSaved ? 'تقييم المتجر (تم):' : 'قيّم المتجر:',
              style: theme.textTheme.labelSmall?.copyWith(
                color: isSaved ? Colors.green.shade600 : cs.onSurfaceVariant,
                fontWeight: FontWeight.w600,
              ),
            ),
            const SizedBox(width: 8),
            if (_storeSending)
              const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
            else
              ...List.generate(5, (i) {
                final star = i + 1;
                return GestureDetector(
                  onTap: (isSaved || widget.state.customerId == null) ? null : () async {
                    setState(() { _localStoreRating = star; _storeSending = true; });
                    try {
                      await widget.api.rateStore(
                        orderId: orderId,
                        customerId: widget.state.customerId!,
                        stars: star,
                      );
                      await _load();
                      if (mounted) {
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(content: Text('✅ تم حفظ تقييم المتجر'), duration: Duration(seconds: 2)),
                        );
                      }
                    } catch (_) {
                      if (mounted) {
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(content: Text('❌ فشل حفظ التقييم، حاول مجدداً'), duration: Duration(seconds: 2)),
                        );
                      }
                    } finally {
                      if (mounted) setState(() { _storeSending = false; });
                    }
                  },
                  child: Icon(
                    star <= current ? Icons.star_rounded : Icons.star_border_rounded,
                    color: star <= current
                        ? (isSaved ? Colors.green.shade400 : Colors.amber)
                        : (isSaved ? cs.outlineVariant.withOpacity(0.3) : cs.outlineVariant),
                    size: 22,
                  ),
                );
              }),
            if (isSaved) ...[
              const SizedBox(width: 6),
              Icon(Icons.check_circle, size: 14, color: Colors.green.shade600),
            ],
          ],
        ),
      ],
    );
  }
}
