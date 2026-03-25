import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../app_config.dart';
import '../services/api.dart';
import '../services/realtime.dart';
import '../models/brand_state.dart';
import '../theme/driver_theme.dart';
import 'driver_order_detail_screen.dart';

class DriverHome extends StatelessWidget {
  const DriverHome({super.key, required this.prefs, required this.brand});
  final SharedPreferences prefs;
  final BrandState brand;

  @override
  Widget build(BuildContext context) {
    final baseUrl = kBackendBaseUrl;
    final token = prefs.getString('driverToken') ?? '';
    final rn = (prefs.getString('storeName') ?? '').trim();
    return HomeScreen(api: DriverApi(baseUrl: baseUrl), token: token, storeName: rn, brand: brand);
  }
}

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key, required this.api, required this.token, this.storeName = '', required this.brand});
  final DriverApi api;
  final String token;
  final String storeName;
  final BrandState brand;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> with SingleTickerProviderStateMixin {
  bool _loading = true;
  String? _error;

  String _storeName = '';

  RealtimeClient? _rt;

  double? _storeLat;
  double? _storeLng;
  List<Map<String, dynamic>> _orders = [];

  int _todayDelivered = 0;
  int _todayInProgress = 0;
  double _todayCash = 0;
  double _todayEarnings = 0;
  bool _statsLoading = false;
  
  int _newOrdersCount = 0;

  late TabController _tabController;
  Timer? _timer;

  static const int _stNew = 0;
  static const int _stConfirmed = 1;
  static const int _stPreparing = 2;
  static const int _stReadyForPickup = 3;
  static const int _stWithDriver = 4;
  static const int _stDelivered = 5;
  static const int _stCancelled = 6;
  static const int _stAccepted = 7;

  String _statusLabel(int s) {
    switch (s) {
      case _stNew: return 'جديد';
      case _stConfirmed: return 'تم التأكيد';
      case _stPreparing: return 'قيد المعالجة';
      case _stReadyForPickup: return 'جاهز للاستلام';
      case _stAccepted: return 'تم القبول';
      case _stWithDriver: return 'مع السائق';
      case _stDelivered: return 'تم التسليم';
      case _stCancelled: return 'ملغى';
      default: return '$s';
    }
  }

  Color _statusColor(int s, ColorScheme cs) {
    switch (s) {
      case _stNew: return Colors.orange;
      case _stConfirmed: return Colors.blue;
      case _stPreparing: return Colors.purple;
      case _stReadyForPickup: return Colors.teal;
      case _stAccepted: return Colors.indigo;
      case _stWithDriver: return DriverTheme.primaryRed;
      case _stDelivered: return Colors.green;
      case _stCancelled: return cs.error;
      default: return cs.outline;
    }
  }

  IconData _statusIcon(int s) {
    switch (s) {
      case _stNew: return Icons.fiber_new_rounded;
      case _stConfirmed: return Icons.check_circle_outline_rounded;
      case _stPreparing: return Icons.restaurant_rounded;
      case _stReadyForPickup: return Icons.inventory_2_rounded;
      case _stAccepted: return Icons.thumb_up_alt_rounded;
      case _stWithDriver: return Icons.delivery_dining_rounded;
      case _stDelivered: return Icons.done_all_rounded;
      case _stCancelled: return Icons.cancel_rounded;
      default: return Icons.help_outline_rounded;
    }
  }

  @override
  void initState() {
    super.initState();
    _storeName = widget.storeName;

    () async {
      try {
        final prefs = await SharedPreferences.getInstance();
        final rn = (prefs.getString('storeName') ?? '').trim();
        if (rn.isNotEmpty && mounted) setState(() => _storeName = rn);
      } catch (_) {}
    }();

    try {
      _rt = RealtimeClient(baseUrl: widget.api.baseUrl);
      _rt!.connectDriver(
        token: widget.token,
        onSettingsUpdated: (s) async {
          final rn = (s['storeName'] ?? '').toString();
          if (rn.trim().isNotEmpty) {
            setState(() => _storeName = rn);
            try {
              final prefs = await SharedPreferences.getInstance();
              await prefs.setString('storeName', rn);
            } catch (_) {}
          }
        },
        onOrderAssigned: (p) async {
          try { SystemSound.play(SystemSoundType.alert); } catch (_) {}
          if (!mounted) return;
          setState(() => _newOrdersCount = _newOrdersCount + 1);
          ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('وصلتك مهمة جديدة')));
          await _loadActiveOrders(silent: true);
          await _loadTodayStats(silent: true);
        },
        onOrderUpdated: (_) async {
          await _loadActiveOrders(silent: true);
          await _loadTodayStats(silent: true);
        },
      );
    } catch (_) {}

    _tabController = TabController(length: 2, vsync: this);
    _tabController.addListener(() {
      if (_tabController.index == 1) setState(() => _newOrdersCount = 0);
      if (_tabController.index == 0) _loadTodayStats();
    });
    _loadActiveOrders();
    _loadTodayStats();
    _timer = Timer.periodic(const Duration(seconds: 10), (_) => _loadActiveOrders(silent: true));
    Timer.periodic(const Duration(seconds: 25), (_) => _loadTodayStats(silent: true));
  }

  @override
  void dispose() {
    _tabController.dispose();
    _timer?.cancel();
    _rt?.disconnect();
    super.dispose();
  }

  Future<void> _loadTodayStats({bool silent = false}) async {
    if (_statsLoading && silent) return;
    if (mounted) setState(() => _statsLoading = true);
    try {
      final res = await widget.api.todayStats(widget.token);
      final dc  = (res['deliveredCount']    is num) ? (res['deliveredCount']    as num).toInt()    : 0;
      final ip  = (res['inProgressCount']   is num) ? (res['inProgressCount']   as num).toInt()    : 0;
      final cash = (res['cashCollected']    is num) ? (res['cashCollected']     as num).toDouble() : 0.0;
      final commission = (res['estimatedEarnings'] is num) ? (res['estimatedEarnings'] as num).toDouble() : 0.0;
      if (mounted) setState(() {
        _todayDelivered    = dc;
        _todayInProgress   = ip;
        _todayCash         = cash;
        _todayEarnings     = commission;
      });
    } catch (e) {
      // silently fail - show last known values
    } finally {
      if (mounted) setState(() => _statsLoading = false);
    }
  }

  Future<void> _loadActiveOrders({bool silent = false}) async {
    if (!silent) setState(() { _loading = true; _error = null; });
    try {
      final data = await widget.api.activeOrders(widget.token);
      final orders = (data['orders'] as List?) ?? const [];
      setState(() {
        _storeLat = (data['storeLat'] as num?)?.toDouble();
        _storeLng = (data['storeLng'] as num?)?.toDouble();
        _orders = orders.map((e) => Map<String, dynamic>.from(e as Map)).toList();
        _loading = false;
        _error = null;
      });
    } catch (e) {
      if (!silent) setState(() { _loading = false; _error = e.toString(); });
    }
  }

  Widget _orderCard(BuildContext context, int i) {
    final o = _orders[i];
    final id = o['id']?.toString() ?? '';
    final statusCode = (o['currentStatus'] as num?)?.toInt() ?? int.tryParse((o['currentStatus'] ?? '').toString()) ?? -1;
    final status = _statusLabel(statusCode);
    final name = (o['customerName'] ?? '').toString();
    final total = (o['total'] ?? '').toString();
    final theme = Theme.of(context);
    final cs = theme.colorScheme;
    final orderId = o['id'] is num ? (o['id'] as num).toInt() : (i + 1);
    final statusColor = _statusColor(statusCode, cs);
    final statusIcon = _statusIcon(statusCode);

    return InkWell(
      key: ValueKey('order_$orderId'),
      onTap: () async {
        final refreshed = await Navigator.of(context).push<bool>(
          MaterialPageRoute(
            builder: (_) => DriverOrderDetailScreen(
              order: o,
              api: widget.api,
              token: widget.token,
              storeLat: _storeLat,
              storeLng: _storeLng,
            ),
          ),
        );
        if (mounted) {
          _loadActiveOrders(silent: true);
          if (refreshed == true) _loadTodayStats(silent: true);
        }
      },
      borderRadius: BorderRadius.circular(18),
      child: Container(
        margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(18),
          color: cs.surface,
          border: Border.all(color: statusColor.withOpacity(0.25), width: 1.5),
          boxShadow: [
            BoxShadow(
              color: statusColor.withOpacity(0.08),
              blurRadius: 12,
              offset: const Offset(0, 4),
            ),
          ],
        ),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
          child: Row(
            children: [
              // Status Icon Circle
              Container(
                width: 52,
                height: 52,
                decoration: BoxDecoration(
                  color: statusColor.withOpacity(0.12),
                  borderRadius: BorderRadius.circular(14),
                ),
                child: Icon(statusIcon, color: statusColor, size: 26),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Text(
                          'طلب #$id',
                          style: GoogleFonts.cairo(fontWeight: FontWeight.w800, fontSize: 15),
                        ),
                        const Spacer(),
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
                          decoration: BoxDecoration(
                            color: statusColor.withOpacity(0.13),
                            borderRadius: BorderRadius.circular(20),
                          ),
                          child: Text(
                            status,
                            style: GoogleFonts.cairo(
                              fontSize: 11,
                              fontWeight: FontWeight.w600,
                              color: statusColor,
                            ),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 4),
                    Text(
                      name,
                      style: GoogleFonts.cairo(fontSize: 13, color: cs.onSurfaceVariant),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                    const SizedBox(height: 2),
                    Row(
                      children: [
                        Icon(Icons.attach_money_rounded, size: 14, color: DriverTheme.primaryRed),
                        const SizedBox(width: 2),
                        Text(
                          '$total ل.س',
                          style: GoogleFonts.cairo(
                            fontSize: 13,
                            fontWeight: FontWeight.w700,
                            color: DriverTheme.primaryRed,
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 8),
              Icon(Icons.arrow_back_ios_new_rounded, color: cs.outline, size: 16),
            ],
          ),
        ),
      ),
    );
  }

  Widget _statCard(String label, String value, IconData icon, Color color) {
    return Expanded(
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: color.withOpacity(0.09),
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: color.withOpacity(0.2)),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: color, size: 22),
            const SizedBox(height: 8),
            Text(
              value,
              style: GoogleFonts.cairo(fontWeight: FontWeight.w800, fontSize: 20, color: color),
            ),
            const SizedBox(height: 2),
            Text(
              label,
              style: GoogleFonts.cairo(fontSize: 11, color: color.withOpacity(0.8)),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildStatsTab() {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(16),
      child: Column(
        children: [
          // Header section
          Container(
            padding: const EdgeInsets.all(20),
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: [
                  DriverTheme.primaryRed,
                  DriverTheme.primaryRed.withOpacity(0.75),
                ],
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
              ),
              borderRadius: BorderRadius.circular(22),
              boxShadow: [
                BoxShadow(
                  color: DriverTheme.primaryRed.withOpacity(0.3),
                  blurRadius: 16,
                  offset: const Offset(0, 6),
                ),
              ],
            ),
            child: Row(
              children: [
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: Colors.white.withOpacity(0.2),
                    borderRadius: BorderRadius.circular(14),
                  ),
                  child: const Icon(Icons.insights_rounded, color: Colors.white, size: 28),
                ),
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'إحصائيات اليوم',
                        style: GoogleFonts.cairo(
                          fontWeight: FontWeight.w800,
                          fontSize: 18,
                          color: Colors.white,
                        ),
                      ),
                      Text(
                        'ملخص أداءك لهذا اليوم',
                        style: GoogleFonts.cairo(fontSize: 12, color: Colors.white70),
                      ),
                    ],
                  ),
                ),
                if (_statsLoading)
                  const SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2),
                  ),
              ],
            ),
          ),
          const SizedBox(height: 16),

          // Stats grid
          Row(
            children: [
              _statCard('طلبات مُسلّمة', '$_todayDelivered', Icons.done_all_rounded, Colors.green),
              const SizedBox(width: 12),
              _statCard('قيد التنفيذ', '$_todayInProgress', Icons.delivery_dining_rounded, Colors.orange),
            ],
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              _statCard('المبلغ المُحصّل', '${_todayCash.toStringAsFixed(0)} ل.س', Icons.payments_rounded, DriverTheme.primaryRed),
              const SizedBox(width: 12),
              _statCard('العمولة المتوقعة', '${_todayEarnings.toStringAsFixed(0)} ل.س', Icons.account_balance_wallet_rounded, Colors.blue),
            ],
          ),
          const SizedBox(height: 20),

          // Refresh button
          SizedBox(
            width: double.infinity,
            child: OutlinedButton.icon(
              onPressed: _statsLoading ? null : () => _loadTodayStats(),
              icon: _statsLoading
                  ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.refresh_rounded),
              label: Text(
                _statsLoading ? 'جاري التحديث...' : 'تحديث الإحصائيات',
                style: GoogleFonts.cairo(fontWeight: FontWeight.w600),
              ),
              style: OutlinedButton.styleFrom(
                foregroundColor: DriverTheme.primaryRed,
                side: const BorderSide(color: DriverTheme.primaryRed),
                padding: const EdgeInsets.symmetric(vertical: 14),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
              ),
            ),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          centerTitle: false,
          elevation: 0,
          title: Row(
            children: [
              Container(
                padding: const EdgeInsets.all(6),
                decoration: BoxDecoration(
                  color: DriverTheme.primaryRed.withOpacity(0.12),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: const Icon(Icons.delivery_dining_rounded, color: DriverTheme.primaryRed, size: 22),
              ),
              const SizedBox(width: 10),
              Flexible(
                child: Text(
                  _storeName.isNotEmpty ? '$_storeName — مهامي' : 'مهامي الحالية',
                  style: GoogleFonts.cairo(fontWeight: FontWeight.w800, fontSize: 17),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
          bottom: TabBar(
            controller: _tabController,
            indicatorColor: DriverTheme.primaryRed,
            labelColor: DriverTheme.primaryRed,
            unselectedLabelColor: cs.onSurfaceVariant,
            indicatorWeight: 3,
            tabs: [
              Tab(
                icon: const Icon(Icons.insights_rounded),
                child: Text('إحصائيات', style: GoogleFonts.cairo(fontWeight: FontWeight.w600, fontSize: 12)),
              ),
              Tab(
                icon: _newOrdersCount > 0
                    ? Badge(
                        label: Text('$_newOrdersCount', style: GoogleFonts.cairo(fontSize: 11)),
                        backgroundColor: DriverTheme.primaryRed,
                        child: const Icon(Icons.list_alt_rounded),
                      )
                    : const Icon(Icons.list_alt_rounded),
                child: Text('الطلبات', style: GoogleFonts.cairo(fontWeight: FontWeight.w600, fontSize: 12)),
              ),
            ],
          ),
          actions: [
            IconButton(
              onPressed: () => widget.brand.toggleTheme(),
              icon: Icon(widget.brand.isDarkMode ? Icons.light_mode_outlined : Icons.dark_mode_outlined),
              tooltip: 'الوضع الليلي',
            ),
            IconButton(
              onPressed: () { _loadActiveOrders(); _loadTodayStats(); },
              icon: const Icon(Icons.refresh_rounded),
              tooltip: 'تحديث',
            ),
          ],
        ),
        body: _loading
            ? Center(
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    const CircularProgressIndicator(color: DriverTheme.primaryRed),
                    const SizedBox(height: 16),
                    Text('جاري التحميل...', style: GoogleFonts.cairo(fontSize: 14, color: cs.onSurfaceVariant)),
                  ],
                ),
              )
            : _error != null
                ? Center(
                    child: Padding(
                      padding: const EdgeInsets.all(24),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Container(
                            padding: const EdgeInsets.all(20),
                            decoration: BoxDecoration(
                              color: DriverTheme.primaryRed.withOpacity(0.1),
                              shape: BoxShape.circle,
                            ),
                            child: Icon(Icons.error_outline_rounded, size: 48, color: DriverTheme.primaryRed),
                          ),
                          const SizedBox(height: 20),
                          Text('حدث خطأ', style: GoogleFonts.cairo(fontWeight: FontWeight.w800, fontSize: 20)),
                          const SizedBox(height: 8),
                          Text('$_error', textAlign: TextAlign.center, style: GoogleFonts.cairo(fontSize: 14, color: cs.onSurfaceVariant)),
                          const SizedBox(height: 24),
                          FilledButton.icon(
                            onPressed: () => _loadActiveOrders(),
                            icon: const Icon(Icons.refresh_rounded),
                            label: Text('إعادة المحاولة', style: GoogleFonts.cairo()),
                            style: FilledButton.styleFrom(
                              backgroundColor: DriverTheme.primaryRed,
                              padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 12),
                              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
                            ),
                          ),
                        ],
                      ),
                    ),
                  )
                : TabBarView(
                    controller: _tabController,
                    children: [
                      KeyedSubtree(key: const ValueKey('tab_stats'), child: _buildStatsTab()),
                      KeyedSubtree(
                        key: const ValueKey('tab_orders'),
                        child: _orders.isEmpty
                          ? Center(
                              child: Column(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Container(
                                    padding: const EdgeInsets.all(24),
                                    decoration: BoxDecoration(
                                      color: cs.surfaceContainerHighest,
                                      shape: BoxShape.circle,
                                    ),
                                    child: Icon(Icons.inbox_rounded, size: 56, color: cs.onSurfaceVariant.withOpacity(0.5)),
                                  ),
                                  const SizedBox(height: 20),
                                  Text(
                                    'لا توجد طلبات نشطة',
                                    style: GoogleFonts.cairo(fontSize: 18, fontWeight: FontWeight.w700, color: cs.onSurfaceVariant),
                                  ),
                                  const SizedBox(height: 8),
                                  Text(
                                    'ستظهر الطلبات هنا عند تعيينها لك',
                                    style: GoogleFonts.cairo(fontSize: 14, color: cs.onSurfaceVariant.withOpacity(0.7)),
                                  ),
                                ],
                              ),
                            )
                          : RefreshIndicator(
                              color: DriverTheme.primaryRed,
                              onRefresh: () => _loadActiveOrders(),
                              child: ListView.builder(
                                padding: const EdgeInsets.only(top: 12, bottom: 24),
                                itemCount: _orders.length,
                                itemBuilder: (context, i) => _orderCard(context, i),
                              ),
                            ),
                      ),
                    ],
                  ),
      ),
    );
  }
}
