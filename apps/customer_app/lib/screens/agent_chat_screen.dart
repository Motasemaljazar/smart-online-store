import 'dart:async';
import 'package:flutter/material.dart';
import '../models/app_state.dart';
import '../services/api.dart';

class AgentChatScreen extends StatefulWidget {
  const AgentChatScreen({
    super.key,
    required this.api,
    required this.state,
    required this.agentId,
    required this.agentName,
    required this.productName,
  });

  final ApiClient api;
  final AppState state;
  final int agentId;
  final String agentName;
  final String productName;

  @override
  State<AgentChatScreen> createState() => _AgentChatScreenState();
}

class _AgentChatScreenState extends State<AgentChatScreen> {
  bool _loading = true;
  String? _error;
  int? _threadId;
  List<Map<String, dynamic>> _messages = [];
  final _msgCtl = TextEditingController();
  final _scroll = ScrollController();
  Timer? _pollTimer;

  @override
  void initState() {
    super.initState();
    widget.state.addListener(_onStateChange);
    _initChat();
  }
  
  void _setOpenThread(int? id) {
    widget.state.setOpenAgentChatThread(id);
  }

  void _onStateChange() {
    if (!mounted) return;
    final msg = widget.state.lastAgentChatMessage;
    if (msg == null) return;
    final tid = (msg['threadId'] as num?)?.toInt();
    if (tid != _threadId) return;
    
    final incomingId = (msg['id'] as num?)?.toInt() ?? 0;
    
    if (incomingId > 0 && _messages.any((m) => (m['id'] as num?)?.toInt() == incomingId)) return;
    setState(() {
      _messages = [
        ..._messages,
        {
          'id': incomingId,
          'fromAgent': msg['fromAgent'] == true,
          'message': (msg['message'] ?? '').toString(),
          'createdAtUtc': msg['createdAtUtc'] ?? DateTime.now().toUtc().toIso8601String(),
        }
      ];
    });
    _scrollToBottom();
  }

