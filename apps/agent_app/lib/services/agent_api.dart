import 'dart:convert';
import 'package:http/http.dart' as http;

class AgentApi {
  AgentApi({required this.baseUrl});
  final String baseUrl;

  Uri _u(String path) => Uri.parse(baseUrl).resolve(path);

  // تحليل رسالة الخطأ من الاستجابة
  String _extractError(http.Response res) {
    try {
      final j = jsonDecode(res.body);
      if (j is Map) {
        return j['error']?.toString() ??
               j['message']?.toString() ??
               j['title']?.toString() ??
               'خطأ ${res.statusCode}';
      }
    } catch (_) {}
    if (res.body.isNotEmpty && res.body.length < 200) return res.body;
    return 'خطأ ${res.statusCode}';
  }

  T _decode<T>(http.Response res) {
    final body = res.body.trim();
    if (body.isEmpty) {
      if (T == Map<String, dynamic>) return <String, dynamic>{} as T;
      if (T == List<dynamic>) return <dynamic>[] as T;
    }
    try {
      return jsonDecode(body) as T;
    } catch (_) {
      if (T == Map<String, dynamic>) return <String, dynamic>{} as T;
      if (T == List<dynamic>) return <dynamic>[] as T;
      rethrow;
    }
  }

  Map<String, String> _headers(String token) => {
        'Content-Type': 'application/json',
        'X-AGENT-TOKEN': token,
      };

  Future<Map<String, dynamic>> login({
    required String phone,
    required String password,
  }) async {
    final res = await http.post(
      _u('/api/agent/login'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'phone': phone, 'pin': password, 'password': password}),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<Map<String, dynamic>>(res);
  }

