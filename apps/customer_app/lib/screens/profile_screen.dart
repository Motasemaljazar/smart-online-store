import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../app_config.dart';
import '../models/app_state.dart';
import '../services/api.dart';
import '../widgets/brand_title.dart';
import '../widgets/support_card.dart';
import '../widgets/social_bubbles.dart';
import 'addresses_screen.dart';
import 'auth_screen.dart';
import 'complaints_screen.dart';

class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key, required this.state, required this.prefs, required this.api});
  final AppState state;
  final SharedPreferences prefs;
  final ApiClient api;

  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  bool saving = false;

  Future<Position> _getCurrentPositionOrThrow() async {
    final serviceEnabled = await Geolocator.isLocationServiceEnabled();
    if (!serviceEnabled) throw Exception('يرجى تفعيل خدمة الموقع (GPS)');
    var perm = await Geolocator.checkPermission();
    if (perm == LocationPermission.denied) perm = await Geolocator.requestPermission();
    if (perm == LocationPermission.denied) throw Exception('تم رفض إذن الموقع');
    if (perm == LocationPermission.deniedForever) throw Exception('إذن الموقع مرفوض نهائياً. افتح إعدادات الهاتف وفعّل إذن الموقع للتطبيق');
    return await Geolocator.getCurrentPosition(desiredAccuracy: LocationAccuracy.high);
  }

  Future<void> _applyCustomer(Map<String, dynamic> c) async {
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

  Future<void> _editProfile() async {
    final nameCtrl = TextEditingController(text: (widget.state.customerName ?? '').trim());
    final phoneCtrl = TextEditingController(text: (widget.state.customerPhone ?? '').trim());

    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('تعديل بياناتي'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: nameCtrl,
              decoration: const InputDecoration(labelText: 'الاسم', prefixIcon: Icon(Icons.person_outline), border: OutlineInputBorder()),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: phoneCtrl,
              keyboardType: TextInputType.phone,
              decoration: const InputDecoration(labelText: 'الهاتف', prefixIcon: Icon(Icons.phone_outlined), border: OutlineInputBorder()),
            ),
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(ctx).pop(false), child: const Text('إلغاء')),
          FilledButton(onPressed: () => Navigator.of(ctx).pop(true), child: const Text('حفظ')),
        ],
      ),
    );

    if (ok != true) return;
    final name = nameCtrl.text.trim();
    final phone = phoneCtrl.text.trim();
    if (name.isEmpty || phone.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('الاسم ورقم الهاتف مطلوبان')));
      return;
    }

    setState(() => saving = true);
    try {
      final customerId = widget.prefs.getInt('customerId') ?? 0;
      final lat = widget.state.defaultLat ?? 0.0;
      final lng = widget.state.defaultLng ?? 0.0;
      final c = await widget.api.registerCustomer(
        name: name,
        phone: phone,
        lat: lat,
        lng: lng,
        address: widget.state.defaultAddress,
      );
      await _applyCustomer(c);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('تم حفظ بياناتك بنجاح')));
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('فشل حفظ البيانات: $e')));
    } finally {
      if (mounted) setState(() => saving = false);
    }
  }

  Future<void> _updateLocation() async {
    setState(() => saving = true);
    try {
      final pos = await _getCurrentPositionOrThrow();
      final name = (widget.state.customerName ?? '').trim();
      final phone = (widget.state.customerPhone ?? '').trim();
      final c = await widget.api.registerCustomer(
        name: name.isEmpty ? 'زبون' : name,
        phone: phone,
        lat: pos.latitude,
        lng: pos.longitude,
        address: widget.state.defaultAddress,
      );
      await _applyCustomer(c);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('تم تحديث موقعك الحالي بنجاح')));
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('فشل تحديث الموقع: $e')));
    } finally {
      if (mounted) setState(() => saving = false);
    }
  }

  Future<void> _logout() async {
    
    await widget.prefs.remove('customerId');
    await widget.prefs.remove('customerName');
    await widget.prefs.remove('customerPhone');
    await widget.prefs.remove('defaultLat');
    await widget.prefs.remove('defaultLng');
    await widget.prefs.remove('defaultAddress');
    widget.state.clearCustomer();
    if (mounted) {
      Navigator.of(context).pushNamedAndRemoveUntil(AuthScreen.route, (_) => false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final name = (widget.state.customerName ?? '').trim();
    final phone = (widget.state.customerPhone ?? '').trim();
    final address = (widget.state.defaultAddress ?? '').trim();
    final cs = Theme.of(context).colorScheme;
    final theme = Theme.of(context).textTheme;
    final displayName = name.isEmpty ? 'حسابي' : name;
    final initial = name.isNotEmpty ? name[0].toUpperCase() : '؟';

    return Directionality(
      textDirection: TextDirection.rtl,
      child: Scaffold(
        backgroundColor: cs.surface,
        appBar: AppBar(
          title: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              BrandTitle(state: widget.state),
              const SizedBox(width: 10),
              Flexible(
                child: Text(
                  displayName,
                  overflow: TextOverflow.ellipsis,
                  style: theme.titleMedium?.copyWith(fontWeight: FontWeight.w800, color: cs.onSurface),
                ),
              ),
            ],
          ),
          backgroundColor: cs.surface,
          foregroundColor: cs.onSurface,
          elevation: 0,
        ),
        body: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
          children: [
            
            Container(
              padding: const EdgeInsets.symmetric(vertical: 20, horizontal: 16),
              decoration: BoxDecoration(
                color: cs.surfaceContainerLow,
                borderRadius: BorderRadius.circular(20),
                border: Border.all(color: cs.outlineVariant.withOpacity(0.5)),
              ),
              child: Row(
                children: [
                  CircleAvatar(
                    radius: 36,
                    backgroundColor: cs.primaryContainer,
                    child: Text(
                      initial,
                      style: theme.headlineMedium?.copyWith(fontWeight: FontWeight.w900, color: cs.onPrimaryContainer),
                    ),
                  ),
                  const SizedBox(width: 16),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(displayName, style: theme.titleLarge?.copyWith(fontWeight: FontWeight.w900, color: cs.onSurface)),
                        if (phone.isNotEmpty) ...[
                          const SizedBox(height: 4),
                          Row(
                            children: [
                              Icon(Icons.phone_outlined, size: 16, color: cs.onSurfaceVariant),
                              const SizedBox(width: 6),
                              Text(phone, style: theme.bodyMedium?.copyWith(color: cs.onSurfaceVariant, fontSize: 13)),
                            ],
                          ),
                        ],
                        if (address.isNotEmpty) ...[
                          const SizedBox(height: 2),
                          Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Icon(Icons.location_on_outlined, size: 16, color: cs.onSurfaceVariant),
                              const SizedBox(width: 6),
                              Expanded(
                                child: Text(address, style: theme.bodySmall?.copyWith(color: cs.onSurfaceVariant, fontSize: 12), maxLines: 2, overflow: TextOverflow.ellipsis),
                              ),
                            ],
                          ),
                        ],
                      ],
                    ),
                  ),
                ],
              ),
            ),

            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                  child: FilledButton.icon(
                    onPressed: saving ? null : _editProfile,
                    icon: const Icon(Icons.edit_outlined, size: 20),
                    label: const Text('تعديل بياناتي'),
                    style: FilledButton.styleFrom(padding: const EdgeInsets.symmetric(vertical: 12), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12))),
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: OutlinedButton.icon(
                    onPressed: saving ? null : _updateLocation,
                    icon: const Icon(Icons.my_location_outlined, size: 20),
                    label: const Text('تعديل موقعي'),
                    style: OutlinedButton.styleFrom(
                      padding: const EdgeInsets.symmetric(vertical: 12),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                      foregroundColor: cs.primary,
                      side: BorderSide(color: cs.outline),
                    ),
                  ),
                ),
              ],
            ),

            const SizedBox(height: 20),
            Text('الإعدادات', style: theme.titleSmall?.copyWith(fontWeight: FontWeight.w800, color: cs.onSurfaceVariant, letterSpacing: 0.5)),
            const SizedBox(height: 8),
            Container(
              decoration: BoxDecoration(
                color: cs.surfaceContainerLow,
                borderRadius: BorderRadius.circular(16),
                border: Border.all(color: cs.outlineVariant.withOpacity(0.5)),
              ),
              child: Column(
                children: [
                  SwitchListTile.adaptive(
                    value: widget.state.isDarkMode,
                    onChanged: (_) => widget.state.toggleTheme(),
                    secondary: Icon(Icons.dark_mode_outlined, color: cs.onSurface),
                    title: Text('الوضع الليلي', style: theme.titleMedium?.copyWith(fontWeight: FontWeight.w700, color: cs.onSurface)),
                    subtitle: Text('تفعيل المظهر الداكن', style: theme.bodySmall?.copyWith(color: cs.onSurfaceVariant)),
                    activeColor: cs.primary,
                  ),
                  Divider(height: 1, color: cs.outlineVariant.withOpacity(0.5)),
                  ListTile(
                    leading: Icon(Icons.location_city_outlined, color: cs.primary),
                    title: Text('عناويني المحفوظة', style: theme.titleMedium?.copyWith(fontWeight: FontWeight.w700, color: cs.onSurface)),
                    subtitle: Text('إضافة أو تعديل عناوين التوصيل', style: theme.bodySmall?.copyWith(color: cs.onSurfaceVariant)),
                    trailing: Icon(Icons.chevron_right, color: cs.onSurfaceVariant),
                    onTap: () async {
                      await Navigator.push(context, MaterialPageRoute(builder: (_) => AddressesScreen(api: widget.api, state: widget.state)));
                    },
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
                  ),
                  Divider(height: 1, color: cs.outlineVariant.withOpacity(0.5)),
                  ListTile(
                    leading: Icon(Icons.report_problem_outlined, color: cs.primary),
                    title: Text('الشكاوى والمقترحات', style: theme.titleMedium?.copyWith(fontWeight: FontWeight.w700, color: cs.onSurface)),
                    subtitle: Text('تقديم شكوى أو طلب دعم', style: theme.bodySmall?.copyWith(color: cs.onSurfaceVariant)),
                    trailing: Icon(Icons.chevron_right, color: cs.onSurfaceVariant),
                    onTap: () async {
                      await Navigator.push(context, MaterialPageRoute(builder: (_) => ComplaintsScreen(api: widget.api, state: widget.state)));
                    },
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
                  ),
                ],
              ),
            ),

            const SizedBox(height: 20),
            SupportCard(state: widget.state),

            const SizedBox(height: 16),
            SocialBubbles(state: widget.state),

            const SizedBox(height: 24),
            OutlinedButton.icon(
              icon: Icon(Icons.logout, color: cs.error, size: 22),
              label: Text('تسجيل الخروج', style: theme.titleMedium?.copyWith(fontWeight: FontWeight.w800, color: cs.error)),
              onPressed: saving ? null : _logout,
              style: OutlinedButton.styleFrom(
                padding: const EdgeInsets.symmetric(vertical: 14),
                side: BorderSide(color: cs.error.withOpacity(0.7)),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
              ),
            ),

            const SizedBox(height: 32),
            _buildAppPromo(context, theme, cs),
            const SizedBox(height: 20),
          ],
        ),
      ),
    );
  }

  static const String _devWhatsApp = '963988520360';

  Future<void> _openDevWhatsApp() async {
    
    debugPrint('WhatsApp: $_devWhatsApp');
  }

  Widget _buildAppPromo(BuildContext context, TextTheme? theme, ColorScheme cs) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 20),
      decoration: BoxDecoration(
        color: cs.surfaceContainerHighest.withOpacity(0.5),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: cs.outlineVariant.withOpacity(0.4)),
      ),
      child: Directionality(
        textDirection: TextDirection.rtl,
        child: RichText(
          textAlign: TextAlign.center,
          text: TextSpan(
            style: theme?.bodyMedium?.copyWith(color: cs.onSurface, height: 1.5),
            children: [
              const TextSpan(text: 'اعجبك التطبيق؟ فريقنا جاهز لإنشاء تطبيقك الخاص. '),
              TextSpan(
                text: 'تواصل معنا',
                style: theme?.bodyMedium?.copyWith(color: cs.primary, fontWeight: FontWeight.w800, decoration: TextDecoration.underline, decorationColor: cs.primary),
                recognizer: TapGestureRecognizer()..onTap = _openDevWhatsApp,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
