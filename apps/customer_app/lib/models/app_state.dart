import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

class CartItem {
  CartItem({
    required this.key,
    required this.productId,
    required this.name,
    required this.unitPrice,
    required this.qty,
    required this.optionsSnapshot,
    required this.optionsLabel,
  });
  final String key;

  final int productId;
  final String name;
  final double unitPrice;
  int qty;
  final String optionsSnapshot; 
  final String optionsLabel; 

  bool get isOffer => productId < 0;
  int get offerId => isOffer ? (-productId) : 0;

  double get total => unitPrice * qty;
}

class AppState extends ChangeNotifier {
  SharedPreferences? _prefs;
  bool isDarkMode = false;

  static const _keyRatingDismissed = 'rating_dismissed_order_ids';

  Set<int> _ratingDismissedOrderIds = {};

  bool isRatingDismissed(int orderId) => _ratingDismissedOrderIds.contains(orderId);

  Future<void> markRatingDismissed(int orderId) async {
    _ratingDismissedOrderIds.add(orderId);
    try {
      await _prefs?.setString(_keyRatingDismissed, jsonEncode(_ratingDismissedOrderIds.toList()));
    } catch (_) {}
    notifyListeners();
  }

  Future<void> init(SharedPreferences prefs) async {
    _prefs = prefs;
    isDarkMode = prefs.getBool('theme_dark') ?? false;
    try {
      final raw = prefs.getString(_keyRatingDismissed);
      if (raw != null && raw.isNotEmpty) {
        final list = jsonDecode(raw);
        if (list is List) {
          _ratingDismissedOrderIds = list.map((e) => (e is num) ? e.toInt() : int.tryParse(e.toString()) ?? 0).where((e) => e > 0).toSet();
        }
      }
    } catch (_) {}
    notifyListeners();
  }

  Future<void> setDarkMode(bool value) async {
    isDarkMode = value;
    await _prefs?.setBool('theme_dark', value);
    notifyListeners();
  }

  Future<void> toggleTheme() => setDarkMode(!isDarkMode);
  
  String storeName = '';
  String? logoUrl;
  String? customerSplashUrl;
  List<String> splashBackgrounds = const [];
  
  List<String> homeBanners = const [];
  
  String primaryColorHex = '#5C4A8E';
  String secondaryColorHex = '#FFC107';
  String workHours = '';
  double minOrderAmount = 0;
  double deliveryFeeValue = 0;
  double deliveryFeePerKmValue = 0;
  int deliveryFeeType = 0; // 0=Fixed, 1=ByZone
  String supportPhone = '';
  String supportWhatsApp = '';
  String? facebookUrl;
  String? instagramUrl;
  String? telegramUrl;
  bool isAcceptingOrders = true;
  String closedMessage = 'المتجر مغلق حالياً';
  String? closedScreenImageUrl;
  double storeLat = 0;
  double storeLng = 0;

  List<dynamic> onboardingSlides = const [];

  List<dynamic> notifications = const [];
  int unreadNotifications = 0;

  List<Map<String, dynamic>> complaintThreads = const [];
  int unreadComplaints = 0;
  int? openComplaintThreadId;
  Map<String, dynamic>? lastComplaintMessage;
  int complaintMessageSeq = 0;

  final List<String> _seenChatKeysOrder = [];
  final Set<String> _seenChatKeys = {};

  bool _markChatSeen(String key) {
    if (_seenChatKeys.contains(key)) return false;
    _seenChatKeys.add(key);
    _seenChatKeysOrder.add(key);
    
    if (_seenChatKeysOrder.length > 250) {
      final removed = _seenChatKeysOrder.removeAt(0);
      _seenChatKeys.remove(removed);
    }
    return true;
  }

  void setComplaintThreads(List<dynamic> list) {
    complaintThreads = list
        .whereType<Map>()
        .map((e) => Map<String, dynamic>.from(e))
        .toList();
    _recalcUnreadComplaints();
    notifyListeners();
  }

  void openComplaintThread(int threadId) {
    openComplaintThreadId = threadId;
    
    final idx = complaintThreads.indexWhere((t) => t['id'] == threadId);
    if (idx >= 0) {
      complaintThreads[idx] = {
        ...complaintThreads[idx],
        'unreadCount': 0,
      };
    }
    _recalcUnreadComplaints();
    notifyListeners();
  }

  void closeComplaintThread() {
    openComplaintThreadId = null;
    _recalcUnreadComplaints();
    notifyListeners();
  }

