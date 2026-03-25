import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../app_config.dart';
import '../models/app_state.dart';
import '../services/api.dart';
import '../widgets/brand_title.dart';
import '../services/push.dart';
import 'complete_profile_screen.dart';
import 'home_screen.dart';
import 'blocked_screen.dart';

class AuthScreen extends StatefulWidget {
  static const route = '/auth';
  const AuthScreen({super.key, required this.prefs, required this.state});

  final SharedPreferences prefs;
  final AppState state;

  @override
  State<AuthScreen> createState() => _AuthScreenState();
}

class _AuthScreenState extends State<AuthScreen> {
  final phoneCtrl = TextEditingController();
  bool loading = false;
  String? error;

  String get baseUrl => kBackendBaseUrl;

  @override
  void dispose() {
    phoneCtrl.dispose();
    super.dispose();
  }

  Future<void> _persistCustomer(Map<String, dynamic> c) async {
    await widget.prefs.setInt('customerId', c['id'] as int);
    await widget.prefs.setString('customerName', (c['name'] ?? '') as String);
    await widget.prefs.setString('customerPhone', (c['phone'] ?? '') as String);
    await widget.prefs.setDouble('defaultLat', (c['defaultLat'] as num? ?? 0).toDouble());
    await widget.prefs.setDouble('defaultLng', (c['defaultLng'] as num? ?? 0).toDouble());
    await widget.prefs.setString('defaultAddress', (c['defaultAddress'] ?? '') as String);
    widget.state.setCustomer(
      id: c['id'] as int,
      name: (c['name'] ?? '') as String,
      phone: (c['phone'] ?? '') as String,
      lat: (c['defaultLat'] as num? ?? 0).toDouble(),
      lng: (c['defaultLng'] as num? ?? 0).toDouble(),
      address: c['defaultAddress'] as String?,
    );
  }

