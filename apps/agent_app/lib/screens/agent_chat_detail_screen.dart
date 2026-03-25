import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import '../services/agent_api.dart';
import '../models/agent_state.dart';
import '../app_config.dart';

class AgentChatDetailScreen extends StatefulWidget {
  const AgentChatDetailScreen({
    super.key,
    required this.state,
    required this.threadId,
    required this.customerName,
  });
  final AgentState state;
  final int threadId;
  final String customerName;

  @override
  State<AgentChatDetailScreen> createState() => _AgentChatDetailScreenState();
}

class _AgentChatDetailScreenState extends State<AgentChatDetailScreen> {
  late final AgentApi _api;
  bool _loading = true;
  String? _error;
  Map<String, dynamic>? _thread;
  final _msgCtl = TextEditingController();
  final _scroll = ScrollController();
  final Set<int> _seenIds = {};

  @override
  void initState() {
    super.initState();
    _api = AgentApi(baseUrl: kBackendBaseUrl);
    widget.state.addListener(_onStateChange);
    _load();
  }

  void _onStateChange() {
    if (!mounted) return;
    final msg = widget.state.lastChatMessage;
    if (msg == null) return;
    final tid = msg['threadId'];
    if (tid != widget.threadId) return;

    final msgs = ((_thread?['messages'] as List<dynamic>?) ?? [])
        .cast<Map<String, dynamic>>()
        .toList();

    final incomingId = (msg['id'] as num?)?.toInt() ?? 0;
    if (incomingId > 0 && _seenIds.contains(incomingId)) return;
    if (incomingId > 0) _seenIds.add(incomingId);

    msgs.add({
      'id': incomingId,
      'fromAgent': msg['fromAgent'] == true,
      'message': (msg['message'] ?? '').toString(),
      'createdAtUtc': msg['createdAtUtc'] ?? DateTime.now().toUtc().toIso8601String(),
    });
    setState(() => _thread = {...(_thread ?? {}), 'messages': msgs});
    _scrollToBottom();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final thread = await _api.getChatThread(widget.state.token ?? '', widget.threadId);
      _thread = thread;
      _seenIds
        ..clear()
        ..addAll(((_thread?['messages'] as List<dynamic>? ?? [])
            .map((e) => (e as Map)['id'])
            .where((x) => x is int)
            .cast<int>()));
    } catch (e) {
      _error = e.toString();
    } finally {
      if (mounted) setState(() => _loading = false);
      _scrollToBottom();
    }
  }

  Future<void> _send() async {
    final txt = _msgCtl.text.trim();
    if (txt.isEmpty) return;
    _msgCtl.clear();
    try {
      await _api.sendMessage(widget.state.token ?? '', widget.threadId, txt);
      
      final msgs = ((_thread?['messages'] as List<dynamic>?) ?? [])
          .cast<Map<String, dynamic>>()
          .toList();
      msgs.add({
        'id': 0,
        'fromAgent': true,
        'message': txt,
        'createdAtUtc': DateTime.now().toUtc().toIso8601String(),
      });
      setState(() => _thread = {...(_thread ?? {}), 'messages': msgs});
      _scrollToBottom();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('فشل الإرسال: $e')));
        _msgCtl.text = txt;
      }
    }
  }

  Future<void> _scrollToBottom() async {
    await Future.delayed(const Duration(milliseconds: 80));
    if (!mounted) return;
    if (!_scroll.hasClients) return;
    _scroll.animateTo(
      _scroll.position.maxScrollExtent,
      duration: const Duration(milliseconds: 200),
      curve: Curves.easeOut,
    );
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
      if (msgDay == today.subtract(const Duration(days: 1))) return 'أمس $timeStr';
      return '${dt.day}/${dt.month} $timeStr';
    } catch (_) {}
    return '';
  }

  @override
  void dispose() {
    widget.state.removeListener(_onStateChange);
    _msgCtl.dispose();
    _scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Column(
          children: [
            Text(widget.customerName, style: GoogleFonts.cairo(fontWeight: FontWeight.w800, fontSize: 17)),
            Text('عميل', style: GoogleFonts.cairo(fontSize: 12, color: cs.onSurfaceVariant)),
          ],
        ),
        leading: IconButton(
          icon: const Icon(Icons.arrow_back_rounded),
          onPressed: () => Navigator.pop(context),
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh_rounded),
            onPressed: _load,
          ),
        ],
      ),
      body: _loading
          ? Center(child: CircularProgressIndicator(color: cs.primary))
          : _error != null
              ? Center(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(Icons.error_outline_rounded, size: 48, color: cs.error),
                      const SizedBox(height: 12),
                      Text(_error!, textAlign: TextAlign.center),
                      const SizedBox(height: 12),
                      FilledButton(onPressed: _load, child: const Text('إعادة المحاولة')),
                    ],
                  ),
                )
              : Column(
                  children: [
                    
                    Expanded(
                      child: Builder(builder: (ctx) {
                        final msgs = ((_thread?['messages'] as List<dynamic>?) ?? [])
                            .cast<Map<String, dynamic>>();
                        if (msgs.isEmpty) {
                          return Center(
                            child: Column(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                Icon(Icons.chat_bubble_outline_rounded, size: 64, color: cs.primary.withOpacity(0.4)),
                                const SizedBox(height: 16),
                                Text('لا توجد رسائل بعد', style: GoogleFonts.cairo(color: cs.onSurfaceVariant, fontSize: 16)),
                              ],
                            ),
                          );
                        }
                        return ListView.builder(
                          controller: _scroll,
                          padding: const EdgeInsets.fromLTRB(16, 16, 16, 12),
                          itemCount: msgs.length,
                          itemBuilder: (ctx, i) {
                            final m = msgs[i];
                            final fromAgent = m['fromAgent'] == true;
                            final text = (m['message'] ?? '').toString();
                            final timeStr = _formatTime(m['createdAtUtc']?.toString());

                            return Padding(
                              padding: const EdgeInsets.only(bottom: 12),
                              child: Row(
                                mainAxisAlignment: fromAgent ? MainAxisAlignment.start : MainAxisAlignment.end,
                                crossAxisAlignment: CrossAxisAlignment.end,
                                children: [
                                  if (fromAgent)
                                    Container(
                                      width: 32, height: 32,
                                      margin: const EdgeInsets.only(left: 8),
                                      decoration: BoxDecoration(
                                        color: cs.primaryContainer,
                                        shape: BoxShape.circle,
                                      ),
                                      child: Icon(Icons.badge_rounded, size: 18, color: cs.onPrimaryContainer),
                                    ),
                                  Flexible(
                                    child: Container(
                                      constraints: BoxConstraints(maxWidth: MediaQuery.sizeOf(context).width * 0.75),
                                      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                                      decoration: BoxDecoration(
                                        color: fromAgent ? cs.primaryContainer : cs.surfaceContainerHighest,
                                        borderRadius: BorderRadius.only(
                                          topLeft: const Radius.circular(18),
                                          topRight: const Radius.circular(18),
                                          bottomLeft: Radius.circular(fromAgent ? 4 : 18),
                                          bottomRight: Radius.circular(fromAgent ? 18 : 4),
                                        ),
                                        boxShadow: [
                                          BoxShadow(color: Colors.black.withOpacity(0.05), blurRadius: 6, offset: const Offset(0, 2)),
                                        ],
                                      ),
                                      child: Column(
                                        crossAxisAlignment: CrossAxisAlignment.start,
                                        mainAxisSize: MainAxisSize.min,
                                        children: [
                                          Text(
                                            text,
                                            textDirection: TextDirection.rtl,
                                            style: GoogleFonts.cairo(
                                              fontSize: 14,
                                              color: fromAgent ? cs.onPrimaryContainer : cs.onSurface,
                                              height: 1.4,
                                            ),
                                          ),
                                          if (timeStr.isNotEmpty) ...[
                                            const SizedBox(height: 4),
                                            Text(
                                              timeStr,
                                              style: GoogleFonts.cairo(
                                                fontSize: 11,
                                                color: (fromAgent ? cs.onPrimaryContainer : cs.onSurfaceVariant).withOpacity(0.7),
                                              ),
                                            ),
                                          ],
                                        ],
                                      ),
                                    ),
                                  ),
                                  if (!fromAgent)
                                    Container(
                                      width: 32, height: 32,
                                      margin: const EdgeInsets.only(right: 8),
                                      decoration: BoxDecoration(
                                        color: cs.surfaceContainerHighest,
                                        shape: BoxShape.circle,
                                      ),
                                      child: Icon(Icons.person_rounded, size: 18, color: cs.onSurfaceVariant),
                                    ),
                                ],
                              ),
                            );
                          },
                        );
                      }),
                    ),

                    Container(
                      padding: EdgeInsets.fromLTRB(16, 12, 16, 12 + MediaQuery.paddingOf(context).bottom),
                      decoration: BoxDecoration(
                        color: cs.surface,
                        boxShadow: [
                          BoxShadow(color: cs.shadow.withOpacity(0.06), blurRadius: 8, offset: const Offset(0, -2)),
                        ],
                      ),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          Expanded(
                            child: TextField(
                              controller: _msgCtl,
                              textDirection: TextDirection.rtl,
                              minLines: 1,
                              maxLines: 4,
                              decoration: InputDecoration(
                                hintText: 'اكتب ردك…',
                                hintStyle: GoogleFonts.cairo(color: cs.onSurfaceVariant.withOpacity(0.7)),
                                filled: true,
                                fillColor: cs.surfaceContainerHighest.withOpacity(0.6),
                                contentPadding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
                                border: OutlineInputBorder(
                                  borderRadius: BorderRadius.circular(24),
                                  borderSide: BorderSide.none,
                                ),
                                enabledBorder: OutlineInputBorder(
                                  borderRadius: BorderRadius.circular(24),
                                  borderSide: BorderSide(color: cs.outlineVariant.withOpacity(0.4)),
                                ),
                                focusedBorder: OutlineInputBorder(
                                  borderRadius: BorderRadius.circular(24),
                                  borderSide: BorderSide(color: cs.primary, width: 1.5),
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(width: 10),
                          InkWell(
                            onTap: _send,
                            borderRadius: BorderRadius.circular(16),
                            child: Container(
                              height: 48, width: 48,
                              decoration: BoxDecoration(
                                color: cs.primary,
                                borderRadius: BorderRadius.circular(16),
                                boxShadow: [
                                  BoxShadow(color: cs.primary.withOpacity(0.35), blurRadius: 8, offset: const Offset(0, 2)),
                                ],
                              ),
                              child: const Icon(Icons.send_rounded, color: Colors.white, size: 22),
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
