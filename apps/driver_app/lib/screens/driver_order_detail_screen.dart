import 'dart:async';
import 'dart:convert';
import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:geolocator/geolocator.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:http/http.dart' as http;
import 'package:latlong2/latlong.dart';
import 'package:url_launcher/url_launcher.dart';

import '../services/api.dart';
import '../theme/driver_theme.dart';

class DriverOrderDetailScreen extends StatefulWidget {
  const DriverOrderDetailScreen({
    super.key,
    required this.order,
    required this.api,
    required this.token,
    this.storeLat,
    this.storeLng,
  });

  final Map<String, dynamic> order;
  final DriverApi api;
  final String token;
  final double? storeLat;
  final double? storeLng;

  static const int _stReadyForPickup = 3;
  static const int _stWithDriver     = 4;
  static const int _stDelivered      = 5;
  static const int _stCancelled      = 6;
  static const int _stAccepted       = 7;

  @override
  State<DriverOrderDetailScreen> createState() => _DriverOrderDetailScreenState();
}

class _DriverOrderDetailScreenState extends State<DriverOrderDetailScreen>
    with TickerProviderStateMixin {

  LatLng? _driverPos;
  LatLng? _animatedPos;
  bool _busyDeliver = false;
  bool _busyCancel  = false;
  Timer? _posTimer;
  Timer? _animTimer;

  List<LatLng>? _roadRoutePoints;
  String?       _roadRouteKey;
  DateTime?     _lastRoadRouteFetch;
  bool          _roadRouteLoading = false;

  bool          _demoMode         = false;
  LatLng?       _demoPos;
  List<LatLng>  _demoRoute        = [];
  int           _demoRouteIdx     = 0;
  Timer?        _demoTimer;
  bool          _demoRouteLoading = false;
  bool          _demoArrived      = false; 

  Map<String, dynamic>? _orderData;
  final MapController   _mapController = MapController();

  Map<String, dynamic> get _order => _orderData ?? widget.order;

  bool get _isWithDriver =>
      _statusCode == DriverOrderDetailScreen._stWithDriver;

  int get _statusCode =>
      (_order['currentStatus'] as num?)?.toInt() ??
      int.tryParse((_order['currentStatus'] ?? '').toString()) ?? -1;

  String _statusLabel(int s) {
    switch (s) {
      case 0: return 'جديد';
      case 1: return 'تم التأكيد';
      case 2: return 'قيد المعالجة';
      case DriverOrderDetailScreen._stReadyForPickup: return 'جاهز للاستلام';
      case DriverOrderDetailScreen._stWithDriver:     return 'مع السائق';
      case DriverOrderDetailScreen._stDelivered:      return 'تم التسليم';
      case DriverOrderDetailScreen._stCancelled:      return 'ملغى';
      case DriverOrderDetailScreen._stAccepted:       return 'تم القبول';
      default: return '$s';
    }
  }

  static const String _osrmBase = 'https://router.project-osrm.org';

  Future<List<LatLng>?> _fetchRoutePoints(LatLng start, LatLng end) async {
    final coords = '${start.longitude},${start.latitude};'
        '${end.longitude},${end.latitude}';
    final uri = Uri.parse(
        '$_osrmBase/route/v1/driving/$coords?overview=full&geometries=geojson');
    try {
      final res = await http.get(uri).timeout(const Duration(seconds: 8));
      if (res.statusCode != 200) return null;
      final data = jsonDecode(res.body) as Map<String, dynamic>?;
      if (data == null || data['code'] != 'Ok') return null;
      final coordsList =
          data['routes']?[0]?['geometry']?['coordinates'] as List<dynamic>?;
      if (coordsList == null || coordsList.length < 2) return null;
      return coordsList.map((e) {
        final pair = e as List<dynamic>;
        return LatLng(
            (pair[1] as num).toDouble(), (pair[0] as num).toDouble());
      }).toList();
    } catch (_) {
      return null;
    }
  }

  Future<void> _fetchRoadRoute(LatLng start, LatLng end) async {
    if (mounted) setState(() => _roadRouteLoading = true);
    try {
      final pts = await _fetchRoutePoints(start, end);
      if (!mounted) return;
      if (pts != null && pts.length >= 2) {
        setState(() {
          _roadRoutePoints = pts;
          _roadRouteLoading = false;
        });
      } else {
        setState(() => _roadRouteLoading = false);
      }
    } catch (_) {
      if (mounted) setState(() { _roadRoutePoints = null; _roadRouteLoading = false; });
    }
  }

  Future<LatLng?> _getDriverPosition() async {
    try {
      final enabled = await Geolocator.isLocationServiceEnabled();
      if (!enabled) return null;
      var perm = await Geolocator.checkPermission();
      if (perm == LocationPermission.denied)
        perm = await Geolocator.requestPermission();
      if (perm == LocationPermission.deniedForever ||
          perm == LocationPermission.denied) return null;
      final pos = await Geolocator.getCurrentPosition(
          desiredAccuracy: LocationAccuracy.low);
      return LatLng(pos.latitude, pos.longitude);
    } catch (_) {
      return null;
    }
  }

  void _stepAnimatedPos() {
    if (!mounted || !_isWithDriver || _demoMode) return;
    final target  = _deliveryPoint();
    final current = _animatedPos ?? _driverPos;
    if (current == null) return;
    const double stepFraction = 0.12;
    final newLat =
        current.latitude + (target.latitude - current.latitude) * stepFraction;
    final newLng =
        current.longitude + (target.longitude - current.longitude) * stepFraction;
    setState(() => _animatedPos = LatLng(newLat, newLng));
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      _mapController.move(
          LatLng(newLat, newLng), _mapController.camera.zoom);
    });
  }

  Future<void> _loadDriverPos() async {
    if (_demoMode) return;
    final p = await _getDriverPosition();
    if (!mounted) return;
    if (p != null) {
      setState(() {
        _driverPos   = p;
        _animatedPos ??= p;
      });
    }
    if (_isWithDriver && _driverPos != null) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        _mapController.move(_center(), _mapController.camera.zoom);
      });
    }
  }

  Future<void> _toggleDemoMode() async {
    if (_demoMode) {
      
      _demoTimer?.cancel();
      setState(() {
        _demoMode         = false;
        _demoPos          = null;
        _demoRoute        = [];
        _demoRouteIdx     = 0;
        _demoArrived      = false;
        _roadRoutePoints  = null;
      });
      return;
    }

    if (_statusCode == DriverOrderDetailScreen._stAccepted ||
        _statusCode == DriverOrderDetailScreen._stReadyForPickup) {
      await _markPickedUpSilent();
    }

    final delivery = _deliveryPoint();

    final realPos = await _getDriverPosition();
    final LatLng startPos = realPos ??
        LatLng(delivery.latitude + 0.013, delivery.longitude + 0.011);

    setState(() {
      _demoMode         = true;
      _demoArrived      = false;
      _demoPos          = startPos;
      _demoRoute        = [];
      _demoRouteIdx     = 0;
      _demoRouteLoading = true;
      _roadRoutePoints  = null;
    });

    final pts = await _fetchRoutePoints(startPos, delivery);
    if (!mounted) return;

    final List<LatLng> route = (pts != null && pts.length >= 2)
        ? pts
        : _interpolate(startPos, delivery, 80);

    setState(() {
      _demoRoute        = route;
      _demoRouteIdx     = 0;
      _demoPos          = route.first;
      _demoRouteLoading = false;
      _roadRoutePoints  = route;
    });

    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      try {
        _mapController.fitCamera(
          CameraFit.bounds(
            bounds: LatLngBounds.fromPoints(route),
            padding: const EdgeInsets.all(55),
          ),
        );
      } catch (_) {}
    });

    _demoTimer?.cancel();
    _demoTimer = Timer.periodic(
        const Duration(milliseconds: 700), (_) => _demoStep());
  }

  void _demoStep() {
    if (!mounted || !_demoMode || _demoArrived || _demoRoute.isEmpty) return;

    final nextIdx = math.min(_demoRouteIdx + 3, _demoRoute.length - 1);
    final nextPt  = _demoRoute[nextIdx];

    double heading = 0;
    if (_demoRouteIdx < _demoRoute.length - 1) {
      final prev = _demoRoute[_demoRouteIdx];
      final dLon = nextPt.longitude - prev.longitude;
      final dLat = nextPt.latitude  - prev.latitude;
      heading = (math.atan2(dLon, dLat) * 180 / math.pi + 360) % 360;
    }

    setState(() {
      _demoRouteIdx = nextIdx;
      _demoPos      = nextPt;
    });

    widget.api.sendLocation(
      widget.token,
      lat: nextPt.latitude,
      lng: nextPt.longitude,
      speedMps: 5.5,         
      headingDeg: heading,
      accuracyMeters: 8.0,
    ).catchError((_) {});    

    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      try {
        _mapController.move(nextPt, _mapController.camera.zoom);
      } catch (_) {}
    });

    if (nextIdx >= _demoRoute.length - 1) {
      _demoTimer?.cancel();
      setState(() => _demoArrived = true);
    }
  }

  List<LatLng> _interpolate(LatLng from, LatLng to, int steps) {
    return List.generate(
      steps + 1,
      (i) => LatLng(
        from.latitude  + (to.latitude  - from.latitude)  * (i / steps),
        from.longitude + (to.longitude - from.longitude) * (i / steps),
      ),
    );
  }

  Future<void> _markPickedUpSilent() async {
    final id = (_order['id'] as num?)?.toInt();
    if (id == null) return;
    try {
      await widget.api.updateOrderStatus(
          widget.token, id, DriverOrderDetailScreen._stWithDriver);
      if (!mounted) return;
      _orderData ??= Map<String, dynamic>.from(widget.order);
      _orderData!['currentStatus'] = DriverOrderDetailScreen._stWithDriver;
      if (mounted) setState(() {});
    } catch (_) {}
  }

  Future<void> _demoMarkDelivered() async {
    final id = (_order['id'] as num?)?.toInt();
    if (id == null) return;
    setState(() => _busyDeliver = true);
    try {
      await widget.api.updateOrderStatus(
          widget.token, id, DriverOrderDetailScreen._stDelivered);
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('✅ تم تسجيل التسليم في النظام')));
      Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted)
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('فشل: $e')));
    } finally {
      if (mounted) setState(() => _busyDeliver = false);
    }
  }

  Future<void> _markPickedUp() async {
    final id = (_order['id'] as num?)?.toInt();
    if (id == null) return;
    try {
      await widget.api.updateOrderStatus(
          widget.token, id, DriverOrderDetailScreen._stWithDriver);
      if (!mounted) return;
      _orderData ??= Map<String, dynamic>.from(widget.order);
      _orderData!['currentStatus'] = DriverOrderDetailScreen._stWithDriver;
      setState(() {});
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('تم بدء التوصيل')));
    } catch (e) {
      if (mounted)
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('فشل: $e')));
    }
  }

  static double _haversineKm(
      double lat1, double lon1, double lat2, double lon2) {
    const R    = 6371.0;
    final dLat = (lat2 - lat1) * math.pi / 180.0;
    final dLon = (lon2 - lon1) * math.pi / 180.0;
    final a = math.sin(dLat / 2) * math.sin(dLat / 2) +
        math.cos(lat1 * math.pi / 180.0) *
            math.cos(lat2 * math.pi / 180.0) *
            math.sin(dLon / 2) *
            math.sin(dLon / 2);
    return R * 2 * math.atan2(math.sqrt(a), math.sqrt(1 - a));
  }

  Future<void> _markDelivered() async {
    final id  = (_order['id'] as num?)?.toInt();
    if (id == null) return;
    setState(() => _busyDeliver = true);
    try {
      await widget.api.updateOrderStatus(
          widget.token, id, DriverOrderDetailScreen._stDelivered);
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('تم تسجيل التسليم')));
      Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted)
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('فشل: $e')));
    } finally {
      if (mounted) setState(() => _busyDeliver = false);
    }
  }

  Future<void> _cancelOrder() async {
    final id = (_order['id'] as num?)?.toInt();
    if (id == null) return;
    setState(() => _busyCancel = true);
    try {
      await widget.api.cancelOrder(widget.token, orderId: id);
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('تم إلغاء الطلب')));
      Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted)
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('فشل: $e')));
    } finally {
      if (mounted) setState(() => _busyCancel = false);
    }
  }

  Future<void> _callPhone(String phone) async {
    final cleaned = phone.replaceAll(' ', '');
    if (cleaned.isEmpty) return;
    await launchUrl(Uri.parse('tel:$cleaned'));
  }

  Future<void> _openInGoogleMaps() async {
    final p = _deliveryPoint();
    final Uri url;
    if (_demoMode && _demoPos != null) {
      url = Uri.parse(
        'https://www.google.com/maps/dir/'
        '${_demoPos!.latitude},${_demoPos!.longitude}/'
        '${p.latitude},${p.longitude}',
      );
    } else {
      url = Uri.parse(
          'https://www.google.com/maps?q=${p.latitude},${p.longitude}');
    }
    try {
      if (await canLaunchUrl(url)) {
        await launchUrl(url, mode: LaunchMode.externalApplication);
      } else if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('تعذر فتح خرائط جوجل')));
      }
    } catch (e) {
      if (mounted)
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('خطأ: $e')));
    }
  }

  LatLng _deliveryPoint() {
    final lat = (_order['deliveryLat'] as num?)?.toDouble();
    final lng = (_order['deliveryLng'] as num?)?.toDouble();
    if (lat != null && lng != null && (lat != 0 || lng != 0))
      return LatLng(lat, lng);
    return const LatLng(33.5138, 36.2765);
  }

  LatLng _center() {
    final delivery = _deliveryPoint();
    final cur = _demoMode ? _demoPos : (_animatedPos ?? _driverPos);
    if (cur != null) {
      final bounds = LatLngBounds.fromPoints([cur, delivery]);
      return LatLng(
        (bounds.south + bounds.north) / 2,
        (bounds.west  + bounds.east)  / 2,
      );
    }
    return delivery;
  }

  @override
  void initState() {
    super.initState();
    _orderData = Map<String, dynamic>.from(widget.order);
    _loadDriverPos();
    _posTimer  = Timer.periodic(const Duration(seconds: 5),  (_) => _loadDriverPos());
    _animTimer = Timer.periodic(const Duration(seconds: 2),  (_) => _stepAnimatedPos());
  }

  @override
  void didUpdateWidget(covariant DriverOrderDetailScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.order['id'] != oldWidget.order['id']) {
      _orderData       = Map<String, dynamic>.from(widget.order);
      _roadRouteKey    = null;
      _roadRoutePoints = null;
      _animatedPos     = null;
      if (_demoMode) {
        _demoTimer?.cancel();
        _demoMode    = false;
        _demoPos     = null;
        _demoRoute   = [];
        _demoArrived = false;
      }
    }
  }

  @override
  void dispose() {
    _posTimer?.cancel();
    _animTimer?.cancel();
    _demoTimer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final id      = _order['id']?.toString() ?? '';
    final name    = (_order['customerName']    ?? '').toString();
    final phone   = (_order['customerPhone']   ?? '').toString();
    final address = (_order['deliveryAddress'] ?? '').toString();
    final total   = (_order['total']           ?? '').toString();
    final status  = _statusLabel(_statusCode);
    final delivery = _deliveryPoint();
    final rLat    = widget.storeLat;
    final rLng    = widget.storeLng;
    final cs      = Theme.of(context).colorScheme;

    final LatLng? displayPos =
        _demoMode ? _demoPos : (_animatedPos ?? _driverPos);

    List<LatLng> routePoints;
    if (_demoMode && _demoRoute.isNotEmpty) {
      
      routePoints = _demoRoute.sublist(_demoRouteIdx);
    } else if (_isWithDriver && displayPos != null) {
      routePoints = [displayPos, delivery];
    } else if (!_isWithDriver &&
        rLat != null && rLng != null && (rLat != 0 || rLng != 0)) {
      routePoints = [LatLng(rLat, rLng), delivery];
    } else {
      routePoints = [];
    }

    if (!_demoMode && routePoints.length >= 2) {
      final start = routePoints.first;
      final end   = routePoints.last;
      final key   = '${start.latitude.toStringAsFixed(5)},'
          '${start.longitude.toStringAsFixed(5)},'
          '${end.latitude.toStringAsFixed(5)},'
          '${end.longitude.toStringAsFixed(5)}';
      final throttleSec = _isWithDriver ? 5 : 15;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        if (key == _roadRouteKey) return;
        final throttled = _lastRoadRouteFetch != null &&
            DateTime.now().difference(_lastRoadRouteFetch!) <
                Duration(seconds: throttleSec);
        if (throttled) return;
        _roadRouteKey       = key;
        _lastRoadRouteFetch = DateTime.now();
        setState(() => _roadRoutePoints = null);
        _fetchRoadRoute(start, end);
      });
    }

    final List<LatLng> linePoints;
    if (_demoMode) {
      linePoints = routePoints;
    } else if (routePoints.length >= 2 &&
        _roadRoutePoints != null &&
        _roadRoutePoints!.length >= 2) {
      linePoints = _roadRoutePoints!;
    } else {
      linePoints = routePoints;
    }

    final markers = <Marker>[
      Marker(
        width: 40, height: 40,
        point: delivery,
        child: const Icon(Icons.location_on, color: DriverTheme.primaryBlue, size: 40),
      ),
    ];
    if (displayPos != null) {
      markers.add(Marker(
        width: 38, height: 38,
        point: displayPos,
        child: _bikeMarker(),
      ));
    }
    if (!_isWithDriver && !_demoMode &&
        rLat != null && rLng != null && (rLat != 0 || rLng != 0)) {
      markers.add(Marker(
        width: 32, height: 32,
        point: LatLng(rLat, rLng),
        child: const Icon(Icons.store, color: Colors.blueGrey, size: 32),
      ));
    }

    final pct = _demoRoute.isEmpty
        ? 0
        : ((_demoRouteIdx / math.max(1, _demoRoute.length - 1)) * 100).toInt();

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        appBar: AppBar(
          title: Text('طلب #$id',
              style: GoogleFonts.cairo(fontWeight: FontWeight.w700)),
          actions: [
            Padding(
              padding: const EdgeInsets.only(left: 8),
              child: _demoButton(),
            ),
          ],
        ),

        body: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [

              if (_demoMode)
                Container(
                  color: _demoArrived
                      ? Colors.green.shade900
                      : Colors.green.shade800,
                  padding: const EdgeInsets.fromLTRB(16, 6, 8, 6),
                  child: Row(
                    children: [
                      Icon(
                        _demoArrived
                            ? Icons.check_circle
                            : Icons.directions_bike_rounded,
                        color: Colors.white, size: 18,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              _demoRouteLoading
                                  ? 'جاري تحميل مسار المحاكاة...'
                                  : _demoArrived
                                      ? 'وصلت الدراجة! اضغط "تم التسليم" لتسجيله'
                                      : 'الدراجة في الطريق — $pct%',
                              style: GoogleFonts.cairo(
                                  color: Colors.white,
                                  fontSize: 12,
                                  fontWeight: FontWeight.w700),
                            ),
                            if (!_demoRouteLoading && !_demoArrived)
                              Padding(
                                padding: const EdgeInsets.only(top: 3),
                                child: LinearProgressIndicator(
                                  value: pct / 100,
                                  backgroundColor: Colors.white24,
                                  color: Colors.white,
                                  minHeight: 3,
                                ),
                              ),
                          ],
                        ),
                      ),
                      TextButton(
                        onPressed: _toggleDemoMode,
                        child: Text('إيقاف',
                            style: GoogleFonts.cairo(
                                color: Colors.white70, fontSize: 11)),
                      ),
                    ],
                  ),
                ),

              SizedBox(
                height: 265,
                child: Stack(
                  children: [
                    FlutterMap(
                      mapController: _mapController,
                      options: MapOptions(
                          initialCenter: _center(), initialZoom: 14),
                      children: [
                        TileLayer(
                          urlTemplate:
                              'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                          userAgentPackageName:
                              'com.single.store.driver',
                        ),

                        if (_demoMode && _demoRouteIdx > 0)
                          PolylineLayer(polylines: [
                            Polyline(
                              points: _demoRoute.sublist(
                                  0, _demoRouteIdx + 1),
                              color: Colors.grey.withOpacity(0.4),
                              strokeWidth: 3,
                            ),
                          ]),

                        if (linePoints.length >= 2)
                          PolylineLayer(polylines: [
                            Polyline(
                              points: linePoints,
                              color: _demoMode
                                  ? Colors.green.shade700
                                  : DriverTheme.primaryBlue,
                              strokeWidth: 5,
                            ),
                          ]),

                        MarkerLayer(markers: markers),
                      ],
                    ),

                    if (_roadRouteLoading || _demoRouteLoading)
                      Positioned(
                        top: 8, left: 0, right: 0,
                        child: Center(
                          child: Material(
                            color: cs.surfaceContainerHighest
                                .withOpacity(0.95),
                            borderRadius: BorderRadius.circular(20),
                            child: Padding(
                              padding: const EdgeInsets.symmetric(
                                  horizontal: 12, vertical: 6),
                              child: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  SizedBox(
                                    width: 16, height: 16,
                                    child: CircularProgressIndicator(
                                        strokeWidth: 2,
                                        color: DriverTheme.primaryBlue),
                                  ),
                                  const SizedBox(width: 8),
                                  Text(
                                    _demoRouteLoading
                                        ? 'جاري تحميل مسار المحاكاة...'
                                        : 'جاري تحميل المسار...',
                                    style: GoogleFonts.cairo(
                                        fontSize: 12,
                                        color: cs.onSurface),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ),

                    if (_demoMode && !_demoRouteLoading)
                      Positioned(
                        bottom: 8, left: 8,
                        child: Container(
                          padding: const EdgeInsets.symmetric(
                              horizontal: 10, vertical: 4),
                          decoration: BoxDecoration(
                            color: _demoArrived
                                ? Colors.green.shade900.withOpacity(0.92)
                                : Colors.green.shade800.withOpacity(0.88),
                            borderRadius: BorderRadius.circular(12),
                          ),
                          child: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Icon(
                                _demoArrived
                                    ? Icons.check_circle
                                    : Icons.directions_bike_rounded,
                                color: Colors.white, size: 13,
                              ),
                              const SizedBox(width: 5),
                              Text(
                                _demoArrived ? 'وصلت!' : '$pct%',
                                style: GoogleFonts.cairo(
                                    color: Colors.white,
                                    fontSize: 11,
                                    fontWeight: FontWeight.w700),
                              ),
                            ],
                          ),
                        ),
                      ),
                  ],
                ),
              ),

              Padding(
                padding: const EdgeInsets.symmetric(
                    horizontal: 16, vertical: 8),
                child: OutlinedButton.icon(
                  onPressed: _openInGoogleMaps,
                  icon: const Icon(Icons.map),
                  label: Text(_demoMode
                      ? 'فتح المسار في خرائط جوجل'
                      : 'فتح موقع الطلب في خرائط جوجل'),
                  style: OutlinedButton.styleFrom(
                    foregroundColor: DriverTheme.primaryBlue,
                    side: const BorderSide(color: DriverTheme.primaryBlue),
                  ),
                ),
              ),

              Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Card(
                      elevation: 0,
                      shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(16)),
                      child: Padding(
                        padding: const EdgeInsets.all(16),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              mainAxisAlignment:
                                  MainAxisAlignment.spaceBetween,
                              children: [
                                Text('الحالة',
                                    style: GoogleFonts.cairo(
                                        fontWeight: FontWeight.w600)),
                                Text(status,
                                    style: GoogleFonts.cairo(
                                        fontWeight: FontWeight.w700,
                                        color: DriverTheme.primaryBlue)),
                              ],
                            ),
                            const Divider(height: 20),
                            _row(context, 'الزبون', name),
                            if (phone.isNotEmpty)
                              _row(context, 'الهاتف', phone),
                            if (address.isNotEmpty)
                              _row(context, 'العنوان', address),
                            _row(context, 'الإجمالي', '$total ل.س'),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 16),

                    if (phone.isNotEmpty)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 8),
                        child: OutlinedButton.icon(
                          onPressed: () => _callPhone(phone),
                          icon: const Icon(Icons.call),
                          label: const Text('اتصال بالزبون'),
                          style: OutlinedButton.styleFrom(
                            foregroundColor: DriverTheme.primaryBlue,
                            side: const BorderSide(
                                color: DriverTheme.primaryBlue),
                          ),
                        ),
                      ),

                    if (_statusCode !=
                            DriverOrderDetailScreen._stDelivered &&
                        _statusCode !=
                            DriverOrderDetailScreen._stCancelled) ...[

                      if (!_demoMode &&
                          (_statusCode ==
                                  DriverOrderDetailScreen._stAccepted ||
                              _statusCode ==
                                  DriverOrderDetailScreen
                                      ._stReadyForPickup))
                        Padding(
                          padding: const EdgeInsets.only(bottom: 8),
                          child: FilledButton.icon(
                            onPressed: _markPickedUp,
                            icon: const Icon(Icons.play_circle_fill),
                            label: const Text('تم استلام الطلب'),
                            style: FilledButton.styleFrom(
                                backgroundColor: DriverTheme.primaryBlue),
                          ),
                        ),

                      if (!_demoMode &&
                          _statusCode ==
                              DriverOrderDetailScreen._stWithDriver)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 8),
                          child: FilledButton.icon(
                            onPressed:
                                _busyDeliver ? null : _markDelivered,
                            icon: _busyDeliver
                                ? const SizedBox(
                                    width: 20, height: 20,
                                    child: CircularProgressIndicator(
                                        strokeWidth: 2,
                                        color: Colors.white))
                                : const Icon(Icons.check_circle),
                            label: Text(_busyDeliver
                                ? 'جاري...'
                                : 'تم التسليم'),
                            style: FilledButton.styleFrom(
                                backgroundColor: DriverTheme.primaryBlue),
                          ),
                        ),

                      if (_demoMode && _demoRoute.isNotEmpty)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 8),
                          child: FilledButton.icon(
                            onPressed:
                                _busyDeliver ? null : _demoMarkDelivered,
                            icon: _busyDeliver
                                ? const SizedBox(
                                    width: 20, height: 20,
                                    child: CircularProgressIndicator(
                                        strokeWidth: 2,
                                        color: Colors.white))
                                : const Icon(Icons.check_circle),
                            label: Text(_busyDeliver
                                ? 'جاري التسجيل...'
                                : _demoArrived ? '✅ تم التسليم — وصلت الدراجة' : '✅ تم التسليم — تسجيل فوري'),
                            style: FilledButton.styleFrom(
                                backgroundColor: Colors.green.shade700),
                          ),
                        ),

                      if (!_demoMode)
                        OutlinedButton.icon(
                          onPressed: _busyCancel ? null : _cancelOrder,
                          icon: _busyCancel
                              ? const SizedBox(
                                  width: 18, height: 18,
                                  child: CircularProgressIndicator(
                                      strokeWidth: 2))
                              : const Icon(Icons.cancel_outlined),
                          label: const Text('إلغاء الطلب'),
                          style: OutlinedButton.styleFrom(
                            foregroundColor: Colors.red,
                            side: const BorderSide(color: Colors.red),
                          ),
                        ),
                    ],
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _demoButton() {
    return Tooltip(
      message: _demoMode ? 'إيقاف المحاكاة' : 'محاكاة التوصيل',
      child: InkWell(
        borderRadius: BorderRadius.circular(22),
        onTap: _toggleDemoMode,
        child: Container(
          width: 40, height: 40,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: _demoMode
                ? Colors.green.shade700.withOpacity(0.15)
                : Theme.of(context).colorScheme.surfaceContainerHighest,
          ),
          child: Icon(
            _demoMode
                ? Icons.stop_rounded
                : Icons.directions_bike_rounded,
            color: _demoMode
                ? Colors.green.shade700
                : DriverTheme.primaryBlue,
            size: 22,
          ),
        ),
      ),
    );
  }

  Widget _bikeMarker() {
    if (_demoMode && _demoArrived) {
      return Container(
        decoration: BoxDecoration(
            color: Colors.green.shade900, shape: BoxShape.circle),
        child: const Icon(Icons.check_circle, color: Colors.white, size: 24),
      );
    }
    if (_demoMode) {
      return Container(
        decoration: BoxDecoration(
          color: Colors.green.shade700,
          shape: BoxShape.circle,
          boxShadow: [
            BoxShadow(
                color: Colors.green.shade700.withOpacity(0.5),
                blurRadius: 10,
                spreadRadius: 2),
          ],
        ),
        child: const Icon(Icons.directions_bike_rounded,
            color: Colors.white, size: 22),
      );
    }
    return const Icon(Icons.delivery_dining, color: Colors.green, size: 36);
  }

  Widget _row(BuildContext context, String label, String value) {
    final cs = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
              width: 80,
              child: Text(label,
                  style: GoogleFonts.cairo(
                      fontSize: 14, color: cs.onSurfaceVariant))),
          Expanded(
              child: Text(value,
                  style: GoogleFonts.cairo(
                      fontSize: 14, color: cs.onSurface))),
        ],
      ),
    );
  }
}