  Future<void> _login() async {
    final phone = phoneCtrl.text.trim();
    if (phone.isEmpty || phone.length < 7) {
      setState(() => error = 'أدخل رقم هاتف صحيح');
      return;
    }

    setState(() {
      loading = true;
      error = null;
    });

    try {
      final api = ApiClient(baseUrl: baseUrl);
      final res = await api.loginByPhone(phone: phone);

      if (!mounted) return;

      if (res['error'] == 'customer_blocked') {
        Navigator.of(context).pushReplacement(
          MaterialPageRoute(
            builder: (_) => BlockedScreen(prefs: widget.prefs, state: widget.state),
          ),
        );
        return;
      }

      if (res['requiresProfile'] == true) {
        Navigator.of(context).pushReplacement(
          MaterialPageRoute(
            builder: (_) => CompleteProfileScreen(
              prefs: widget.prefs,
              state: widget.state,
              phone: phone,
            ),
          ),
        );
        return;
      }

      await _persistCustomer(res);
      final cid = res['id'] as int;
      try {
        await PushService(api: api, platformTag: 'android').initForCustomer(customerId: cid);
      } catch (_) {}

      if (!mounted) return;
      Navigator.of(context).pushReplacementNamed(HomeScreen.route);
    } catch (e) {
      if (mounted) setState(() => error = 'تعذر الاتصال بالسيرفر. تحقق من الإنترنت.');
    } finally {
      if (mounted) setState(() => loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final primary = theme.colorScheme.primary;
    final secondary = theme.colorScheme.secondary;

    return Scaffold(
      backgroundColor: const Color(0xFFF8F7FC),
      appBar: AppBar(
        elevation: 0,
        backgroundColor: Colors.transparent,
        title: BrandTitle(state: widget.state, suffix: 'تسجيل الدخول'),
        titleTextStyle: theme.textTheme.titleLarge?.copyWith(
          fontWeight: FontWeight.w800,
          color: const Color(0xFF1A1A1A),
        ),
      ),
      body: SafeArea(
        child: Column(
          children: [
            
            Container(
              height: 4,
              width: double.infinity,
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.centerLeft,
                  end: Alignment.centerRight,
                  colors: [primary, secondary],
                ),
              ),
            ),
            Expanded(
              child: SingleChildScrollView(
                padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
                child: Column(
                  children: [
                    const SizedBox(height: 8),
                    
                    Builder(builder: (context) {
                      final logo = (widget.state.logoUrl ?? '').trim();
                      if (logo.isNotEmpty) {
                        return Container(
                          decoration: BoxDecoration(
                            borderRadius: BorderRadius.circular(20),
                            boxShadow: [
                              BoxShadow(
                                color: primary.withOpacity(0.15),
                                blurRadius: 16,
                                offset: const Offset(0, 4),
                              ),
                            ],
                          ),
                          child: ClipRRect(
                            borderRadius: BorderRadius.circular(20),
                            child: Image.network(
                              logo,
                              width: 100,
                              height: 100,
                              fit: BoxFit.cover,
                              errorBuilder: (_, __, ___) => const SizedBox(width: 100, height: 100),
                            ),
                          ),
                        );
                      }
                      return const SizedBox(width: 100, height: 100);
                    }),
                    const SizedBox(height: 20),
                    Text(
                      'مرحباً بك في ${widget.state.storeName.trim().isEmpty ? 'متجرنا' : widget.state.storeName.trim()}',
                      textAlign: TextAlign.center,
                      style: theme.textTheme.headlineSmall?.copyWith(
                        fontWeight: FontWeight.w900,
                        color: const Color(0xFF1A1A1A),
                      ),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'أدخل رقم هاتفك للمتابعة',
                      textAlign: TextAlign.center,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: const Color(0xFF616161),
                      ),
                    ),
                    const SizedBox(height: 32),

                    _InputCard(
                      child: TextField(
                        controller: phoneCtrl,
                        keyboardType: TextInputType.phone,
                        textDirection: TextDirection.ltr,
                        style: theme.textTheme.bodyLarge,
                        textInputAction: TextInputAction.done,
                        onSubmitted: (_) => loading ? null : _login(),
                        decoration: InputDecoration(
                          hintText: '09xxxxxxxx',
                          hintStyle: theme.textTheme.bodyLarge?.copyWith(color: const Color(0xFF9E9E9E)),
                          border: InputBorder.none,
                          prefixIcon: Icon(Icons.phone_outlined, color: primary, size: 22),
                        ),
                      ),
                    ),

                    if (error != null) ...[
                      const SizedBox(height: 14),
                      Container(
                        width: double.infinity,
                        padding: const EdgeInsets.all(14),
                        decoration: BoxDecoration(
                          color: theme.colorScheme.error.withOpacity(0.08),
                          borderRadius: BorderRadius.circular(14),
                          border: Border.all(color: theme.colorScheme.error.withOpacity(0.3)),
                        ),
                        child: Text(
                          error!,
                          style: theme.textTheme.bodyMedium?.copyWith(
                            color: theme.colorScheme.error,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                    ],

                    const SizedBox(height: 24),

                    SizedBox(
                      width: double.infinity,
                      height: 56,
                      child: FilledButton(
                        onPressed: loading ? null : _login,
                        style: FilledButton.styleFrom(
                          backgroundColor: primary,
                          foregroundColor: Colors.white,
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(16),
                          ),
                          elevation: 2,
                          shadowColor: primary.withOpacity(0.35),
                        ),
                        child: loading
                            ? const SizedBox(
                                width: 26,
                                height: 26,
                                child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                              )
                            : Text(
                                'متابعة',
                                style: theme.textTheme.titleMedium?.copyWith(
                                  color: Colors.white,
                                  fontWeight: FontWeight.w800,
                                ),
                              ),
                      ),
                    ),

                    const SizedBox(height: 20),
                    Text(
                      'إذا كان هذا أول دخول لك، ستُطلب منك إكمال بياناتك.',
                      textAlign: TextAlign.center,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: const Color(0xFF9E9E9E),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _InputCard extends StatelessWidget {
  final Widget child;
  const _InputCard({required this.child});
  @override
  Widget build(BuildContext context) {
    final primary = Theme.of(context).colorScheme.primary;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: const Color(0xFFEEEEEE)),
        boxShadow: [
          BoxShadow(
            color: primary.withOpacity(0.04),
            blurRadius: 12,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: child,
    );
  }
}