  void applyComplaintMessage(Map<String, dynamic> payload) {
    
    final threadIdRaw = payload['threadId'];
    final fromAdminRaw = payload['fromAdmin'] == true;
    final messageRaw = (payload['message'] ?? '').toString();
    final createdAtRaw = (payload['createdAtUtc'] ?? payload['createdAt'] ?? '').toString();
    final idRaw = payload['id'] ?? payload['messageId'];
    final key = (idRaw is int || idRaw is num)
        ? 'id:${(idRaw as num).toInt()}'
        : 't:$threadIdRaw|a:$fromAdminRaw|m:${messageRaw.hashCode}|c:$createdAtRaw';
    if (!_markChatSeen(key)) {
      return;
    }

    lastComplaintMessage = payload;
    complaintMessageSeq++;

    final threadId = threadIdRaw;
    final fromAdmin = fromAdminRaw;
    final message = messageRaw;
    final createdAt = createdAtRaw;

    if (threadId is! int) {
      notifyListeners();
      return;
    }

    final previewPrefix = fromAdmin ? 'الإدارة: ' : 'أنت: ';
    final preview = (previewPrefix + message);
    final shortPreview = preview.length > 60 ? (preview.substring(0, 60) + '…') : preview;

    final idx = complaintThreads.indexWhere((t) => t['id'] == threadId);
    Map<String, dynamic> thread;
    if (idx >= 0) {
      thread = Map<String, dynamic>.from(complaintThreads[idx]);
    } else {
      thread = {
        'id': threadId,
        'title': 'الدعم',
        'orderId': null,
        'unreadCount': 0,
        'lastMessagePreview': '',
        'lastMessageAtUtc': null,
      };
    }

    int unread = (thread['unreadCount'] as num?)?.toInt() ?? 0;
    
    if (fromAdmin && openComplaintThreadId != threadId) unread += 1;
    if (openComplaintThreadId == threadId) unread = 0;

    thread['unreadCount'] = unread;
    thread['lastMessagePreview'] = shortPreview;
    thread['lastMessageAtUtc'] = createdAt;
    thread['updatedAtUtc'] = createdAt;

    if (idx >= 0) {
      complaintThreads[idx] = thread;
    } else {
      complaintThreads = [thread, ...complaintThreads];
    }

    complaintThreads = [...complaintThreads]
      ..sort((a, b) {
        final aa = (a['lastMessageAtUtc'] ?? a['updatedAtUtc'] ?? '').toString();
        final bb = (b['lastMessageAtUtc'] ?? b['updatedAtUtc'] ?? '').toString();
        return bb.compareTo(aa);
      });

    _recalcUnreadComplaints();
    notifyListeners();
  }

  void _recalcUnreadComplaints() {
    unreadComplaints = complaintThreads.fold<int>(0, (sum, t) {
      final u = (t['unreadCount'] as num?)?.toInt() ?? 0;
      return sum + (u < 0 ? 0 : u);
    });
  }

  void setOpenComplaintThread(int? threadId) {
    openComplaintThreadId = threadId;
    if (threadId != null) {
      final idx = complaintThreads.indexWhere((t) => t['id'] == threadId);
      if (idx >= 0) {
        final t = Map<String, dynamic>.from(complaintThreads[idx]);
        t['unreadCount'] = 0;
        complaintThreads[idx] = t;
        complaintThreads = [...complaintThreads];
      }
    }
    _recalcUnreadComplaints();
    notifyListeners();
  }

  int? editingOrderId;
  DateTime? editingUntilUtc;
  String? editingNotes;

  bool get isEditingOrder => editingOrderId != null && editingUntilUtc != null;

  Duration? get editingRemaining {
    if (editingUntilUtc == null) return null;
    final d = editingUntilUtc!.difference(DateTime.now().toUtc());
    return d.isNegative ? Duration.zero : d;
  }

  void beginEditOrder({
    required int orderId,
    required DateTime untilUtc,
    required List<CartItem> items,
    String? notes,
  }) {
    editingOrderId = orderId;
    editingUntilUtc = untilUtc;
    editingNotes = notes;
    cart = List<CartItem>.from(items);
    notifyListeners();
  }

  void endEditOrder() {
    editingOrderId = null;
    editingUntilUtc = null;
    editingNotes = null;
    notifyListeners();
  }

  List<dynamic> menuCategories = const [];
  List<dynamic> activeOffers = const [];

  void setMenu(Map<String, dynamic> menu) {
    menuCategories = (menu['categories'] is List) ? (menu['categories'] as List) : const [];
    activeOffers = (menu['offers'] is List) ? (menu['offers'] as List) : const [];
    notifyListeners();
  }

  final Map<int, Map<String, dynamic>> orderEtaCache = {};

  void upsertOrderEta(Map<String, dynamic> payload) {
    final oid = payload['orderId'];
    if (oid is int) {
      orderEtaCache[oid] = Map<String, dynamic>.from(payload);
      notifyListeners();
    }
  }

