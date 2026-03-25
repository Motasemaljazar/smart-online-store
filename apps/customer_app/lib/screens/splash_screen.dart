import 'package:flutter/material.dart';
import 'dart:convert';
import 'package:shared_preferences/shared_preferences.dart';
import '../app_config.dart';
import '../models/app_state.dart';
import '../services/api.dart';
import '../services/push.dart';
import '../services/realtime.dart';
import 'onboarding_screen.dart';
import 'auth_screen.dart';
import 'home_screen.dart';
import 'closed_screen.dart';
import 'blocked_screen.dart';

class SplashScreen extends StatefulWidget {
  const SplashScreen({super.key, required this.prefs, required this.state});
  final SharedPreferences prefs;
  final AppState state;

  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen> {
  late final String baseUrl;
  Map<String, dynamic>? settings;
  int _phase = 0;

  @override
  void initState() {
    super.initState();
    baseUrl = kBackendBaseUrl;
    _init();
  }

  Future<void> _init() async {
    
    try {
      final cached = widget.prefs.getString('cached_settings');
      if (cached != null && cached.trim().isNotEmpty) {
        final json = jsonDecode(cached) as Map<String, dynamic>;
        widget.state.setConfig(json);
      }
    } catch (_) {}

    try {
      final rt = RealtimeClient(baseUrl: baseUrl);
      await rt.connectPublic(onSettingsUpdated: () async {
        try {
          final api = ApiClient(baseUrl: baseUrl);
          final s = await api.getSettings();
          widget.state.setConfig(s);
          await widget.prefs.setString('cached_settings', jsonEncode(s));
        } catch (_) {}
      });
    } catch (_) {}

    try {
      final api = ApiClient(baseUrl: baseUrl);
      settings = await api.getSettings();
      if (settings != null) {
        widget.state.setConfig(settings!);
        try {
          await widget.prefs.setString('cached_settings', jsonEncode(settings));
        } catch (_) {}
      }
    } catch (_) {
      
    }

    if (!widget.state.isAcceptingOrders) {
      if (!mounted) return;
      Navigator.of(context).pushReplacement(
        MaterialPageRoute(
          builder: (_) => _ClosedGate(
            baseUrl: baseUrl,
            prefs: widget.prefs,
            state: widget.state,
          ),
        ),
      );
      return;
    }

    final bgs = widget.state.splashBackgrounds;
    if (bgs.isNotEmpty) {
      setState(() => _phase = 1);
      await Future.delayed(const Duration(seconds: 3));
      if (bgs.length > 1) {
        setState(() => _phase = 2);
        await Future.delayed(const Duration(seconds: 3));
      }
    }

    final seen = widget.prefs.getBool('seenOnboarding') ?? false;
    if (!seen) {
      if (mounted)
        Navigator.of(context).pushReplacementNamed(OnboardingScreen.route);
      return;
    }

    final cid = widget.prefs.getInt('customerId');
    final name = widget.prefs.getString('customerName');
    final phone = widget.prefs.getString('customerPhone') ?? '';
    final lat = widget.prefs.getDouble('defaultLat');
    final lng = widget.prefs.getDouble('defaultLng');
    final addr = widget.prefs.getString('defaultAddress');

    if (cid != null && name != null && lat != null && lng != null) {
      
      try {
        final api = ApiClient(baseUrl: baseUrl);
        final session = await api.loginByPhone(phone: phone);
        if (session['error'] == 'customer_blocked') {
          if (mounted) {
            Navigator.of(context).pushReplacement(
              MaterialPageRoute(
                builder: (_) => BlockedScreen(prefs: widget.prefs, state: widget.state),
              ),
            );
          }
          return;
        }
      } catch (_) {
        
      }

      widget.state.setCustomer(id: cid, name: name, phone: phone, lat: lat, lng: lng, address: addr);
      try {
        await PushService(api: ApiClient(baseUrl: baseUrl), platformTag: 'android')
            .initForCustomer(customerId: cid);
      } catch (_) {}
      if (mounted) Navigator.of(context).pushReplacementNamed(HomeScreen.route);
    } else {
      if (mounted) Navigator.of(context).pushReplacementNamed(AuthScreen.route);
    }
  }

  @override
  Widget build(BuildContext context) {
    
    String? logo = widget.state.logoUrl;
    if (logo != null && logo.startsWith('/')) {
      logo = '$baseUrl$logo';
    }

    final bgs = widget.state.splashBackgrounds;
    final theme = Theme.of(context);

    return Scaffold(
      body: Stack(
        fit: StackFit.expand,
        children: [
          
          Container(
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                colors: [
                  const Color(0xFF1A1A2E),
                  const Color(0xFF3D2C6E),
                  const Color(0xFF5C4A8E),
                ],
              ),
            ),
          ),

          if (bgs.isNotEmpty && _phase > 0)
            Opacity(
              opacity: 0.3,
              child: Image.network(
                bgs[_phase == 1 ? 0 : (bgs.length > 1 ? 1 : 0)],
                fit: BoxFit.cover,
                errorBuilder: (_, __, ___) => const SizedBox(),
              ),
            ),

          Container(
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                colors: [
                  Colors.black.withOpacity(0.3),
                  Colors.black.withOpacity(0.1),
                ],
              ),
            ),
          ),

          Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                
                Container(
                  width: 120,
                  height: 120,
                  decoration: BoxDecoration(
                    color: Colors.white,
                    shape: BoxShape.circle,
                    boxShadow: [
                      BoxShadow(
                        color: Colors.black.withOpacity(0.2),
                        blurRadius: 20,
                        spreadRadius: 5,
                      ),
                    ],
                  ),
                  padding: const EdgeInsets.all(16),
                  child: logo != null
                      ? ClipOval(
                          child: Image.network(
                            logo,
                            fit: BoxFit.cover,
                            gaplessPlayback: true,
                            loadingBuilder: (_, child, progress) =>
                                progress == null
                                    ? child
                                    : const Center(
                                        child: SizedBox(
                                          width: 40,
                                          height: 40,
                                          child: CircularProgressIndicator(
                                            strokeWidth: 2,
                                            valueColor: AlwaysStoppedAnimation<Color>(
                                                Color(0xFFD4AF37)),
                                          ),
                                        ),
                                      ),
                            errorBuilder: (_, __, ___) => const Icon(
                              Icons.store_mall_directory,
                              size: 60,
                              color: Color(0xFFD4AF37),
                            ),
                          ),
                        )
                      : const Icon(
                          Icons.store_mall_directory,
                          size: 60,
                          color: Color(0xFFD4AF37),
                        ),
                ),
                const SizedBox(height: 24),

                Text(
                  widget.state.storeName,
                  style: theme.textTheme.headlineMedium?.copyWith(
                    color: Colors.white,
                    fontWeight: FontWeight.w900,
                    shadows: [
                      Shadow(
                        color: Colors.black.withOpacity(0.3),
                        offset: const Offset(0, 2),
                        blurRadius: 4,
                      ),
                    ],
                  ),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 8),

                Text(
                  'متجرك الإلكتروني الأول',
                  style: theme.textTheme.titleMedium?.copyWith(
                    color: const Color(0xFFD4AF37).withOpacity(0.9),
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 40),

                Container(
                  width: 50,
                  height: 50,
                  padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(
                    color: Colors.white.withOpacity(0.2),
                    shape: BoxShape.circle,
                  ),
                  child: const CircularProgressIndicator(
                    strokeWidth: 3,
                    valueColor: AlwaysStoppedAnimation<Color>(Colors.white),
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

class _ClosedGate extends StatelessWidget {
  const _ClosedGate(
      {required this.baseUrl, required this.prefs, required this.state});
  final String baseUrl;
  final SharedPreferences prefs;
  final AppState state;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: ClosedScreen(
        state: state,
        onRefresh: () async {
          try {
            final api = ApiClient(baseUrl: baseUrl);
            final s = await api.getSettings();
            state.setConfig(s);
            try {
              await prefs.setString('cached_settings', jsonEncode(s));
            } catch (_) {}
            final open = state.isAcceptingOrders;
            if (open && context.mounted) {
              Navigator.of(context).pushReplacement(MaterialPageRoute(
                  builder: (_) => SplashScreen(prefs: prefs, state: state)));
            }
            return open;
          } catch (_) {
            return false;
          }
        },
      ),
    );
  }
}
