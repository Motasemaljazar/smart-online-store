import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import '../services/agent_api.dart';
import '../models/agent_state.dart';
import '../app_config.dart';
import 'agent_chat_detail_screen.dart';

class AgentChatsScreen extends StatefulWidget {
  const AgentChatsScreen({super.key, required this.state});
  final AgentState state;

  @override
  State<AgentChatsScreen> createState() => _AgentChatsScreenState();
}

class _AgentChatsScreenState extends State<AgentChatsScreen> {
  late final AgentApi _api;
  bool _loading = true;
  String? _error;
  List<Map<String, dynamic>> _threads = [];

  @override
  void initState() {
    super.initState();
    _api = AgentApi(baseUrl: kBackendBaseUrl);
    widget.state.addListener(_onStateChange);
    _load();
  }

  void _onStateChange() {
    if (!mounted) return;
    
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    setState(() { _loading = true; _error = null; });
    try {
      final threads = await _api.getChatThreads(widget.state.token ?? '');
      _threads = threads.cast<Map<String, dynamic>>();
    } catch (e) {
      _error = e.toString();
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  void dispose() {
    widget.state.removeListener(_onStateChange);
    super.dispose();
  }

  String _formatTime(String? iso) {
    if (iso == null || iso.isEmpty) return '';
    try {
      final dt = DateTime.parse(iso).toLocal();
      final now = DateTime.now();
      final today = DateTime(now.year, now.month, now.day);
      final msgDay = DateTime(dt.year, dt.month, dt.day);
      final timeStr = '${dt.hour.toString().padLeft(2, '0')}:${dt.minute.toString().padLeft(2, '0')}';
      if (msgDay == today) return timeStr;
      if (msgDay == today.subtract(const Duration(days: 1))) return 'أمس';
      return '${dt.day}/${dt.month}';
    } catch (_) {}
    return '';
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    if (_loading) {
      return Center(child: CircularProgressIndicator(color: cs.primary));
    }

    if (_error != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.error_outline_rounded, size: 48, color: cs.error),
              const SizedBox(height: 12),
              Text(_error!, textAlign: TextAlign.center),
              const SizedBox(height: 12),
              FilledButton.icon(onPressed: _load, icon: const Icon(Icons.refresh), label: const Text('إعادة المحاولة')),
            ],
          ),
        ),
      );
    }

    if (_threads.isEmpty) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.chat_bubble_outline_rounded, size: 64, color: cs.primary.withOpacity(0.4)),
            const SizedBox(height: 16),
            Text('لا توجد محادثات بعد', style: GoogleFonts.cairo(fontSize: 18, fontWeight: FontWeight.w700, color: cs.onSurfaceVariant)),
            const SizedBox(height: 8),
            Text('ستظهر هنا محادثات العملاء الذين يتواصلون معك', style: GoogleFonts.cairo(color: cs.onSurfaceVariant), textAlign: TextAlign.center),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.separated(
        padding: const EdgeInsets.symmetric(vertical: 8),
        itemCount: _threads.length,
        separatorBuilder: (_, __) => const Divider(height: 1, indent: 76),
        itemBuilder: (ctx, i) {
          final t = _threads[i];
          final threadId = t['id'] as int? ?? 0;
          final customerName = (t['customerName'] ?? t['name'] ?? 'عميل').toString();
          final lastMsg = (t['lastMessagePreview'] ?? t['lastMessage'] ?? '').toString();
          final unread = (t['unreadCount'] ?? 0) as int;
          final timeStr = _formatTime(t['lastMessageAt']?.toString() ?? t['updatedAt']?.toString());

          return ListTile(
            onTap: () {
              Navigator.push(
                context,
                MaterialPageRoute(
                  builder: (_) => AgentChatDetailScreen(
                    state: widget.state,
                    threadId: threadId,
                    customerName: customerName,
                  ),
                ),
              ).then((_) => _load());
            },
            contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            leading: CircleAvatar(
              radius: 26,
              backgroundColor: cs.primaryContainer,
              child: Text(
                customerName.isNotEmpty ? customerName[0] : '?',
                style: GoogleFonts.cairo(
                  fontSize: 20,
                  fontWeight: FontWeight.w800,
                  color: cs.onPrimaryContainer,
                ),
              ),
            ),
            title: Row(
              children: [
                Expanded(
                  child: Text(
                    customerName,
                    style: GoogleFonts.cairo(
                      fontWeight: unread > 0 ? FontWeight.w800 : FontWeight.w600,
                      fontSize: 15,
                    ),
                  ),
                ),
                if (timeStr.isNotEmpty)
                  Text(
                    timeStr,
                    style: GoogleFonts.cairo(
                      fontSize: 12,
                      color: unread > 0 ? cs.primary : cs.onSurfaceVariant,
                      fontWeight: unread > 0 ? FontWeight.w700 : FontWeight.normal,
                    ),
                  ),
              ],
            ),
            subtitle: Row(
              children: [
                Expanded(
                  child: Text(
                    lastMsg.isNotEmpty ? lastMsg : 'لا توجد رسائل',
                    style: GoogleFonts.cairo(
                      color: unread > 0 ? cs.onSurface : cs.onSurfaceVariant,
                      fontWeight: unread > 0 ? FontWeight.w600 : FontWeight.normal,
                      fontSize: 13,
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                if (unread > 0)
                  Container(
                    margin: const EdgeInsets.only(right: 8),
                    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                    decoration: BoxDecoration(
                      color: cs.primary,
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: Text(
                      '$unread',
                      style: GoogleFonts.cairo(color: Colors.white, fontSize: 12, fontWeight: FontWeight.w700),
                    ),
                  ),
              ],
            ),
          );
        },
      ),
    );
  }
}
