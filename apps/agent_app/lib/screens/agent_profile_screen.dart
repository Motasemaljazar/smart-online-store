import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import '../models/agent_state.dart';
import 'agent_login_screen.dart';

class AgentProfileScreen extends StatelessWidget {
  const AgentProfileScreen({super.key, required this.state});
  final AgentState state;

  Future<void> _logout(BuildContext context) async {
    final confirm = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('تسجيل الخروج', style: GoogleFonts.cairo(fontWeight: FontWeight.w700)),
        content: Text('هل تريد تسجيل الخروج؟', style: GoogleFonts.cairo()),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: Text('إلغاء', style: GoogleFonts.cairo()),
          ),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: Colors.red),
            onPressed: () => Navigator.pop(ctx, true),
            child: Text('خروج', style: GoogleFonts.cairo()),
          ),
        ],
      ),
    );
    if (confirm != true || !context.mounted) return;
    await state.logout();
    Navigator.of(context).pushAndRemoveUntil(
      MaterialPageRoute(builder: (_) => AgentLoginScreen(state: state)),
      (_) => false,
    );
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return AnimatedBuilder(
      animation: state,
      builder: (context, _) {
        return ListView(
          padding: const EdgeInsets.all(20),
          children: [
            
            Center(
              child: Column(
                children: [
                  CircleAvatar(
                    radius: 48,
                    backgroundColor: cs.primaryContainer,
                    child: Text(
                      state.agentName.isNotEmpty ? state.agentName[0] : 'م',
                      style: GoogleFonts.cairo(
                        fontSize: 40,
                        fontWeight: FontWeight.w800,
                        color: cs.onPrimaryContainer,
                      ),
                    ),
                  ),
                  const SizedBox(height: 16),
                  Text(
                    state.agentName.isNotEmpty ? state.agentName : 'المندوب',
                    style: GoogleFonts.cairo(fontSize: 22, fontWeight: FontWeight.w800),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    state.agentPhone,
                    style: GoogleFonts.cairo(color: cs.onSurfaceVariant, fontSize: 14),
                  ),
                ],
              ),
            ),

            const SizedBox(height: 32),

            _SettingsCard(
              children: [
                _SettingsTile(
                  icon: Icons.dark_mode_rounded,
                  title: 'الوضع الداكن',
                  trailing: Switch.adaptive(
                    value: state.isDarkMode,
                    onChanged: (_) => state.toggleDarkMode(),
                    activeColor: cs.primary,
                  ),
                ),
              ],
            ),

            const SizedBox(height: 12),

            _SettingsCard(
              children: [
                _SettingsTile(
                  icon: Icons.info_outline_rounded,
                  title: 'الإصدار',
                  trailing: Text('1.0.0', style: GoogleFonts.cairo(color: cs.onSurfaceVariant)),
                ),
              ],
            ),

            const SizedBox(height: 24),

            OutlinedButton.icon(
              onPressed: () => _logout(context),
              icon: const Icon(Icons.logout_rounded, color: Colors.red),
              label: Text('تسجيل الخروج', style: GoogleFonts.cairo(color: Colors.red, fontWeight: FontWeight.w700)),
              style: OutlinedButton.styleFrom(
                minimumSize: const Size.fromHeight(52),
                side: const BorderSide(color: Colors.red),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
              ),
            ),
          ],
        );
      },
    );
  }
}

class _SettingsCard extends StatelessWidget {
  const _SettingsCard({required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return Container(
      decoration: BoxDecoration(
        color: cs.surfaceContainerLow,
        borderRadius: BorderRadius.circular(16),
        boxShadow: [
          BoxShadow(color: Colors.black.withOpacity(0.05), blurRadius: 8, offset: const Offset(0, 2)),
        ],
      ),
      child: Column(children: children),
    );
  }
}

class _SettingsTile extends StatelessWidget {
  const _SettingsTile({
    required this.icon,
    required this.title,
    this.trailing,
    this.onTap,
  });
  final IconData icon;
  final String title;
  final Widget? trailing;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return ListTile(
      onTap: onTap,
      leading: Container(
        width: 40, height: 40,
        decoration: BoxDecoration(
          color: cs.primaryContainer.withOpacity(0.5),
          borderRadius: BorderRadius.circular(10),
        ),
        child: Icon(icon, color: cs.primary, size: 22),
      ),
      title: Text(title, style: GoogleFonts.cairo(fontWeight: FontWeight.w600, fontSize: 14)),
      trailing: trailing,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
    );
  }
}
