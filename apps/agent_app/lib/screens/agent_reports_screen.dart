import 'dart:math' show max;
import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import '../services/agent_api.dart';
import '../models/agent_state.dart';
import '../app_config.dart';

class AgentReportsScreen extends StatefulWidget {
  const AgentReportsScreen({super.key, required this.state});
  final AgentState state;

  @override
  State<AgentReportsScreen> createState() => _AgentReportsScreenState();
}

class _AgentReportsScreenState extends State<AgentReportsScreen>
    with SingleTickerProviderStateMixin {
  late final AgentApi _api;
  late TabController _tabController;

  Map<String, dynamic>? _daily;
  bool _dailyLoading = true;

  Map<String, dynamic>? _monthly;
  bool _monthlyLoading = true;
  int _selectedMonth = DateTime.now().month;
  int _selectedYear = DateTime.now().year;

  List<dynamic> _topProducts = [];
  bool _topLoading = true;

  @override
  void initState() {
    super.initState();
    _api = AgentApi(baseUrl: kBackendBaseUrl);
    _tabController = TabController(length: 3, vsync: this);
    _loadAll();
  }

  @override
  void dispose() {
    _tabController.dispose();
    super.dispose();
  }

  Future<void> _loadAll() async {
    _loadDaily();
    _loadMonthly();
    _loadTopProducts();
  }

  Future<void> _loadDaily() async {
    setState(() => _dailyLoading = true);
    try {
      final data = await _api.getDailyReport(widget.state.token ?? '');
      if (mounted)
        setState(() {
          _daily = data;
          _dailyLoading = false;
        });
    } catch (_) {
      if (mounted) setState(() => _dailyLoading = false);
    }
  }

  Future<void> _loadMonthly() async {
    setState(() => _monthlyLoading = true);
    try {
      final data = await _api.getMonthlyReport(
        widget.state.token ?? '',
        year: _selectedYear,
        month: _selectedMonth,
      );
      if (mounted)
        setState(() {
          _monthly = data;
          _monthlyLoading = false;
        });
    } catch (_) {
      if (mounted) setState(() => _monthlyLoading = false);
    }
  }

  Future<void> _loadTopProducts() async {
    setState(() => _topLoading = true);
    try {
      final data =
          await _api.getTopProducts(widget.state.token ?? '', limit: 10);
      if (mounted)
        setState(() {
          _topProducts = data;
          _topLoading = false;
        });
    } catch (_) {
      if (mounted) setState(() => _topLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text('التقارير',
            style: GoogleFonts.cairo(fontWeight: FontWeight.w800)),
        centerTitle: true,
        bottom: TabBar(
          controller: _tabController,
          tabs: const [
            Tab(text: 'اليوم'),
            Tab(text: 'الشهر'),
            Tab(text: 'المنتجات'),
          ],
        ),
      ),
      body: TabBarView(
        controller: _tabController,
        children: [
          _buildDailyTab(cs),
          _buildMonthlyTab(cs),
          _buildTopProductsTab(cs),
        ],
      ),
    );
  }

  Widget _buildDailyTab(ColorScheme cs) {
    if (_dailyLoading) return const Center(child: CircularProgressIndicator());

    final d = _daily;
    if (d == null) {
      return _errorWidget('تعذر تحميل بيانات اليوم', _loadDaily);
    }

    final sales = _toDouble(d['totalSales']);
    final commission = _toDouble(d['commissionDue']);
    final net = _toDouble(d['netIncome']);
    final orders = d['orderCount'] as int? ?? 0;

    return RefreshIndicator(
      onRefresh: _loadDaily,
      child: SingleChildScrollView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Container(
              padding: const EdgeInsets.all(14),
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  colors: [cs.primary, cs.primary.withOpacity(0.7)],
                ),
                borderRadius: BorderRadius.circular(16),
              ),
              child: Column(
                children: [
                  Text(
                    'تقرير اليوم',
                    style: GoogleFonts.cairo(color: Colors.white, fontSize: 13),
                  ),
                  Text(
                    _formatDate(DateTime.now()),
                    style: GoogleFonts.cairo(
                      color: Colors.white,
                      fontSize: 18,
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                    child: _statCard('عدد الطلبات', '$orders',
                        Icons.shopping_bag_outlined, Colors.blue, cs)),
                const SizedBox(width: 12),
                Expanded(
                    child: _statCard('إجمالي المبيعات', _fmtMoney(sales),
                        Icons.bar_chart_rounded, Colors.green, cs)),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                    child: _statCard('صافي دخلك', _fmtMoney(commission),
                        Icons.percent_rounded, const Color(0xFFD4AF37), cs)),
                const SizedBox(width: 12),
                Expanded(
                    child: _statCard(
                        'عمولة المتجر',
                        _fmtMoney(net),
                        Icons.account_balance_wallet_outlined,
                        Colors.purple,
                        cs)),
              ],
            ),
            const SizedBox(height: 24),
            Container(
              padding: const EdgeInsets.all(20),
              decoration: BoxDecoration(
                color: Colors.green.withOpacity(0.1),
                borderRadius: BorderRadius.circular(16),
                border: Border.all(color: Colors.green.withOpacity(0.3)),
              ),
              child: Column(
                children: [
                  Icon(Icons.savings_outlined,
                      size: 36, color: Colors.green.shade700),
                  const SizedBox(height: 8),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildMonthlyTab(ColorScheme cs) {
    return Column(
      children: [
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
          color: cs.surface,
          child: Row(
            children: [
              Expanded(
                child: DropdownButtonFormField<int>(
                  value: _selectedMonth,
                  decoration: const InputDecoration(
                    labelText: 'الشهر',
                    isDense: true,
                    border: OutlineInputBorder(),
                  ),
                  items: List.generate(
                      12,
                      (i) => DropdownMenuItem(
                            value: i + 1,
                            child: Text(_monthName(i + 1),
                                style: GoogleFonts.cairo()),
                          )),
                  onChanged: (v) {
                    if (v != null) setState(() => _selectedMonth = v);
                  },
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: DropdownButtonFormField<int>(
                  value: _selectedYear,
                  decoration: const InputDecoration(
                    labelText: 'السنة',
                    isDense: true,
                    border: OutlineInputBorder(),
                  ),
                  items: [
                    DateTime.now().year,
                    DateTime.now().year - 1,
                  ]
                      .map((y) => DropdownMenuItem(
                            value: y,
                            child: Text('$y', style: GoogleFonts.cairo()),
                          ))
                      .toList(),
                  onChanged: (v) {
                    if (v != null) setState(() => _selectedYear = v);
                  },
                ),
              ),
              const SizedBox(width: 10),
              ElevatedButton(
                onPressed: _loadMonthly,
                child: Text('بحث', style: GoogleFonts.cairo()),
              ),
            ],
          ),
        ),
        Expanded(
          child: _monthlyLoading
              ? const Center(child: CircularProgressIndicator())
              : _monthly == null
                  ? _errorWidget('تعذر تحميل البيانات', _loadMonthly)
                  : _buildMonthlyContent(cs),
        ),
      ],
    );
  }

  Widget _buildMonthlyContent(ColorScheme cs) {
    final m = _monthly!;
    final sales = _toDouble(m['totalSales']);
    final commission = _toDouble(m['totalCommission']);
    final net = _toDouble(m['totalNetIncome']);
    final orders = m['totalOrders'] as int? ?? 0;
    final details = (m['details'] as List?) ?? [];

    return RefreshIndicator(
      onRefresh: _loadMonthly,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Row(children: [
            Expanded(
                child: _statCard('الطلبات', '$orders',
                    Icons.receipt_long_outlined, Colors.blue, cs)),
            const SizedBox(width: 12),
            Expanded(
                child: _statCard('المبيعات', _fmtMoney(sales),
                    Icons.trending_up_rounded, Colors.green, cs)),
          ]),
          const SizedBox(height: 12),
          Row(children: [
            Expanded(
                child: _statCard('الصافي', _fmtMoney(commission),
                    Icons.percent_rounded, const Color(0xFFD4AF37), cs)),
            const SizedBox(width: 12),
            Expanded(
                child: _statCard('العمولة', _fmtMoney(net),
                    Icons.account_balance_wallet_outlined, Colors.purple, cs)),
          ]),
          const SizedBox(height: 16),
          if (details.isNotEmpty) ...[
            Text(
              'تفاصيل الطلبات',
              style:
                  GoogleFonts.cairo(fontWeight: FontWeight.w700, fontSize: 16),
            ),
            const SizedBox(height: 8),
            _buildMiniChart(details, cs),
            const SizedBox(height: 16),
          ],
          if (details.isEmpty)
            Center(
              child: Padding(
                padding: const EdgeInsets.all(32),
                child: Text(
                  'لا توجد طلبات في هذه الفترة',
                  style: GoogleFonts.cairo(color: cs.onSurfaceVariant),
                ),
              ),
            )
          else
            ...details.map((d) => _orderRow(d, cs)),
        ],
      ),
    );
  }

  Widget _buildMiniChart(List details, ColorScheme cs) {
    final maxVal = details.fold<double>(
      0.0,
      (prev, d) => max(prev, _toDouble(d['saleAmount'])),
    );

    return Container(
      height: 80,
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: details.take(30).map((d) {
          final val = _toDouble(d['saleAmount']);
          final ratio = maxVal > 0 ? val / maxVal : 0.0;
          return Expanded(
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 1),
              child: Container(
                height: 60 * ratio + 4,
                decoration: BoxDecoration(
                  color: cs.primary.withOpacity(0.7),
                  borderRadius: BorderRadius.circular(3),
                ),
              ),
            ),
          );
        }).toList(),
      ),
    );
  }

  Widget _orderRow(Map d, ColorScheme cs) {
    final amount = _toDouble(d['saleAmount']);
    final net = _toDouble(d['netIncome']);
    final settled = d['settledAt'] != null;
    final date = _parseDate(d['date']?.toString() ?? '');

    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: cs.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: cs.outlineVariant.withOpacity(0.5)),
      ),
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: cs.primaryContainer,
              borderRadius: BorderRadius.circular(10),
            ),
            child: Center(
              child: Text(
                '#${d['orderId']}',
                style: GoogleFonts.cairo(
                  fontSize: 11,
                  fontWeight: FontWeight.w700,
                  color: cs.primary,
                ),
              ),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  date,
                  style: GoogleFonts.cairo(
                      fontSize: 12, color: cs.onSurfaceVariant),
                ),
                Text(
                  'مبيعات: ${_fmtMoney(amount)}',
                  style: GoogleFonts.cairo(
                      fontSize: 13, fontWeight: FontWeight.w600),
                ),
              ],
            ),
          ),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Text(
                _fmtMoney(net),
                style: GoogleFonts.cairo(
                  fontWeight: FontWeight.w800,
                  color: Colors.green.shade700,
                ),
              ),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                decoration: BoxDecoration(
                  color:
                      settled ? Colors.grey.shade200 : const Color(0xFFFDF8E1),
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Text(
                  settled ? 'مسوَّى' : 'معلق',
                  style: GoogleFonts.cairo(
                    fontSize: 10,
                    color: settled
                        ? Colors.grey.shade700
                        : const Color(0xFF8B6914),
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _buildTopProductsTab(ColorScheme cs) {
    if (_topLoading) return const Center(child: CircularProgressIndicator());
    if (_topProducts.isEmpty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.bar_chart_rounded,
                  size: 64, color: cs.onSurface.withOpacity(0.2)),
              const SizedBox(height: 16),
              Text(
                'لا توجد بيانات مبيعات بعد',
                style:
                    GoogleFonts.cairo(color: cs.onSurfaceVariant, fontSize: 16),
              ),
            ],
          ),
        ),
      );
    }

    final maxRev = _topProducts.fold<double>(
      0.0,
      (p, d) => max(p, _toDouble(d['totalRevenue'])),
    );

    return RefreshIndicator(
      onRefresh: () async => _loadTopProducts(),
      child: ListView.builder(
        padding: const EdgeInsets.all(16),
        itemCount: _topProducts.length,
        itemBuilder: (ctx, i) {
          final p = _topProducts[i];
          final name = p['productNameSnapshot']?.toString() ?? 'منتج غير معروف';
          final qty = p['totalQuantity'] as int? ?? 0;
          final rev = _toDouble(p['totalRevenue']);
          final ratio = maxRev > 0 ? rev / maxRev : 0.0;

          return Container(
            margin: const EdgeInsets.only(bottom: 12),
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(
              color: cs.surface,
              borderRadius: BorderRadius.circular(16),
              boxShadow: [
                BoxShadow(
                  color: Colors.black.withOpacity(0.05),
                  blurRadius: 8,
                  offset: const Offset(0, 2),
                ),
              ],
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Container(
                      width: 32,
                      height: 32,
                      decoration: BoxDecoration(
                        color: i == 0
                            ? Colors.amber
                            : i == 1
                                ? Colors.grey.shade400
                                : i == 2
                                    ? Colors.brown.shade300
                                    : cs.primaryContainer,
                        shape: BoxShape.circle,
                      ),
                      child: Center(
                        child: Text(
                          '${i + 1}',
                          style: TextStyle(
                            fontWeight: FontWeight.w900,
                            color: i < 3 ? Colors.white : cs.primary,
                            fontSize: 13,
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Text(
                        name,
                        style: GoogleFonts.cairo(
                            fontWeight: FontWeight.w700, fontSize: 14),
                      ),
                    ),
                    Text(
                      _fmtMoney(rev),
                      style: GoogleFonts.cairo(
                        fontWeight: FontWeight.w800,
                        color: Colors.green.shade700,
                        fontSize: 14,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                ClipRRect(
                  borderRadius: BorderRadius.circular(4),
                  child: LinearProgressIndicator(
                    value: ratio,
                    minHeight: 8,
                    backgroundColor: cs.primaryContainer.withOpacity(0.3),
                    valueColor: AlwaysStoppedAnimation(
                      i == 0 ? Colors.amber : cs.primary,
                    ),
                  ),
                ),
                const SizedBox(height: 6),
                Text(
                  'الكمية المباعة: $qty وحدة',
                  style: GoogleFonts.cairo(
                    fontSize: 12,
                    color: cs.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          );
        },
      ),
    );
  }

  Widget _statCard(
      String label, String value, IconData icon, Color color, ColorScheme cs) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: color.withOpacity(0.08),
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
            style: GoogleFonts.cairo(
              fontSize: 18,
              fontWeight: FontWeight.w900,
              color: color,
            ),
          ),
          Text(
            label,
            style: GoogleFonts.cairo(
              fontSize: 12,
              color: cs.onSurfaceVariant,
            ),
          ),
        ],
      ),
    );
  }

  Widget _errorWidget(String msg, VoidCallback retry) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.error_outline, size: 48, color: Colors.red),
          const SizedBox(height: 12),
          Text(msg),
          const SizedBox(height: 12),
          ElevatedButton(onPressed: retry, child: const Text('إعادة المحاولة')),
        ],
      ),
    );
  }

  double _toDouble(dynamic v) {
    if (v == null) return 0.0;
    if (v is double) return v;
    if (v is int) return v.toDouble();
    return double.tryParse(v.toString()) ?? 0.0;
  }

  String _fmtMoney(double v) => '${v.toStringAsFixed(0)} ل.س';

  String _formatDate(DateTime d) => '${d.day}/${d.month}/${d.year}';

  String _parseDate(String iso) {
    try {
      final d = DateTime.parse(iso).toLocal();
      return _formatDate(d);
    } catch (_) {
      return iso;
    }
  }

  String _monthName(int m) {
    const names = [
      '',
      'يناير',
      'فبراير',
      'مارس',
      'أبريل',
      'مايو',
      'يونيو',
      'يوليو',
      'أغسطس',
      'سبتمبر',
      'أكتوبر',
      'نوفمبر',
      'ديسمبر'
    ];
    return names[m];
  }
}
