import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import '../services/agent_api.dart';
import '../models/agent_state.dart';
import '../app_config.dart';
import 'agent_home_screen.dart';

class AgentLoginScreen extends StatefulWidget {
  const AgentLoginScreen({super.key, required this.state});
  final AgentState state;

  @override
  State<AgentLoginScreen> createState() => _AgentLoginScreenState();
}

class _AgentLoginScreenState extends State<AgentLoginScreen> {
  final _phoneCtl = TextEditingController();
  final _passCtl = TextEditingController();
  bool _loading = false;
  String? _error;
  bool _obscure = true;

  Future<void> _login() async {
    final phone = _phoneCtl.text.trim();
    final pass = _passCtl.text.trim();
    if (phone.isEmpty || pass.isEmpty) {
      setState(() => _error = 'يرجى ملء جميع الحقول');
      return;
    }
    setState(() { _loading = true; _error = null; });
    try {
      final api = AgentApi(baseUrl: kBackendBaseUrl);
      final res = await api.login(phone: phone, password: pass);
      final token = (res['token'] ?? res['accessToken'] ?? '').toString();
      final agentId = (res['agentId'] ?? res['id'] ?? 0) as int;
      final name = (res['name'] ?? res['agentName'] ?? '').toString();
      if (token.isEmpty) throw Exception('فشل تسجيل الدخول: لم يُرجع السيرفر token');
      await widget.state.saveSession(
        token: token,
        agentId: agentId,
        name: name,
        phone: phone,
      );
      if (!mounted) return;
      Navigator.of(context).pushReplacement(
        MaterialPageRoute(
          builder: (_) => AgentHomeScreen(state: widget.state, api: api),
        ),
      );
    } catch (e) {
      setState(() => _error = 'فشل تسجيل الدخول: ${e.toString()}');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  void dispose() {
    _phoneCtl.dispose();
    _passCtl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return Scaffold(
      backgroundColor: cs.primary,
      body: SafeArea(
        child: Column(
          children: [
            
            Expanded(
              flex: 2,
              child: Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(
                      width: 90,
                      height: 90,
                      decoration: BoxDecoration(
                        color: Colors.white.withOpacity(0.2),
                        shape: BoxShape.circle,
                      ),
                      child: const Icon(Icons.badge_rounded, size: 48, color: Colors.white),
                    ),
                    const SizedBox(height: 20),
                    Text(
                      'تطبيق المندوب',
                      style: GoogleFonts.cairo(
                        fontSize: 28,
                        fontWeight: FontWeight.w800,
                        color: Colors.white,
                      ),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'أدر منتجاتك وتواصل مع عملائك',
                      style: GoogleFonts.cairo(
                        fontSize: 15,
                        color: Colors.white.withOpacity(0.85),
                      ),
                    ),
                  ],
                ),
              ),
            ),

            Expanded(
              flex: 3,
              child: Container(
                width: double.infinity,
                padding: const EdgeInsets.all(28),
                decoration: const BoxDecoration(
                  color: Colors.white,
                  borderRadius: BorderRadius.vertical(top: Radius.circular(32)),
                ),
                child: SingleChildScrollView(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        'تسجيل الدخول',
                        textAlign: TextAlign.center,
                        style: GoogleFonts.cairo(
                          fontSize: 22,
                          fontWeight: FontWeight.w800,
                          color: const Color(0xFF1A1A1A),
                        ),
                      ),
                      const SizedBox(height: 28),

                      TextField(
                        controller: _phoneCtl,
                        keyboardType: TextInputType.phone,
                        textDirection: TextDirection.ltr,
                        decoration: const InputDecoration(
                          labelText: 'رقم الهاتف',
                          prefixIcon: Icon(Icons.phone_rounded),
                        ),
                      ),
                      const SizedBox(height: 16),

                      TextField(
                        controller: _passCtl,
                        obscureText: _obscure,
                        decoration: InputDecoration(
                          labelText: 'كلمة المرور',
                          prefixIcon: const Icon(Icons.lock_rounded),
                          suffixIcon: IconButton(
                            icon: Icon(_obscure ? Icons.visibility_rounded : Icons.visibility_off_rounded),
                            onPressed: () => setState(() => _obscure = !_obscure),
                          ),
                        ),
                        onSubmitted: (_) => _login(),
                      ),

                      if (_error != null) ...[
                        const SizedBox(height: 16),
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                          decoration: BoxDecoration(
                            color: Colors.red.shade50,
                            borderRadius: BorderRadius.circular(10),
                            border: Border.all(color: Colors.red.shade200),
                          ),
                          child: Row(
                            children: [
                              Icon(Icons.error_outline_rounded, color: Colors.red.shade700, size: 20),
                              const SizedBox(width: 10),
                              Expanded(
                                child: Text(
                                  _error!,
                                  style: GoogleFonts.cairo(color: Colors.red.shade700, fontSize: 13),
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],

                      const SizedBox(height: 24),

                      FilledButton(
                        onPressed: _loading ? null : _login,
                        style: FilledButton.styleFrom(
                          minimumSize: const Size.fromHeight(52),
                        ),
                        child: _loading
                            ? const SizedBox(
                                width: 22,
                                height: 22,
                                child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white),
                              )
                            : Text(
                                'دخول',
                                style: GoogleFonts.cairo(fontSize: 16, fontWeight: FontWeight.w700),
                              ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