  Future<void> _initChat() async {
    setState(() { _loading = true; _error = null; });
    try {
      final customerId = widget.state.customerId;
      if (customerId == null) throw Exception('يجب تسجيل الدخول أولاً');
      final result = await widget.api.getOrCreateAgentChatThread(
        customerId: customerId,
        agentId: widget.agentId,
      );
      _threadId = (result['threadId'] as num).toInt();
      _setOpenThread(_threadId);
      await _loadMessages();
      
      _pollTimer = Timer.periodic(const Duration(seconds: 5), (_) => _loadMessages());
    } catch (e) {
      _error = e.toString();
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _loadMessages() async {
    if (_threadId == null) return;
    try {
      final thread = await widget.api.getAgentChatThread(
        _threadId!,
        customerId: widget.state.customerId,
      );
      final rawMsgs = ((thread['messages'] ?? thread['Messages']) as List<dynamic>? ?? []);
      final msgs = rawMsgs.map((e) {
        final m = Map<String, dynamic>.from(e as Map);
        // Normalize fromAgent field (backend may return FromAgent or fromAgent)
        if (!m.containsKey('fromAgent') && m.containsKey('FromAgent')) {
          m['fromAgent'] = m['FromAgent'];
        }
        if (!m.containsKey('message') && m.containsKey('Message')) {
          m['message'] = m['Message'];
        }
        if (!m.containsKey('createdAtUtc') && m.containsKey('CreatedAtUtc')) {
          m['createdAtUtc'] = m['CreatedAtUtc'];
        }
        return m;
      }).toList().cast<Map<String, dynamic>>();
      if (mounted) {
        setState(() => _messages = msgs);
        _scrollToBottom();
      }
    } catch (_) {}
  }

  Future<void> _send() async {
    final txt = _msgCtl.text.trim();
    if (txt.isEmpty || _threadId == null) return;
    _msgCtl.clear();
    try {
      await widget.api.sendAgentChatMessage(
        threadId: _threadId!,
        customerId: widget.state.customerId!,
        message: txt,
      );
      await _loadMessages();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('فشل الإرسال: $e')),
        );
        _msgCtl.text = txt;
      }
    }
  }

  Future<void> _scrollToBottom() async {
    await Future.delayed(const Duration(milliseconds: 80));
    if (!mounted || !_scroll.hasClients) return;
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
    _setOpenThread(null);
    widget.state.removeListener(_onStateChange);
    _pollTimer?.cancel();
    _msgCtl.dispose();
    _scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cs = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Column(
          children: [
            Text(
              'تواصل مع المندوب',
              style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800),
            ),
            Text(
              widget.productName,
              style: theme.textTheme.bodySmall?.copyWith(color: cs.onSurfaceVariant),
            ),
          ],
        ),
        leading: IconButton(
          icon: const Icon(Icons.arrow_back_rounded),
          onPressed: () => Navigator.pop(context),
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh_rounded),
            onPressed: _loadMessages,
          ),
        ],
      ),
      body: _loading
          ? Center(child: CircularProgressIndicator(color: cs.primary))
          : _error != null
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(24),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(Icons.error_outline_rounded, size: 48, color: cs.error),
                        const SizedBox(height: 12),
                        Text(_error!, textAlign: TextAlign.center),
                        const SizedBox(height: 12),
                        FilledButton(onPressed: _initChat, child: const Text('إعادة المحاولة')),
                      ],
                    ),
                  ),
                )
              : Column(
                  children: [
                    
                    Container(
                      width: double.infinity,
                      margin: const EdgeInsets.all(12),
                      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                      decoration: BoxDecoration(
                        color: cs.primaryContainer.withOpacity(0.6),
                        borderRadius: BorderRadius.circular(12),
                        border: Border.all(color: cs.primary.withOpacity(0.2)),
                      ),
                      child: Row(
                        children: [
                          Icon(Icons.inventory_2_rounded, size: 18, color: cs.primary),
                          const SizedBox(width: 10),
                          Expanded(
                            child: Text(
                              'منتج: ${widget.productName}',
                              style: theme.textTheme.bodySmall?.copyWith(
                                color: cs.onPrimaryContainer,
                                fontWeight: FontWeight.w600,
                              ),
                              textDirection: TextDirection.rtl,
                            ),
                          ),
                        ],
                      ),
                    ),

                    Expanded(
                      child: _messages.isEmpty
                          ? Center(
                              child: Column(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Container(
                                    padding: const EdgeInsets.all(20),
                                    decoration: BoxDecoration(
                                      color: cs.surfaceContainerHighest.withOpacity(0.6),
                                      shape: BoxShape.circle,
                                    ),
                                    child: Icon(Icons.chat_bubble_outline_rounded, size: 56, color: cs.primary.withOpacity(0.7)),
                                  ),
                                  const SizedBox(height: 20),
                                  Text(
                                    'ابدأ المحادثة مع المندوب',
                                    style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700),
                                  ),
                                  const SizedBox(height: 8),
                                  Text(
                                    'اسأل عن المنتج أو تواصل مع المندوب مباشرة',
                                    textAlign: TextAlign.center,
                                    style: theme.textTheme.bodyMedium?.copyWith(color: cs.onSurfaceVariant),
                                  ),
                                ],
                              ),
                            )
                          : ListView.builder(
                              controller: _scroll,
                              padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
                              itemCount: _messages.length,
                              itemBuilder: (ctx, i) {
                                final m = _messages[i];
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
                                            color: cs.secondaryContainer,
                                            shape: BoxShape.circle,
                                          ),
                                          child: Icon(Icons.badge_rounded, size: 18, color: cs.onSecondaryContainer),
                                        ),
                                      Flexible(
                                        child: Container(
                                          constraints: BoxConstraints(maxWidth: MediaQuery.sizeOf(context).width * 0.75),
                                          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                                          decoration: BoxDecoration(
                                            color: fromAgent ? cs.secondaryContainer : cs.primaryContainer,
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
                                                style: theme.textTheme.bodyLarge?.copyWith(
                                                  color: fromAgent ? cs.onSecondaryContainer : cs.onPrimaryContainer,
                                                  height: 1.4,
                                                ),
                                              ),
                                              if (timeStr.isNotEmpty) ...[
                                                const SizedBox(height: 4),
                                                Text(
                                                  timeStr,
                                                  style: theme.textTheme.labelSmall?.copyWith(
                                                    color: (fromAgent ? cs.onSecondaryContainer : cs.onPrimaryContainer).withOpacity(0.7),
                                                    fontSize: 11,
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
                            ),
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
                                hintText: 'اكتب رسالتك للمندوب…',
                                hintStyle: TextStyle(color: cs.onSurfaceVariant.withOpacity(0.7)),
                                filled: true,
                                fillColor: cs.surfaceContainerHighest.withOpacity(0.6),
                                contentPadding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
                                border: OutlineInputBorder(borderRadius: BorderRadius.circular(24), borderSide: BorderSide.none),
                                enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(24), borderSide: BorderSide(color: cs.outlineVariant.withOpacity(0.4))),
                                focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(24), borderSide: BorderSide(color: cs.primary, width: 1.5)),
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
                                boxShadow: [BoxShadow(color: cs.primary.withOpacity(0.35), blurRadius: 8, offset: const Offset(0, 2))],
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
