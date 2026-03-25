import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'models/agent_state.dart';
import 'theme/agent_theme.dart';
import 'screens/agent_login_screen.dart';
import 'screens/agent_home_screen.dart';
import 'services/agent_api.dart';
import 'app_config.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  SystemChrome.setSystemUIOverlayStyle(
    const SystemUiOverlayStyle(
      statusBarColor: Colors.transparent,
      statusBarIconBrightness: Brightness.dark,
    ),
  );
  final prefs = await SharedPreferences.getInstance();
  runApp(AgentApp(prefs: prefs));
}

class AgentApp extends StatefulWidget {
  const AgentApp({super.key, required this.prefs});
  final SharedPreferences prefs;

  @override
  State<AgentApp> createState() => _AgentAppState();
}

class _AgentAppState extends State<AgentApp> {
  late final AgentState _state;
  late final AgentApi _api;
  bool _ready = false;

  @override
  void initState() {
    super.initState();
    _state = AgentState();
    _api = AgentApi(baseUrl: kBackendBaseUrl);
    _state.init(widget.prefs).then((_) {
      if (mounted) setState(() => _ready = true);
    });
  }

  @override
  Widget build(BuildContext context) {
    if (!_ready) {
      return const MaterialApp(
        debugShowCheckedModeBanner: false,
        home: Scaffold(body: Center(child: CircularProgressIndicator())),
      );
    }

    return AnimatedBuilder(
      animation: _state,
      builder: (ctx, _) {
        return MaterialApp(
          debugShowCheckedModeBanner: false,
          title: 'تطبيق المندوب',
          locale: const Locale('ar'),
          theme: AgentTheme.lightTheme(),
          darkTheme: AgentTheme.darkTheme(),
          themeMode: _state.isDarkMode ? ThemeMode.dark : ThemeMode.light,
          builder: (context, child) {
            return Directionality(
              textDirection: TextDirection.rtl,
              child: child ?? const SizedBox(),
            );
          },
          home: _state.isLoggedIn
              ? AgentHomeScreen(state: _state, api: _api)
              : AgentLoginScreen(state: _state),
        );
      },
    );
  }
}
