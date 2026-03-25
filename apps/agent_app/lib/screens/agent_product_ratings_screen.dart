import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import '../models/agent_state.dart';
import '../services/agent_api.dart';
import '../app_config.dart';

class AgentProductRatingsScreen extends StatefulWidget {
  const AgentProductRatingsScreen({super.key, required this.api, required this.state});
  final dynamic api;
  final AgentState state;

  @override
  State<AgentProductRatingsScreen> createState() => _AgentProductRatingsScreenState();
}

class _AgentProductRatingsScreenState extends State<AgentProductRatingsScreen> {
  late final AgentApi _api;
  bool _loading = true;
  String? _error;
  List<dynamic> _ratings = [];
  int _totalCount = 0;
  double? _avgStars;
  int _page = 1;
  static const int _limit = 20;
  bool _hasMore = true;
  bool _loadingMore = false;

  final ScrollController _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    _api = AgentApi(baseUrl: kBackendBaseUrl);
    _loadRatings();
    _scroll.addListener(_onScroll);
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (_scroll.position.pixels >= _scroll.position.maxScrollExtent - 200 &&
        _hasMore &&
        !_loadingMore) {
      _loadMore();
    }
  }

  Future<void> _loadRatings({bool refresh = false}) async {
    if (refresh) {
      setState(() {
        _page = 1;
        _hasMore = true;
        _ratings = [];
        _loading = true;
        _error = null;
      });
    }
    try {
      final data = await _api.getMyProductRatings(widget.state.token ?? '', page: 1, limit: _limit);
      final list = (data['ratings'] as List?) ?? [];
      final total = data['totalCount'] as int? ?? 0;
      final avg = (data['avgStars'] is num) ? (data['avgStars'] as num).toDouble() : null;
      if (mounted) {
        setState(() {
          _ratings = list;
          _totalCount = total;
          _avgStars = avg;
          _loading = false;
          _hasMore = list.length >= _limit;
          _page = 1;
        });
      }
    } catch (e) {
      if (mounted) setState(() { _loading = false; _error = e.toString(); });
    }
  }

  Future<void> _loadMore() async {
    if (_loadingMore || !_hasMore) return;
    setState(() => _loadingMore = true);
    try {
      final nextPage = _page + 1;
      final data = await _api.getMyProductRatings(widget.state.token ?? '', page: nextPage, limit: _limit);
      final list = (data['ratings'] as List?) ?? [];
      if (mounted) {
        setState(() {
          _ratings.addAll(list);
          _page = nextPage;
          _hasMore = list.length >= _limit;
          _loadingMore = false;
        });
      }
    } catch (_) {
      if (mounted) setState(() => _loadingMore = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    if (_loading) return const Center(child: CircularProgressIndicator());

    if (_error != null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.error_outline, size: 48, color: Colors.red),
            const SizedBox(height: 12),
            Text('تعذر تحميل التقييمات', style: GoogleFonts.cairo()),
            const SizedBox(height: 12),
            ElevatedButton(
              onPressed: () => _loadRatings(refresh: true),
              child: Text('إعادة المحاولة', style: GoogleFonts.cairo()),
            ),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: () => _loadRatings(refresh: true),
      child: Column(
        children: [
          // Summary Card
          Container(
            margin: const EdgeInsets.all(16),
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: [cs.primary, cs.primary.withOpacity(0.7)],
              ),
              borderRadius: BorderRadius.circular(16),
            ),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'إجمالي التقييمات',
                        style: GoogleFonts.cairo(color: Colors.white70, fontSize: 13),
                      ),
                      Text(
                        '$_totalCount تقييم',
                        style: GoogleFonts.cairo(
                          color: Colors.white,
                          fontSize: 22,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                    ],
                  ),
                ),
                if (_avgStars != null) ...[
                  const SizedBox(width: 16),
                  Column(
                    children: [
                      Row(
                        children: List.generate(5, (i) {
                          return Icon(
                            i < _avgStars!.round() ? Icons.star_rounded : Icons.star_outline_rounded,
                            color: Colors.amber,
                            size: 20,
                          );
                        }),
                      ),
                      Text(
                        _avgStars!.toStringAsFixed(1),
                        style: GoogleFonts.cairo(
                          color: Colors.white,
                          fontWeight: FontWeight.w800,
                          fontSize: 18,
                        ),
                      ),
                    ],
                  ),
                ],
              ],
            ),
          ),

          // Ratings List
          Expanded(
            child: _ratings.isEmpty
                ? Center(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(Icons.star_outline_rounded, size: 72, color: cs.onSurface.withOpacity(0.2)),
                        const SizedBox(height: 16),
                        Text(
                          'لا توجد تقييمات بعد',
                          style: GoogleFonts.cairo(
                            fontSize: 18,
                            fontWeight: FontWeight.w700,
                            color: cs.onSurfaceVariant,
                          ),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'ستظهر هنا تقييمات عملائك لمنتجاتك',
                          style: GoogleFonts.cairo(
                            fontSize: 14,
                            color: cs.onSurfaceVariant.withOpacity(0.8),
                          ),
                        ),
                      ],
                    ),
                  )
                : ListView.builder(
                    controller: _scroll,
                    padding: const EdgeInsets.only(left: 16, right: 16, bottom: 24),
                    itemCount: _ratings.length + (_loadingMore ? 1 : 0),
                    itemBuilder: (ctx, i) {
                      if (i == _ratings.length) {
                        return const Center(
                          child: Padding(
                            padding: EdgeInsets.all(16),
                            child: CircularProgressIndicator(),
                          ),
                        );
                      }
                      return _ratingCard(_ratings[i], cs);
                    },
                  ),
          ),
        ],
      ),
    );
  }

  Widget _ratingCard(dynamic r, ColorScheme cs) {
    final productName = r['productName']?.toString() ?? r['productNameSnapshot']?.toString() ?? 'منتج';
    final stars = (r['stars'] is num) ? (r['stars'] as num).toInt() : 0;
    final comment = r['comment']?.toString();
    final customerName = r['customerName']?.toString() ?? 'زبون';
    final dateStr = r['createdAt']?.toString() ?? r['date']?.toString() ?? '';
    String dateDisplay = '';
    try {
      if (dateStr.isNotEmpty) {
        final d = DateTime.parse(dateStr).toLocal();
        dateDisplay = '${d.day}/${d.month}/${d.year}';
      }
    } catch (_) {}

    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: cs.surface,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: cs.outlineVariant.withOpacity(0.4)),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.04),
            blurRadius: 6,
            offset: const Offset(0, 2),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  productName,
                  style: GoogleFonts.cairo(fontWeight: FontWeight.w700, fontSize: 14),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              Row(
                children: List.generate(5, (i) => Icon(
                  i < stars ? Icons.star_rounded : Icons.star_outline_rounded,
                  color: Colors.amber,
                  size: 18,
                )),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Row(
            children: [
              Icon(Icons.person_outline_rounded, size: 14, color: cs.onSurfaceVariant),
              const SizedBox(width: 4),
              Text(
                customerName,
                style: GoogleFonts.cairo(fontSize: 12, color: cs.onSurfaceVariant),
              ),
              const Spacer(),
              if (dateDisplay.isNotEmpty)
                Text(
                  dateDisplay,
                  style: GoogleFonts.cairo(fontSize: 11, color: cs.onSurfaceVariant.withOpacity(0.7)),
                ),
            ],
          ),
          if (comment != null && comment.isNotEmpty) ...[
            const SizedBox(height: 8),
            Container(
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(
                color: cs.primaryContainer.withOpacity(0.3),
                borderRadius: BorderRadius.circular(8),
              ),
              child: Text(
                comment,
                style: GoogleFonts.cairo(fontSize: 13),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