  void setConfig(Map<String, dynamic> s) {
    storeName = (s['storeName'] ?? storeName).toString();
    logoUrl = s['logoUrl']?.toString();
    final splashUrls = (s['splashUrls'] is Map) ? (s['splashUrls'] as Map) : null;
    customerSplashUrl = splashUrls?['customer']?.toString() ?? s['customerSplashUrl']?.toString();
    splashBackgrounds = (s['splashBackgrounds'] is List) ? (s['splashBackgrounds'] as List).map((e) => e.toString()).toList() : splashBackgrounds;
    primaryColorHex = (s['primaryColor'] ?? s['primaryColorHex'] ?? primaryColorHex).toString();
    secondaryColorHex = (s['secondaryColor'] ?? s['secondaryColorHex'] ?? secondaryColorHex).toString();
    workHours = (s['openHours'] ?? s['workHours'] ?? workHours).toString();
    minOrderAmount = (s['minOrder'] is num) ? (s['minOrder'] as num).toDouble() : ((s['minOrderAmount'] is num) ? (s['minOrderAmount'] as num).toDouble() : minOrderAmount);
    deliveryFeeValue = (s['deliveryFee'] is num) ? (s['deliveryFee'] as num).toDouble() : ((s['deliveryFeeValue'] is num) ? (s['deliveryFeeValue'] as num).toDouble() : deliveryFeeValue);
    deliveryFeePerKmValue = (s['deliveryFeePerKm'] is num) ? (s['deliveryFeePerKm'] as num).toDouble() : deliveryFeePerKmValue;
    deliveryFeeType = (s['deliveryFeeType'] is num) ? (s['deliveryFeeType'] as num).toInt() : deliveryFeeType;
    supportPhone = (s['supportPhone'] ?? supportPhone).toString();
    supportWhatsApp = (s['whatsapp'] ?? s['supportWhatsApp'] ?? supportWhatsApp).toString();
    final social = (s['socialLinks'] is Map) ? (s['socialLinks'] as Map) : null;
    facebookUrl = social?['facebook']?.toString() ?? facebookUrl;
    instagramUrl = social?['instagram']?.toString() ?? instagramUrl;
    telegramUrl = social?['telegram']?.toString() ?? telegramUrl;
    isAcceptingOrders = !(s['isManuallyClosed'] == true) && !((s['acceptOrders'] == false) || (s['isAcceptingOrders'] == false));
    closedMessage = (s['texts'] is Map && (s['texts'] as Map)['closedMessage'] != null)
        ? (s['texts'] as Map)['closedMessage'].toString()
        : (s['closedMessage']?.toString() ?? closedMessage);
    closedScreenImageUrl = (s['closedScreenImageUrl'] ?? s['closedBackgroundUrl'])?.toString() ?? closedScreenImageUrl;
    storeLat = (s['storeLat'] is num) ? (s['storeLat'] as num).toDouble() : storeLat;
    storeLng = (s['storeLng'] is num) ? (s['storeLng'] as num).toDouble() : storeLng;
    onboardingSlides = (s['onboarding'] is List) ? (s['onboarding'] as List) : onboardingSlides;
    if (s['homeBanners'] is List) {
      homeBanners = (s['homeBanners'] as List).map((e) => e.toString()).where((e) => e.trim().isNotEmpty).toList();
    }
    notifyListeners();
  }

  void setNotifications(List<dynamic> list) {
    notifications = list;
    unreadNotifications = list.where((n) => (n is Map) && (n['isRead'] != true)).length;
    notifyListeners();
  }

  void pushNotification(dynamic n) {
    notifications = [n, ...notifications];
    unreadNotifications = notifications.where((x) => (x is Map) && (x['isRead'] != true)).length;
    notifyListeners();
  }
  int? customerId;
  String? customerName;
  String? customerPhone;
  double? defaultLat;
  double? defaultLng;
  String? defaultAddress;

  List<Map<String, dynamic>> savedAddresses = const [];
  int? selectedAddressId;

  void setSavedAddresses(List<dynamic> list) {
    savedAddresses = list.whereType<Map>().map((e) => Map<String, dynamic>.from(e)).toList();
    
    if (selectedAddressId == null) {
      final def = savedAddresses.firstWhere((a) => a['isDefault']==true, orElse: () => savedAddresses.isNotEmpty ? savedAddresses.first : <String,dynamic>{});
      if (def.isNotEmpty) selectedAddressId = (def['id'] as num?)?.toInt();
    }
    notifyListeners();
  }

