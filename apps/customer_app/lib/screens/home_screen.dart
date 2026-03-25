import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'dart:convert';
import 'package:shared_preferences/shared_preferences.dart';
import '../app_config.dart';
import '../models/app_state.dart';
import '../services/api.dart';
import '../services/app_refs.dart';
import 'orders_screen.dart';
import 'cart_screen.dart';
import 'menu_screen.dart';
import 'complaints_screen.dart';
import 'notifications_screen.dart';
import 'profile_screen.dart';
import 'auth_screen.dart';
import 'blocked_screen.dart';
import '../widgets/brand_title.dart';
import 'closed_screen.dart';
import '../services/realtime.dart';
import 'favorites_screen.dart';
import 'ai_chat_screen.dart';

class HomeScreen extends StatefulWidget {
  static const route = '/home';
  const HomeScreen({super.key, required this.prefs, required this.state});
  final SharedPreferences prefs;
  final AppState state;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  int idx = 0;
  late final ApiClient api;
  late final RealtimeClient rt;
  
  bool _handlingAppBlock = false;
  bool _appUnblockSnackBarShown = false;
  bool _chatBlockedSnackBarShown = false;
  DateTime? _lastAdminMessageSnackBarAt;

  @override
  void initState() {
    super.initState();
    api = ApiClient(baseUrl: kBackendBaseUrl);
    AppRefs.api = api;
    rt = RealtimeClient(baseUrl: kBackendBaseUrl);
    _connectRealtime();
    _loadFavorites();
  }

  Future<void> _loadFavorites() async {
    final cid = widget.state.customerId;
    if (cid == null) return;
    try {
      final favs = await api.getFavorites(cid);
      widget.state.setFavorites(favs);
    } catch (_) {}
  }

