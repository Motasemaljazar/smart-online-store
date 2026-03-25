import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import '../models/agent_state.dart';
import '../services/agent_realtime.dart';
import '../app_config.dart';
import 'agent_products_screen.dart';
import 'agent_chats_screen.dart';
import 'agent_profile_screen.dart';
import 'agent_incoming_orders_screen.dart';
import 'agent_orders_screen.dart';
import 'agent_reports_screen.dart';
// agent_product_ratings_screen removed

class AgentHomeScreen extends StatefulWidget {
  const AgentHomeScreen({super.key, required this.state, required this.api});
  final AgentState state;
  final dynamic api;

  @override
  State<AgentHomeScreen> createState() => _AgentHomeScreenState();
}

class _AgentHomeScreenState extends State<AgentHomeScreen> {
  AgentRealtimeClient? _rt;

  int _selectedIndex = 0;

  final List<_NavPage> _pages = const [
    _NavPage(label: 'الطلبات الواردة', icon: Icons.inbox_outlined, selectedIcon: Icons.inbox_rounded),
    _NavPage(label: 'كل الطلبات', icon: Icons.list_alt_outlined, selectedIcon: Icons.list_alt_rounded),
    _NavPage(label: 'منتجاتي', icon: Icons.inventory_2_outlined, selectedIcon: Icons.inventory_2_rounded),
    _NavPage(label: 'تقاريري', icon: Icons.bar_chart_outlined, selectedIcon: Icons.bar_chart_rounded),
    _NavPage(label: 'الدردشة', icon: Icons.chat_bubble_outline_rounded, selectedIcon: Icons.chat_bubble_rounded),
    _NavPage(label: 'حسابي', icon: Icons.person_outline_rounded, selectedIcon: Icons.person_rounded),
  ];

  String _title() {
    switch (_selectedIndex) {
      case 0: return 'الطلبات الواردة';
      case 1: return 'إدارة الطلبات';
      case 2: return 'منتجاتي';
      case 3: return 'تقاريري';
      case 4: return 'دردشة العملاء';
      case 5: return 'حسابي';
      default: return 'تطبيق المندوب';
    }
  }

  Widget _buildPage() {
    switch (_selectedIndex) {
      case 0: return AgentIncomingOrdersScreen(api: widget.api, state: widget.state);
      case 1: return AgentOrdersScreen(api: widget.api, state: widget.state);
      case 2: return AgentProductsScreen(state: widget.state);
      case 3: return AgentReportsScreen(state: widget.state);
      case 4: return AgentChatsScreen(state: widget.state);
      case 5: return AgentProfileScreen(state: widget.state);
      default: return AgentIncomingOrdersScreen(api: widget.api, state: widget.state);
    }
  }

  @override
  void initState() {
    super.initState();
    _connectRealtime();
  }

  void _connectRealtime() {
    final agentId = widget.state.agentId;
    if (agentId == null) return;
    _rt = AgentRealtimeClient(baseUrl: kBackendBaseUrl);
    _rt!.connect(
      agentId: agentId,
      onChatMessage: (payload) {
        widget.state.onRealtimeChatMessage(payload);
      },
      onOrderStatus: (payload) {
        widget.state.onRealtimeOrderStatus(payload);
      },
      onOrderAssigned: (payload) {
        widget.state.onRealtimeOrderStatus(payload);
      },
    ).catchError((_) {});
  }

  @override
  void dispose() {
    _rt?.disconnect();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return AnimatedBuilder(
      animation: widget.state,
      builder: (ctx, _) {
        return Scaffold(
          appBar: AppBar(
            title: Text(
              _title(),
              style: GoogleFonts.cairo(fontWeight: FontWeight.w800, fontSize: 20),
            ),
            centerTitle: true,
            actions: [
              if (_selectedIndex == 0)
                IconButton(
                  icon: const Icon(Icons.refresh_rounded),
                  onPressed: () {
                    
                    setState(() {});
                  },
                ),
            ],
          ),
          body: _buildPage(),
          bottomNavigationBar: NavigationBar(
            selectedIndex: _selectedIndex,
            onDestinationSelected: (i) => setState(() => _selectedIndex = i),
            destinations: _pages.map((p) {
              final unread = p.label == 'الدردشة' ? widget.state.chatMessageSeq : 0;
              return NavigationDestination(
                icon: Badge(
                  isLabelVisible: unread > 0,
                  child: Icon(p.icon),
                ),
                selectedIcon: Badge(
                  isLabelVisible: unread > 0,
                  child: Icon(p.selectedIcon),
                ),
                label: p.label,
              );
            }).toList(),
          ),
        );
      },
    );
  }
}

class _NavPage {
  const _NavPage({required this.label, required this.icon, required this.selectedIcon});
  final String label;
  final IconData icon;
  final IconData selectedIcon;
}