  Future<Map<String, dynamic>> getProfile(String token) async {
    final res = await http.get(_u('/api/agent/profile'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<Map<String, dynamic>>(res);
  }

  Future<List<dynamic>> getMyProducts(String token) async {
    final res = await http.get(_u('/api/agent/products'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<List<dynamic>>(res);
  }

  Future<Map<String, dynamic>> createProduct(
    String token, {
    required String name,
    required String description,
    required double price,
    required int categoryId,
    bool isAvailable = true,
    String? imageUrl,
    int stockQuantity = 0,
    bool trackStock = false,
  }) async {
    final res = await http.post(
      _u('/api/agent/products'),
      headers: _headers(token),
      body: jsonEncode({
        'name': name,
        'description': description,
        'price': price,
        'categoryId': categoryId,
        'isAvailable': isAvailable,
        'imageUrl': imageUrl,
        'stockQuantity': stockQuantity,
        'trackStock': trackStock,
      }),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<Map<String, dynamic>>(res);
  }

  Future<Map<String, dynamic>> updateProduct(
    String token,
    int productId, {
    required String name,
    required String description,
    required double price,
    required int categoryId,
    required bool isAvailable,
    String? imageUrl,
    int stockQuantity = 0,
    bool trackStock = false,
  }) async {
    final res = await http.put(
      _u('/api/agent/products/$productId'),
      headers: _headers(token),
      body: jsonEncode({
        'name': name,
        'description': description,
        'price': price,
        'categoryId': categoryId,
        'isAvailable': isAvailable,
        'imageUrl': imageUrl,
        'stockQuantity': stockQuantity,
        'trackStock': trackStock,
      }),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<Map<String, dynamic>>(res);
  }

  Future<void> deleteProduct(String token, int productId) async {
    final res = await http.delete(
      _u('/api/agent/products/$productId'),
      headers: _headers(token),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
  }

  Future<void> toggleProductAvailability(String token, int productId, bool isAvailable) async {
    final res = await http.patch(
      _u('/api/agent/products/$productId/availability'),
      headers: _headers(token),
      body: jsonEncode({'isAvailable': isAvailable}),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
  }

  Future<List<dynamic>> getCategories(String token) async {
    final res = await http.get(_u('/api/agent/categories'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<List<dynamic>>(res);
  }

  Future<List<dynamic>> getChatThreads(String token) async {
    final res = await http.get(_u('/api/agent/chats'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<List<dynamic>>(res);
  }

  Future<Map<String, dynamic>> getChatThread(String token, int threadId) async {
    final res = await http.get(_u('/api/agent/chats/$threadId'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<Map<String, dynamic>>(res);
  }

  Future<void> sendMessage(String token, int threadId, String message) async {
    final res = await http.post(
      _u('/api/agent/chats/$threadId/messages'),
      headers: _headers(token),
      body: jsonEncode({'message': message}),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
  }

  Future<Map<String, dynamic>> publicSettings() async {
    final res = await http.get(_u('/api/public/settings'));
    if (res.statusCode >= 400) throw Exception('settings_failed');
    return _decode<Map<String, dynamic>>(res);
  }

  Future<Map<String, dynamic>> publicAppConfig() async {
    final res = await http.get(_u('/api/public/app-config'));
    if (res.statusCode >= 400) throw Exception('app_config_failed');
    return _decode<Map<String, dynamic>>(res);
  }

  String absoluteUrl(String? url) {
    final v = (url ?? '').trim();
    if (v.isEmpty) return '';
    if (v.startsWith('http://') || v.startsWith('https://')) return v;
    final origin = baseUrl.replaceAll(RegExp(r'/+$'), '');
    return origin + (v.startsWith('/') ? v : '/$v');
  }

  Future<List<dynamic>> getPendingOrders(String token) async {
    final res = await http.get(_u('/api/agent/orders/pending'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<List<dynamic>>(res);
  }

  Future<void> acceptOrder(String token, int orderId) async {
    final res = await http.post(
      _u('/api/agent/orders/$orderId/accept'),
      headers: _headers(token),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
  }

  Future<void> rejectOrder(String token, int orderId, String reason) async {
    final res = await http.post(
      _u('/api/agent/orders/$orderId/reject'),
      headers: _headers(token),
      body: jsonEncode({'reason': reason}),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
  }

  Future<List<dynamic>> getActiveOrders(String token) async {
    final res = await http.get(_u('/api/agent/orders/active'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<List<dynamic>>(res);
  }

  Future<List<dynamic>> getOrderHistory(String token, {int page = 1}) async {
    final res = await http.get(_u('/api/agent/orders/history?page=$page'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<List<dynamic>>(res);
  }

  Future<Map<String, dynamic>> getDailyReport(String token) async {
    final res = await http.get(_u('/api/agent/reports/daily'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<Map<String, dynamic>>(res);
  }

  Future<Map<String, dynamic>> getMonthlyReport(String token, {int? year, int? month}) async {
    final q = (year != null && month != null) ? '?year=$year&month=$month' : '';
    final res = await http.get(_u('/api/agent/reports/monthly$q'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<Map<String, dynamic>>(res);
  }

  Future<List<dynamic>> getTopProducts(String token, {int limit = 10}) async {
    final res = await http.get(_u('/api/agent/reports/top-products?limit=$limit'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<List<dynamic>>(res);
  }

  Future<String> uploadProductImage(String token, int productId, List<int> imageBytes, String filename) async {
    final uri = _u('/api/agent/products/$productId/image');
    final request = http.MultipartRequest('POST', uri);
    request.headers.addAll(_headers(token));
    request.files.add(http.MultipartFile.fromBytes('file', imageBytes, filename: filename));
    final streamed = await request.send();
    final res = await http.Response.fromStream(streamed);
    if (res.statusCode >= 400) throw Exception('فشل رفع الصورة: ${_extractError(res)}');
    final j = jsonDecode(res.body) as Map<String, dynamic>;
    return j['url']?.toString() ?? '';
  }

  Future<Map<String, dynamic>> getOrderDetail(String token, int orderId) async {
    final res = await http.get(_u('/api/agent/orders/$orderId'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<Map<String, dynamic>>(res);
  }

  Future<void> assignDriver(String token, int orderId, int driverId) async {
    final res = await http.post(
      _u('/api/agent/orders/$orderId/assign-driver'),
      headers: _headers(token),
      body: jsonEncode({'driverId': driverId}),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
  }

  Future<void> setEta(String token, int orderId, {int? processingEta, int? deliveryEta}) async {
    final res = await http.post(
      _u('/api/agent/orders/$orderId/set-eta'),
      headers: _headers(token),
      body: jsonEncode({
        if (processingEta != null) 'processingEtaMinutes': processingEta,
        if (deliveryEta != null) 'deliveryEtaMinutes': deliveryEta,
      }),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
  }

  Future<void> updateOrderStatus(String token, int orderId, int status, {String? comment}) async {
    final res = await http.post(
      _u('/api/agent/orders/$orderId/status'),
      headers: _headers(token),
      body: jsonEncode({'status': status, if (comment != null) 'comment': comment}),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
  }

  Future<Map<String, dynamic>> getMyProductRatings(String token, {int page = 1, int limit = 50}) async {
    final res = await http.get(
      _u('/api/agent/my-product-ratings?page=$page&limit=$limit'),
      headers: _headers(token),
    );
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<Map<String, dynamic>>(res);
  }

  Future<List<dynamic>> getAvailableDrivers(String token) async {
    final res = await http.get(_u('/api/agent/drivers'), headers: _headers(token));
    if (res.statusCode >= 400) throw Exception(_extractError(res));
    return _decode<List<dynamic>>(res);
  }
}