  Future<void> _connectRealtime() async {
    final id = widget.state.customerId;
    if (id == null) return;
    await rt.connectCustomer(
      customerId: id,
      onNotification: (n) => widget.state.pushNotification(n),
      onOrderStatus: (p) {},
      onOrderEta: (p) { widget.state.upsertOrderEta(p); },
      onComplaintMessage: (p) {
        widget.state.applyComplaintMessage(p);
        final fromAdmin = p['fromAdmin'] == true;
        final threadId = p['threadId'];
        if (fromAdmin && mounted && widget.state.openComplaintThreadId != threadId) {
          final now = DateTime.now();
          if (_lastAdminMessageSnackBarAt != null && now.difference(_lastAdminMessageSnackBarAt!).inSeconds < 4) return;
          _lastAdminMessageSnackBarAt = now;
          try { SystemSound.play(SystemSoundType.alert); } catch (_) {}
          final msg = (p['message'] ?? '').toString();
          final shortMsg = msg.length > 40 ? (msg.substring(0, 40) + '…') : msg;
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text('رسالة جديدة من الإدارة: $shortMsg'),
              action: SnackBarAction(
                label: 'فتح',
                onPressed: () { setState(() => idx = 3); },
              ),
            ),
          );
        }
      },
      onChatBlocked: (p) {
        final blocked = p['isChatBlocked'] == true;
        if (blocked && mounted && !_chatBlockedSnackBarShown) {
          _chatBlockedSnackBarShown = true;
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('تم إيقاف الدردشة من قبل الإدارة')),
          );
          Future.delayed(const Duration(seconds: 10), () { if (mounted) _chatBlockedSnackBarShown = false; });
        }
      },
      onSettingsUpdated: (s) async {
        widget.state.setConfig(s);
        try { await widget.prefs.setString('cached_settings', jsonEncode(s)); } catch (_) {}
      },
      onNotificationRefresh: () async {
        try {
          final list = await api.listNotifications(id);
          widget.state.setNotifications(list);
        } catch (_) {}
      },
      
      onAppBlocked: (p) async {
        final blocked = p['isAppBlocked'] == true;
        final customerId = p['customerId'];

        if (blocked && customerId == id && mounted) {
          if (_handlingAppBlock) return;
          _handlingAppBlock = true;
          await rt.disconnect();
          if (!mounted) return;
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text('تم إيقاف حسابك من قبل الإدارة'),
              backgroundColor: Colors.red,
              duration: Duration(seconds: 3),
            ),
          );
          await Future.delayed(const Duration(milliseconds: 500));
          if (!mounted) return;
          Navigator.of(context).pushAndRemoveUntil(
            MaterialPageRoute(
              builder: (_) => BlockedScreen(
                prefs: widget.prefs,
                state: widget.state,
              ),
            ),
            (route) => false,
          );
        } else if (!blocked && customerId == id && mounted && !_appUnblockSnackBarShown) {
          _appUnblockSnackBarShown = true;
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text('تم إلغاء إيقاف حسابك، يمكنك الآن استخدام التطبيق'),
              backgroundColor: Colors.green,
              duration: Duration(seconds: 3),
            ),
          );
          Future.delayed(const Duration(seconds: 15), () { if (mounted) _appUnblockSnackBarShown = false; });
        }
      },
      onAccountDeleted: (p) async {
        final deletedId = (p['customerId'] is num) ? (p['customerId'] as num).toInt() : null;
        if (deletedId != id || !mounted) return;
        await rt.disconnect();
        if (!mounted) return;
        widget.state.clearCustomer();
        await widget.prefs.remove('customerId');
        await widget.prefs.remove('customerName');
        await widget.prefs.remove('customerPhone');
        await widget.prefs.remove('defaultLat');
        await widget.prefs.remove('defaultLng');
        await widget.prefs.remove('defaultAddress');
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('تم حذف حسابك من قبل الإدارة'),
            backgroundColor: Colors.red,
            duration: Duration(seconds: 3),
          ),
        );
        await Future.delayed(const Duration(milliseconds: 400));
        if (!mounted) return;
        Navigator.of(context).pushAndRemoveUntil(
          MaterialPageRoute(builder: (_) => AuthScreen(prefs: widget.prefs, state: widget.state)),
          (route) => false,
        );
      },
      onAgentChatMessage: (p) {
        widget.state.onRealtimeAgentChatMessage(p);
        if (mounted) {
          // Only show snackbar if not already inside the agent chat screen
          final incomingThreadId = (p['threadId'] as num?)?.toInt();
          final isOpen = widget.state.openAgentChatThreadId != null &&
              widget.state.openAgentChatThreadId == incomingThreadId;
          if (!isOpen) {
            final msg = (p['message'] ?? '').toString();
            final short = msg.length > 40 ? (msg.substring(0, 40) + '…') : msg;
            try {
              ScaffoldMessenger.of(context).clearSnackBars();
              ScaffoldMessenger.of(context).showSnackBar(
                SnackBar(
                  content: Text('💬 رسالة من المندوب: $short'),
                  duration: const Duration(seconds: 4),
                  behavior: SnackBarBehavior.floating,
                  margin: const EdgeInsets.fromLTRB(12, 0, 12, 80),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                  action: SnackBarAction(
                    label: 'فتح',
                    onPressed: () { setState(() => idx = 3); },
                  ),
                ),
              );
            } catch (_) {}
          }
        }
      },
    );
  }

  @override
  void didUpdateWidget(covariant HomeScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    _connectRealtime();
  }

  @override
  void dispose() {
    rt.disconnect();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.state.isAcceptingOrders) {
      return AnimatedBuilder(
        animation: widget.state,
        builder: (_, __) => Scaffold(
          appBar: AppBar(title: BrandTitle(state: widget.state, suffix: 'مغلق')),
          body: ClosedScreen(state: widget.state),
        ),
      );
    }

    final pages = [
      MenuScreen(api: api, state: widget.state),
      CartScreen(api: api, state: widget.state),
      OrdersScreen(api: api, state: widget.state),
      ComplaintsScreen(api: api, state: widget.state),
      ProfileScreen(state: widget.state, prefs: widget.prefs, api: api),
    ];

    final theme = Theme.of(context);
    final primary = theme.colorScheme.primary;

    return AnimatedBuilder(
      animation: widget.state,
      builder: (_, __) => Scaffold(
        appBar: AppBar(
          title: Align(
            alignment: Alignment.centerRight,
            child: BrandTitle(
              state: widget.state,
              suffix: 'أهلاً ${widget.state.customerName ?? ''}',
              logoSize: 40,
            ),
          ),
          centerTitle: false,
          actions: [
            
            IconButton(
              tooltip: 'المفضلة',
              onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => FavoritesScreen(api: api, state: widget.state))),
              icon: Icon(Icons.favorite_outline, color: theme.appBarTheme.foregroundColor),
            ),
            
            IconButton(
              tooltip: 'المساعد الذكي',
              onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => AiChatScreen(api: api, state: widget.state))),
              icon: Stack(
                children: [
                  Icon(Icons.auto_awesome_rounded, color: theme.appBarTheme.foregroundColor),
                ],
              ),
            ),
            IconButton(
              tooltip: 'الإشعارات',
              onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => NotificationsScreen(api: api, state: widget.state))),
              icon: Stack(
                clipBehavior: Clip.none,
                children: [
                  Icon(Icons.notifications_outlined, color: theme.appBarTheme.foregroundColor),
                  if (widget.state.unreadNotifications > 0)
                    Positioned(
                      right: -2,
                      top: -2,
                      child: Container(
                        padding: const EdgeInsets.all(4),
                        decoration: BoxDecoration(
                          color: theme.colorScheme.error,
                          shape: BoxShape.circle,
                          border: Border.all(color: theme.appBarTheme.backgroundColor ?? Colors.white, width: 1),
                        ),
                        constraints: const BoxConstraints(minWidth: 18, minHeight: 18),
                        child: Center(
                          child: Text(
                            widget.state.unreadNotifications > 99 ? '99+' : '${widget.state.unreadNotifications}',
                            style: theme.textTheme.labelSmall?.copyWith(color: Colors.white, fontSize: 10),
                          ),
                        ),
                      ),
                    )
                ],
              ),
            ),
          ],
        ),
        body: Stack(
          children: [
            pages[idx],
          ],
        ),
        bottomNavigationBar: Container(
          decoration: BoxDecoration(
            color: theme.colorScheme.surface,
            boxShadow: [
              BoxShadow(
                color: Colors.black.withOpacity(0.06),
                blurRadius: 12,
                offset: const Offset(0, -2),
              ),
            ],
          ),
          child: SafeArea(
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 8),
              child: NavigationBar(
                selectedIndex: idx,
                onDestinationSelected: (v) => setState(() => idx = v),
                destinations: [
                  const NavigationDestination(
                    icon: Icon(Icons.store_mall_directory_rounded),
                    selectedIcon: Icon(Icons.store_mall_directory_rounded),
                    label: 'القائمة',
                  ),
                  const NavigationDestination(
                    icon: Icon(Icons.shopping_cart_outlined),
                    selectedIcon: Icon(Icons.shopping_cart_rounded),
                    label: 'السلة',
                  ),
                  const NavigationDestination(
                    icon: Icon(Icons.receipt_long_outlined),
                    selectedIcon: Icon(Icons.receipt_long_rounded),
                    label: 'طلباتي',
                  ),
                  NavigationDestination(
                    icon: Stack(
                      clipBehavior: Clip.none,
                      children: [
                        const Icon(Icons.support_agent_outlined),
                        if (widget.state.unreadComplaints > 0)
                          Positioned(
                            right: -4,
                            top: -4,
                            child: Container(
                              padding: const EdgeInsets.all(4),
                              decoration: BoxDecoration(
                                color: primary,
                                shape: BoxShape.circle,
                              ),
                              constraints: const BoxConstraints(minWidth: 16, minHeight: 16),
                              child: Center(
                                child: Text(
                                  widget.state.unreadComplaints > 99 ? '99+' : '${widget.state.unreadComplaints}',
                                  style: theme.textTheme.labelSmall?.copyWith(color: Colors.white, fontSize: 9),
                                ),
                              ),
                            ),
                          )
                      ],
                    ),
                    selectedIcon: const Icon(Icons.support_agent_rounded),
                    label: 'الدردشة',
                  ),
                  const NavigationDestination(
                    icon: Icon(Icons.person_outline_rounded),
                    selectedIcon: Icon(Icons.person_rounded),
                    label: 'حسابي',
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
