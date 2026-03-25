import 'package:flutter/material.dart';
import 'dart:convert';
import 'package:http/http.dart' as http;
import '../services/api.dart';
import '../models/app_state.dart';

String _absUrl(String baseUrl, String? url) {
  if (url == null || url.trim().isEmpty) return '';
  final u = url.trim();
  if (u.startsWith('http://') || u.startsWith('https://')) return u;
  final b = baseUrl.endsWith('/') ? baseUrl.substring(0, baseUrl.length - 1) : baseUrl;
  final p = u.startsWith('/') ? u : '/$u';
  return '$b$p';
}

class AiChatScreen extends StatefulWidget {
  const AiChatScreen({super.key, required this.api, required this.state});
  final ApiClient api;
  final AppState state;

  @override
  State<AiChatScreen> createState() => _AiChatScreenState();
}

class _AiChatScreenState extends State<AiChatScreen> with TickerProviderStateMixin {
  final List<Map<String, dynamic>> _messages = [];
  final TextEditingController _ctrl = TextEditingController();
  final ScrollController _scroll = ScrollController();
  bool _loading = false;
  late AnimationController _typingController;

  @override
  void initState() {
    super.initState();
    _typingController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 800),
    )..repeat(reverse: true);

    _addMessage(
      role: 'assistant',
      text: 'أهلاً وسهلاً! 👋 أنا مساعدك الذكي في المتجر\n'
          'يمكنني مساعدتك باختيار المنتجات المناسبة، المقارنة بين الخيارات، وإجابة أي سؤال 😊',
    );
  }

  @override
  void dispose() {
    _ctrl.dispose();
    _scroll.dispose();
    _typingController.dispose();
    super.dispose();
  }

  void _addMessage({
    required String role,
    required String text,
    List<Map<String, dynamic>>? products,
  }) {
    setState(() {
      _messages.add({
        'role': role,
        'text': text,
        'products': products,
        'time': DateTime.now(),
      });
    });
    Future.delayed(const Duration(milliseconds: 150), () {
      if (_scroll.hasClients) {
        _scroll.animateTo(
          _scroll.position.maxScrollExtent,
          duration: const Duration(milliseconds: 300),
          curve: Curves.easeOut,
        );
      }
    });
  }

  String _buildMenuContext() {
    final cats = widget.state.menuCategories;
    if (cats.isEmpty) return 'لا تتوفر بيانات المنتجات حالياً.';
    final buf = StringBuffer();
    for (final cat in cats) {
      final catName = cat['Name'] ?? cat['name'] ?? '';
      buf.write('【$catName】\n');
      final products = (cat['products'] is List) ? cat['products'] as List : [];
      for (final p in products) {
        final name = p['Name'] ?? p['name'] ?? '';
        final price = p['price'] ?? 0;
        final desc = (p['Description'] ?? p['description'] ?? '').toString();
        final available = p['IsAvailable'] ?? p['isAvailable'] ?? true;
        final ratingAvg = p['ratingAvg'] ?? 0;
        final ratingCount = p['ratingCount'] ?? 0;
        if (!available) continue;
        buf.write('  • $name | $price ل.س');
        if (desc.isNotEmpty) {
          final short = desc.length > 100 ? desc.substring(0, 100) : desc;
          buf.write(' | $short');
        }
        if ((ratingCount as num) > 0) buf.write(' | ⭐$ratingAvg ($ratingCount تقييم)');
        buf.write('\n');
      }
    }
    return buf.toString();
  }

  Map<String, dynamic>? _findProduct(String productName) {
    final query = productName.toLowerCase().trim();
    for (final cat in widget.state.menuCategories) {
      final products = (cat['products'] is List) ? cat['products'] as List : [];
      for (final p in products) {
        final name = (p['Name'] ?? p['name'] ?? '').toString().toLowerCase();
        if (name.contains(query) || query.contains(name)) {
          return Map<String, dynamic>.from(p as Map);
        }
      }
    }
    
    final words = query.split(RegExp(r'\s+'));
    for (final cat in widget.state.menuCategories) {
      final products = (cat['products'] is List) ? cat['products'] as List : [];
      for (final p in products) {
        final name = (p['Name'] ?? p['name'] ?? '').toString().toLowerCase();
        if (words.any((w) => w.length > 2 && name.contains(w))) {
          return Map<String, dynamic>.from(p as Map);
        }
      }
    }
    return null;
  }

  List<Map<String, dynamic>> _extractProducts(String reply) {
    final matches = RegExp(r'\[\[([^\]]+)\]\]').allMatches(reply);
    final found = <Map<String, dynamic>>[];
    final seen = <String>{};
    for (final m in matches) {
      final name = m.group(1) ?? '';
      if (seen.contains(name)) continue;
      seen.add(name);
      final product = _findProduct(name);
      if (product != null) found.add(product);
    }
    return found;
  }

  Future<void> _send() async {
    final text = _ctrl.text.trim();
    if (text.isEmpty || _loading) return;
    _ctrl.clear();
    _addMessage(role: 'user', text: text);
    setState(() => _loading = true);

    try {
      
      // آخر 8 رسائل فقط = سرعة أفضل
      final recentMessages = _messages.length > 8
          ? _messages.sublist(_messages.length - 8)
          : _messages;

      final history = recentMessages
          .where((m) => m['role'] != null && m['text'] != null)
          .map((m) => {
                'role': m['role'] as String,
                'content': m['text'] as String
              })
          .toList();

      final menuContext = _buildMenuContext();
      final storeName = widget.state.storeName.isNotEmpty ? widget.state.storeName : 'المتجر';

      // system prompt محسّن: واضح، مركّز، يعطي ردوداً أسرع وأدق
      final systemPrompt =
          '''أنت مساعد ذكي لمتجر "$storeName" — تتحدث مباشرة مع الزبائن.

قائمة المنتجات المتاحة:
$menuContext

تعليمات:
• أجب بالعربية دائماً — بأسلوب ودود ومباشر وطبيعي كأنك إنسان حقيقي
• اختصر ردودك: جملة أو جملتان تكفي في الغالب — لا تطل أبداً
• عند ذكر منتج من القائمة اكتب اسمه هكذا: [[اسم المنتج]] ليظهر تلقائياً مع زر الإضافة للسلة
• لا تخترع منتجات خارج القائمة — إذا غير متوفر اقترح بديلاً من القائمة
• إذا سأل عن شيء خارج المتجر، أجب بذكاء وطبيعية ولطف
• تجنّب العبارات التمهيدية — ابدأ بالإجابة مباشرة
• إذا طُلب مقارنة بين منتجين، اعرضها بشكل واضح ومختصر
• لا تستخدم رموز أو أرقام قوائم — الأسلوب طبيعي كالمحادثة''';

      final customerId = widget.state.customerId;
      if (customerId == null) {
        _addMessage(
          role: 'assistant',
          text: '⚠️ يجب تسجيل الدخول أولاً لاستخدام المساعد الذكي.',
        );
        return;
      }

      final serverBase = widget.api.baseUrl.replaceAll(RegExp(r'/$'), '');
      final res = await http
          .post(
            Uri.parse('$serverBase/api/ai/customer-chat'),
            headers: {
              'Content-Type': 'application/json',
              'X-CUSTOMER-ID': '$customerId',
            },
            body: jsonEncode({
              'system': systemPrompt,
              'max_tokens': 800,   // أقل = أسرع
              'messages': history,
            }),
          )
          .timeout(const Duration(seconds: 60));

      if (res.statusCode != 200) {
        Map<String, dynamic> errData = {};
        try {
          errData = jsonDecode(res.body) as Map<String, dynamic>;
        } catch (_) {}
        _addMessage(
          role: 'assistant',
          text: '⚠️ ${errData['error'] ?? 'حدث خطأ في الاتصال بـ AI. يرجى المحاولة مجدداً.'}',
        );
        return;
      }

      final data = jsonDecode(res.body) as Map<String, dynamic>;
      final reply = ((data['content'] as List?) ?? [])
          .map((b) => (b['text'] ?? '').toString())
          .join('')
          .trim();

      if (reply.isEmpty) {
        _addMessage(role: 'assistant', text: 'عذراً، لم أتمكن من الرد. حاول مجدداً.');
        return;
      }

      final suggestedProducts = _extractProducts(reply);
      final cleanReply = reply.replaceAllMapped(
        RegExp(r'\[\[([^\]]+)\]\]'),
        (m) => m.group(1) ?? '',
      );

      _addMessage(
        role: 'assistant',
        text: cleanReply,
        products: suggestedProducts.isNotEmpty ? suggestedProducts : null,
      );
    } catch (e) {
      _addMessage(
        role: 'assistant',
        text: 'حدث خطأ في الاتصال. تأكد من الاتصال بالخادم.',
      );
    } finally {
      setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(
        title: Row(
          children: [
            Container(
              width: 40,
              height: 40,
              decoration: BoxDecoration(
                color: theme.colorScheme.primary,
                shape: BoxShape.circle,
              ),
              child: const Center(
                child: Text('🤖', style: TextStyle(fontSize: 20)),
              ),
            ),
            const SizedBox(width: 10),
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('المساعد الذكي',
                    style: TextStyle(fontSize: 15, fontWeight: FontWeight.bold)),
                Row(
                  children: [
                    Container(
                      width: 8,
                      height: 8,
                      decoration: const BoxDecoration(
                        color: Colors.green,
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: 4),
                    const Text('متصل',
                        style: TextStyle(fontSize: 11, color: Colors.green)),
                  ],
                ),
              ],
            ),
          ],
        ),
        centerTitle: false,
        actions: [
          IconButton(
            icon: const Icon(Icons.delete_outline),
            tooltip: 'مسح المحادثة',
            onPressed: () {
              setState(() {
                _messages.clear();
              });
              _addMessage(
                role: 'assistant',
                text: 'تم مسح المحادثة. كيف يمكنني مساعدتك؟ 😊',
              );
            },
          ),
        ],
      ),
      body: Column(
        children: [
          Expanded(
            child: ListView.builder(
              controller: _scroll,
              padding: const EdgeInsets.all(12),
              itemCount: _messages.length,
              itemBuilder: (ctx, i) {
                final msg = _messages[i];
                final isAi = msg['role'] == 'assistant';
                final text = msg['text'] as String;
                final products =
                    (msg['products'] as List?)?.cast<Map<String, dynamic>>();
                final time = msg['time'] as DateTime;

                return Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Column(
                    crossAxisAlignment: isAi
                        ? CrossAxisAlignment.start
                        : CrossAxisAlignment.end,
                    children: [
                      Row(
                        mainAxisAlignment: isAi
                            ? MainAxisAlignment.start
                            : MainAxisAlignment.end,
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          if (isAi) ...[
                            Container(
                              width: 30,
                              height: 30,
                              margin: const EdgeInsets.only(left: 6),
                              decoration: BoxDecoration(
                                color: theme.colorScheme.primary.withOpacity(0.15),
                                shape: BoxShape.circle,
                              ),
                              child: const Center(
                                child: Text('🤖', style: TextStyle(fontSize: 14)),
                              ),
                            ),
                          ],
                          Flexible(
                            child: Container(
                              padding: const EdgeInsets.symmetric(
                                  horizontal: 14, vertical: 10),
                              constraints: BoxConstraints(
                                  maxWidth:
                                      MediaQuery.of(context).size.width * 0.75),
                              decoration: BoxDecoration(
                                color: isAi
                                    ? theme.colorScheme.surfaceContainerHighest
                                    : theme.colorScheme.primary,
                                borderRadius: BorderRadius.circular(18).copyWith(
                                  bottomLeft: isAi
                                      ? const Radius.circular(4)
                                      : const Radius.circular(18),
                                  bottomRight: !isAi
                                      ? const Radius.circular(4)
                                      : const Radius.circular(18),
                                ),
                                boxShadow: [
                                  BoxShadow(
                                    color: Colors.black.withOpacity(0.06),
                                    blurRadius: 4,
                                    offset: const Offset(0, 2),
                                  ),
                                ],
                              ),
                              child: Text(
                                text,
                                style: TextStyle(
                                  color: isAi
                                      ? theme.colorScheme.onSurface
                                      : Colors.white,
                                  height: 1.5,
                                  fontSize: 14,
                                ),
                              ),
                            ),
                          ),
                        ],
                      ),
                      Padding(
                        padding: EdgeInsets.only(
                          top: 3,
                          left: isAi ? 42 : 0,
                          right: isAi ? 0 : 4,
                        ),
                        child: Text(
                          '${time.hour.toString().padLeft(2, '0')}:${time.minute.toString().padLeft(2, '0')}',
                          style: const TextStyle(
                              fontSize: 10, color: Colors.grey),
                        ),
                      ),
                      if (products != null && products.isNotEmpty)
                        Padding(
                          padding: const EdgeInsets.only(top: 8, right: 8),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: products
                                .map((p) => _buildProductCard(context, p))
                                .toList(),
                          ),
                        ),
                    ],
                  ),
                );
              },
            ),
          ),
          if (_loading)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
              child: Row(
                children: [
                  Container(
                    width: 30,
                    height: 30,
                    decoration: BoxDecoration(
                      color: Theme.of(context)
                          .colorScheme
                          .primary
                          .withOpacity(0.15),
                      shape: BoxShape.circle,
                    ),
                    child: const Center(
                      child: Text('🤖', style: TextStyle(fontSize: 14)),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Container(
                    padding: const EdgeInsets.symmetric(
                        horizontal: 14, vertical: 10),
                    decoration: BoxDecoration(
                      color: Theme.of(context)
                          .colorScheme
                          .surfaceContainerHighest,
                      borderRadius: BorderRadius.circular(18)
                          .copyWith(bottomLeft: const Radius.circular(4)),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        AnimatedBuilder(
                          animation: _typingController,
                          builder: (_, __) {
                            return Row(
                              children: List.generate(3, (i) {
                                final offset = i * 0.33;
                                final v = (_typingController.value - offset)
                                    .clamp(0.0, 1.0);
                                return Container(
                                  margin: const EdgeInsets.symmetric(
                                      horizontal: 2),
                                  width: 6,
                                  height: 6 + v * 4,
                                  decoration: BoxDecoration(
                                    color: Theme.of(context)
                                        .colorScheme
                                        .primary
                                        .withOpacity(0.5 + v * 0.5),
                                    shape: BoxShape.circle,
                                  ),
                                );
                              }),
                            );
                          },
                        ),
                        const SizedBox(width: 6),
                        const Text('يفكر...',
                            style:
                                TextStyle(color: Colors.grey, fontSize: 12)),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          
          if (_messages.length <= 2 && !_loading)
            SizedBox(
              height: 44,
              child: ListView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(
                    horizontal: 12, vertical: 4),
                children: [
                  'ما هي أشهر المنتجات؟',
                  'ما الأفضل بسعر أقل من 5000 ل.س؟',
                  'منتجات بتقييم عالٍ ⭐',
                  'قارن بين منتجين',
                ]
                    .map((q) => Padding(
                          padding: const EdgeInsets.only(left: 8),
                          child: ActionChip(
                            label: Text(q,
                                style: const TextStyle(fontSize: 12)),
                            onPressed: () {
                              _ctrl.text = q;
                              _send();
                            },
                          ),
                        ))
                    .toList(),
              ),
            ),
          Padding(
            padding: EdgeInsets.only(
              left: 12,
              right: 12,
              top: 8,
              bottom: MediaQuery.of(context).viewInsets.bottom + 12,
            ),
            child: Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _ctrl,
                    decoration: InputDecoration(
                      hintText: 'اسأل عن أي شيء...',
                      border: OutlineInputBorder(
                          borderRadius: BorderRadius.circular(26)),
                      contentPadding: const EdgeInsets.symmetric(
                          horizontal: 18, vertical: 12),
                      filled: true,
                      fillColor: Theme.of(context)
                          .colorScheme
                          .surfaceContainerHighest
                          .withOpacity(0.5),
                    ),
                    textDirection: TextDirection.rtl,
                    maxLines: 3,
                    minLines: 1,
                    onSubmitted: (_) => _send(),
                  ),
                ),
                const SizedBox(width: 8),
                FloatingActionButton(
                  onPressed: _loading ? null : _send,
                  mini: false,
                  elevation: 2,
                  child: _loading
                      ? const SizedBox(
                          width: 20,
                          height: 20,
                          child:
                              CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white),
                        )
                      : const Icon(Icons.send),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildProductCard(BuildContext context, Map<String, dynamic> product) {
    final theme = Theme.of(context);
    final baseUrl = widget.api.baseUrl;
    final name = product['Name'] ?? product['name'] ?? '';
    final price = (product['price'] as num?)?.toDouble() ?? 0;
    final productId = (product['id'] as num?)?.toInt() ??
        (product['Id'] as num?)?.toInt() ??
        0;
    final isAvailable = product['IsAvailable'] ?? product['isAvailable'] ?? true;
    final desc = (product['Description'] ?? product['description'] ?? '').toString();
    final ratingAvg = product['ratingAvg'] ?? 0;
    final ratingCount = (product['ratingCount'] as num?)?.toInt() ?? 0;

    String imgUrl = '';
    final rawImgs = product['images'];
    if (rawImgs is List && rawImgs.isNotEmpty) {
      final first = rawImgs.first;
      final rawUrl =
          (first is Map ? first['url'] : first)?.toString() ?? '';
      imgUrl = _absUrl(baseUrl, rawUrl);
    }
    if (imgUrl.isEmpty) {
      final directUrl =
          (product['imageUrl'] ?? product['ImageUrl'] ?? '').toString();
      if (directUrl.isNotEmpty) imgUrl = _absUrl(baseUrl, directUrl);
    }

    return Container(
      margin: const EdgeInsets.only(bottom: 10, top: 4),
      decoration: BoxDecoration(
        border: Border.all(
            color: theme.colorScheme.primary.withOpacity(0.25), width: 1.5),
        borderRadius: BorderRadius.circular(16),
        color: theme.colorScheme.surface,
        boxShadow: [
          BoxShadow(
            color: Colors.black.withOpacity(0.05),
            blurRadius: 6,
            offset: const Offset(0, 2),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (imgUrl.isNotEmpty)
            ClipRRect(
              borderRadius:
                  const BorderRadius.vertical(top: Radius.circular(15)),
              child: Image.network(
                imgUrl,
                width: double.infinity,
                height: 140,
                fit: BoxFit.cover,
                errorBuilder: (_, __, ___) => Container(
                  height: 70,
                  color: theme.colorScheme.surfaceContainerHighest,
                  child: const Center(
                      child: Icon(Icons.fastfood, size: 36, color: Colors.grey)),
                ),
              ),
            ),
          Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Expanded(
                      child: Text(
                        name.toString(),
                        style: const TextStyle(
                            fontWeight: FontWeight.bold, fontSize: 15),
                      ),
                    ),
                    Container(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 10, vertical: 5),
                      decoration: BoxDecoration(
                        color: theme.colorScheme.primary,
                        borderRadius: BorderRadius.circular(20),
                      ),
                      child: Text(
                        '${price.toStringAsFixed(0)} ل.س',
                        style: const TextStyle(
                            color: Colors.white,
                            fontWeight: FontWeight.bold,
                            fontSize: 13),
                      ),
                    ),
                  ],
                ),
                if (desc.isNotEmpty) ...[
                  const SizedBox(height: 5),
                  Text(
                    desc.length > 100 ? '${desc.substring(0, 100)}...' : desc,
                    style: TextStyle(
                        fontSize: 12,
                        color: theme.colorScheme.onSurface.withOpacity(0.6)),
                  ),
                ],
                if (ratingCount > 0) ...[
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      const Icon(Icons.star_rounded,
                          color: Colors.amber, size: 16),
                      const SizedBox(width: 3),
                      Text('$ratingAvg',
                          style: const TextStyle(
                              fontSize: 12, fontWeight: FontWeight.bold)),
                      Text(' ($ratingCount تقييم)',
                          style: const TextStyle(
                              fontSize: 11, color: Colors.grey)),
                    ],
                  ),
                ],
                const SizedBox(height: 10),
                SizedBox(
                  width: double.infinity,
                  child: isAvailable == true
                      ? FilledButton.icon(
                          onPressed: () {
                            widget.state.addToCartBasic(
                              productId: productId,
                              name: name.toString(),
                              basePrice: price,
                            );
                            ScaffoldMessenger.of(context).showSnackBar(
                              SnackBar(
                                content:
                                    Text('✅ تمت إضافة $name للسلة'),
                                duration: const Duration(seconds: 2),
                                behavior: SnackBarBehavior.floating,
                                shape: RoundedRectangleBorder(
                                    borderRadius: BorderRadius.circular(10)),
                              ),
                            );
                          },
                          icon: const Icon(Icons.add_shopping_cart, size: 18),
                          label: const Text('أضف للسلة'),
                        )
                      : const OutlinedButton(
                          onPressed: null,
                          child: Text('غير متوفر حالياً',
                              style: TextStyle(color: Colors.grey)),
                        ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