  void selectAddress(Map<String, dynamic> a) {
    selectedAddressId = (a['id'] as num?)?.toInt();
    final lat = (a['latitude'] as num?)?.toDouble();
    final lng = (a['longitude'] as num?)?.toDouble();
    final text = (a['addressText'] ?? a['address'] ?? '').toString();
    if (lat != null && lng != null) {
      setDeliveryLocation(lat: lat, lng: lng, address: text);
    } else {
      notifyListeners();
    }
  }

  void setDeliveryLocation({required double lat, required double lng, String? address}) {
    defaultLat = lat;
    defaultLng = lng;
    defaultAddress = address;
    notifyListeners();
  }

  void setDeliveryFee(double value) {
    deliveryFeeValue = value;
    notifyListeners();
  }

  void clearDeliveryForNextOrder() {
    defaultLat = null;
    defaultLng = null;
    defaultAddress = null;
    deliveryFeeValue = 0;
    notifyListeners();
  }

  List<CartItem> cart = [];

  void setCustomer({required int id, required String name, required String phone, required double lat, required double lng, String? address}) {
    customerId = id;
    customerName = name;
    customerPhone = phone;
    defaultLat = lat;
    defaultLng = lng;
    defaultAddress = address;
    notifyListeners();
  }

  void clearCustomer() {
    customerId = null;
    customerName = null;
    customerPhone = null;
    defaultLat = null;
    defaultLng = null;
    defaultAddress = null;
    cart.clear();
    notifications.clear();
    unreadNotifications = 0;
    complaintThreads = const [];
    unreadComplaints = 0;
    notifyListeners();
  }

  void addToCartBasic({required int productId, required String name, required double basePrice}) {
    addToCartWithOptions(
      productId: productId,
      name: name,
      unitPrice: basePrice,
      optionsSnapshot: '{"variantId":null,"addonIds":[],"note":null}',
      optionsLabel: 'بدون إضافات',
    );
  }

  void addToCartOffer({required int offerId, required String name, required double price}) {
    
    final pid = -offerId;
    addToCartWithOptions(
      productId: pid,
      name: name,
      unitPrice: price,
      optionsSnapshot: '{"type":"offer","offerId":$offerId}',
      optionsLabel: 'عرض',
    );
  }

  void addToCartWithOptions({
    required int productId,
    required String name,
    required double unitPrice,
    required String optionsSnapshot,
    required String optionsLabel,
  }) {
    final key = '$productId|$optionsSnapshot';
    final existing = cart.where((c) => c.key == key).toList();
    if (existing.isNotEmpty) {
      existing.first.qty += 1;
    } else {
      cart.add(CartItem(
        key: key,
        productId: productId,
        name: name,
        unitPrice: unitPrice,
        qty: 1,
        optionsSnapshot: optionsSnapshot,
        optionsLabel: optionsLabel,
      ));
    }
    notifyListeners();
  }

  void removeFromCart(String key) {
    cart.removeWhere((c) => c.key == key);
    notifyListeners();
  }

  void setQty(String key, int qty) {
    final it = cart.firstWhere((c) => c.key == key);
    it.qty = qty.clamp(1, 999);
    notifyListeners();
  }

  double get cartSubtotal => cart.fold(0, (s, it) => s + it.total);

  void clearCart() {
    cart.clear();
    notifyListeners();
  }

  Set<int> _favoriteProductIds = {};
  int _favoriteVersion = 0;

  int get favoriteVersion => _favoriteVersion;
  bool isFavorite(int productId) => _favoriteProductIds.contains(productId);

  void setFavorites(List<dynamic> favs) {
    _favoriteProductIds = favs
        .whereType<Map>()
        .map((f) => (f['productId'] as num?)?.toInt() ?? 0)
        .where((id) => id > 0)
        .toSet();
    _favoriteVersion++;
    notifyListeners();
  }

  void toggleFavoriteLocal(int productId) {
    if (_favoriteProductIds.contains(productId)) {
      _favoriteProductIds.remove(productId);
    } else {
      _favoriteProductIds.add(productId);
    }
    _favoriteVersion++;
    notifyListeners();
  }

  List<int> get favoriteProductIds => _favoriteProductIds.toList();

  Map<String, dynamic>? _lastAgentChatMessage;
  int _agentChatMessageSeq = 0;

  Map<String, dynamic>? get lastAgentChatMessage => _lastAgentChatMessage;
  int get agentChatMessageSeq => _agentChatMessageSeq;

  int? _openAgentChatThreadId;
  int? get openAgentChatThreadId => _openAgentChatThreadId;
  
  void setOpenAgentChatThread(int? threadId) {
    _openAgentChatThreadId = threadId;
  }

  void onRealtimeAgentChatMessage(Map<String, dynamic> payload) {
    _lastAgentChatMessage = payload;
    _agentChatMessageSeq++;
    notifyListeners();
  }
}
