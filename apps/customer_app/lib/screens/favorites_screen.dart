import 'package:flutter/material.dart';
import '../services/api.dart';
import '../models/app_state.dart';

class FavoritesScreen extends StatefulWidget {
  const FavoritesScreen({super.key, required this.api, required this.state});
  final ApiClient api;
  final AppState state;

  @override
  State<FavoritesScreen> createState() => _FavoritesScreenState();
}

class _FavoritesScreenState extends State<FavoritesScreen> {
  List<dynamic> _favorites = [];
  bool _loading = true;
  String? _error;
  int _lastFavVersion = -1;

  @override
  void initState() {
    super.initState();
    widget.state.addListener(_onStateChanged);
    _load();
  }

  @override
  void dispose() {
    widget.state.removeListener(_onStateChanged);
    super.dispose();
  }

  void _onStateChanged() {
    // Only reload from API when favoriteVersion actually changes
    final newVersion = widget.state.favoriteVersion;
    if (newVersion != _lastFavVersion && mounted) {
      _load();
    }
  }

  Future<void> _load() async {
    final cid = widget.state.customerId;
    if (cid == null) {
      if (mounted) setState(() { _loading = false; _error = 'يجب تسجيل الدخول أولاً'; });
      return;
    }
    _lastFavVersion = widget.state.favoriteVersion;
    try {
      final favs = await widget.api.getFavorites(cid);
      // Resolve relative imageUrls to absolute
      final baseUrl = widget.api.baseUrl;
      final resolved = favs.map((f) {
        final raw = f['imageUrl']?.toString() ?? '';
        String abs = raw;
        if (raw.isNotEmpty && !raw.startsWith('http')) {
          final b = baseUrl.endsWith('/') ? baseUrl.substring(0, baseUrl.length - 1) : baseUrl;
          abs = '$b${raw.startsWith('/') ? raw : '/$raw'}';
        }
        return {...(f as Map<String, dynamic>), 'imageUrl': abs};
      }).toList();

      // Sync local state with server data
      widget.state.setFavorites(favs);
      _lastFavVersion = widget.state.favoriteVersion;

      if (mounted) {
        setState(() {
          _favorites = resolved;
          _loading = false;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted) setState(() { _loading = false; _error = 'فشل تحميل المفضلة، تحقق من الاتصال'; });
    }
  }

  Future<void> _removeFavorite(int productId) async {
    final cid = widget.state.customerId;
    if (cid == null) return;
    // Optimistic UI update
    setState(() {
      _favorites.removeWhere((f) => (f['productId'] as num?)?.toInt() == productId);
    });
    widget.state.toggleFavoriteLocal(productId);
    _lastFavVersion = widget.state.favoriteVersion; // prevent reload loop

    try {
      await widget.api.toggleFavorite(customerId: cid, productId: productId);
    } catch (_) {
      // Revert on failure - reload from server
      _load();
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.favorite, color: Colors.red, size: 22),
            const SizedBox(width: 8),
            const Text('المفضلة'),
          ],
        ),
        centerTitle: true,
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh_rounded),
            tooltip: 'تحديث',
            onPressed: () {
              setState(() { _loading = true; });
              _load();
            },
          ),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? Center(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Icon(Icons.error_outline_rounded, size: 64, color: cs.error),
                      const SizedBox(height: 16),
                      Text(_error!, style: theme.textTheme.titleMedium, textAlign: TextAlign.center),
                      const SizedBox(height: 12),
                      FilledButton.tonal(
                        onPressed: () { setState(() { _loading = true; }); _load(); },
                        child: const Text('إعادة المحاولة'),
                      ),
                    ],
                  ),
                )
              : _favorites.isEmpty
                  ? Center(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Icon(Icons.favorite_border_rounded, size: 80, color: cs.outline.withOpacity(0.4)),
                          const SizedBox(height: 20),
                          Text('قائمة المفضلة فارغة',
                              style: theme.textTheme.titleLarge?.copyWith(fontWeight: FontWeight.bold)),
                          const SizedBox(height: 8),
                          Text('اضغط على 💜 في أي منتج لإضافته هنا',
                              style: theme.textTheme.bodyMedium?.copyWith(color: cs.outline)),
                        ],
                      ),
                    )
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: ListView.builder(
                        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                        itemCount: _favorites.length,
                        itemBuilder: (ctx, i) {
                          final f = _favorites[i] as Map;
                          final productId = (f['productId'] as num?)?.toInt() ?? 0;
                          final name = f['name']?.toString() ?? '';
                          final price = (f['price'] as num?)?.toDouble() ?? 0;
                          final imageUrl = f['imageUrl']?.toString() ?? '';
                          final isAvailable = f['isAvailable'] == true;

                          return Dismissible(
                            key: Key('fav_$productId'),
                            direction: DismissDirection.endToStart,
                            background: Container(
                              margin: const EdgeInsets.only(bottom: 10),
                              decoration: BoxDecoration(
                                color: Colors.red.shade400,
                                borderRadius: BorderRadius.circular(16),
                              ),
                              alignment: Alignment.centerRight,
                              padding: const EdgeInsets.only(right: 20),
                              child: const Icon(Icons.delete_rounded, color: Colors.white, size: 28),
                            ),
                            onDismissed: (_) => _removeFavorite(productId),
                            child: Card(
                              margin: const EdgeInsets.only(bottom: 10),
                              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
                              elevation: 2,
                              child: InkWell(
                                borderRadius: BorderRadius.circular(16),
                                onTap: isAvailable
                                    ? () {
                                        widget.state.addToCartBasic(
                                          productId: productId,
                                          name: name,
                                          basePrice: price,
                                        );
                                        ScaffoldMessenger.of(context).showSnackBar(
                                          SnackBar(
                                            content: Text('تمت إضافة $name للسلة 🛒'),
                                            duration: const Duration(seconds: 2),
                                            behavior: SnackBarBehavior.floating,
                                            shape: RoundedRectangleBorder(
                                                borderRadius: BorderRadius.circular(12)),
                                          ),
                                        );
                                      }
                                    : null,
                                child: Padding(
                                  padding: const EdgeInsets.all(10),
                                  child: Row(
                                    children: [
                                      // Product Image
                                      ClipRRect(
                                        borderRadius: BorderRadius.circular(12),
                                        child: imageUrl.isNotEmpty
                                            ? Image.network(
                                                imageUrl,
                                                width: 72,
                                                height: 72,
                                                fit: BoxFit.cover,
                                                errorBuilder: (_, __, ___) => _placeholderImage(cs),
                                              )
                                            : _placeholderImage(cs),
                                      ),
                                      const SizedBox(width: 12),
                                      // Product Info
                                      Expanded(
                                        child: Column(
                                          crossAxisAlignment: CrossAxisAlignment.start,
                                          children: [
                                            Text(
                                              name,
                                              style: theme.textTheme.titleMedium
                                                  ?.copyWith(fontWeight: FontWeight.bold),
                                              maxLines: 2,
                                              overflow: TextOverflow.ellipsis,
                                            ),
                                            const SizedBox(height: 4),
                                            Row(
                                              children: [
                                                Text(
                                                  '${price.toStringAsFixed(0)} ل.س',
                                                  style: theme.textTheme.bodyMedium?.copyWith(
                                                    color: cs.primary,
                                                    fontWeight: FontWeight.w600,
                                                  ),
                                                ),
                                                if (!isAvailable) ...[
                                                  const SizedBox(width: 8),
                                                  Container(
                                                    padding: const EdgeInsets.symmetric(
                                                        horizontal: 6, vertical: 2),
                                                    decoration: BoxDecoration(
                                                      color: cs.errorContainer,
                                                      borderRadius: BorderRadius.circular(6),
                                                    ),
                                                    child: Text(
                                                      'غير متوفر',
                                                      style: TextStyle(
                                                          fontSize: 11,
                                                          color: cs.onErrorContainer),
                                                    ),
                                                  ),
                                                ],
                                              ],
                                            ),
                                            if (isAvailable) ...[
                                              const SizedBox(height: 4),
                                              Text(
                                                'اضغط للإضافة للسلة',
                                                style: theme.textTheme.bodySmall
                                                    ?.copyWith(color: cs.outline),
                                              ),
                                            ],
                                          ],
                                        ),
                                      ),
                                      // Remove button
                                      IconButton(
                                        icon: const Icon(Icons.favorite, color: Colors.red),
                                        onPressed: () => _removeFavorite(productId),
                                        tooltip: 'إزالة من المفضلة',
                                      ),
                                    ],
                                  ),
                                ),
                              ),
                            ),
                          );
                        },
                      ),
                    ),
    );
  }

  Widget _placeholderImage(ColorScheme cs) {
    return Container(
      width: 72,
      height: 72,
      decoration: BoxDecoration(
        color: cs.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Icon(Icons.fastfood_rounded, color: cs.outline, size: 32),
    );
  }
}
