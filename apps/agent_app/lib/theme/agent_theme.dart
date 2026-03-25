import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

class AgentTheme {
  static const Color primaryGreen     = Color(0xFF0A3D2E);  
  static const Color primaryGreenDark = Color(0xFF051F17);
  static const Color accentGold      = Color(0xFFD4AF37);  

  static TextTheme get cairoTextTheme =>
      GoogleFonts.cairoTextTheme(ThemeData.light().textTheme);

  static TextTheme get cairoTextThemeDark =>
      GoogleFonts.cairoTextTheme(ThemeData.dark().textTheme);

  static ThemeData lightTheme() {
    const primary = primaryGreen;
    final colorScheme = ColorScheme.fromSeed(
      seedColor: primary,
      brightness: Brightness.light,
      primary: primary,
      secondary: accentGold,
      surface: Colors.white,
      onSurface: const Color(0xFF0A1A14),
      onSurfaceVariant: const Color(0xFF3D6455),
      outline: const Color(0xFFD4E8DF),
      primaryContainer: const Color(0xFFDFF5EC),
      onPrimaryContainer: primary,
    );
    return ThemeData(
      useMaterial3: true,
      colorScheme: colorScheme,
      textTheme: cairoTextTheme,
      fontFamily: 'Cairo',
      scaffoldBackgroundColor: const Color(0xFFF2FAF6),
      appBarTheme: AppBarTheme(
        backgroundColor: Colors.white,
        foregroundColor: const Color(0xFF0A1A14),
        elevation: 0,
        centerTitle: true,
        scrolledUnderElevation: 2,
        shadowColor: const Color(0xFF0A3D2E).withOpacity(0.10),
        titleTextStyle: GoogleFonts.cairo(
          fontSize: 20, fontWeight: FontWeight.w700, color: const Color(0xFF0A1A14),
        ),
        iconTheme: const IconThemeData(color: Color(0xFF0A1A14), size: 24),
      ),
      cardTheme: CardTheme(
        elevation: 0,
        color: Colors.white,
        surfaceTintColor: Colors.transparent,
        shadowColor: const Color(0xFF0A3D2E).withOpacity(0.07),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: primary,
          foregroundColor: Colors.white,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 14),
          textStyle: GoogleFonts.cairo(fontSize: 15, fontWeight: FontWeight.w700),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: const Color(0xFFF5FBF8),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: Color(0xFFD4E8DF), width: 1.5),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: Color(0xFFD4E8DF), width: 1.5),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: primaryGreen, width: 2),
        ),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      ),
      navigationBarTheme: NavigationBarThemeData(
        height: 68,
        backgroundColor: Colors.white,
        elevation: 8,
        shadowColor: const Color(0xFF0A3D2E).withOpacity(0.10),
        indicatorColor: primary.withOpacity(0.12),
        labelTextStyle: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) {
            return GoogleFonts.cairo(color: primary, fontWeight: FontWeight.w700, fontSize: 12);
          }
          return GoogleFonts.cairo(color: const Color(0xFF5A7A6B), fontSize: 12);
        }),
        iconTheme: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) {
            return const IconThemeData(color: primaryGreen, size: 26);
          }
          return const IconThemeData(color: Color(0xFF5A7A6B), size: 24);
        }),
      ),
    );
  }

  static ThemeData darkTheme() {
    const primary = Color(0xFF2ECC8A);  
    final colorScheme = ColorScheme.fromSeed(
      seedColor: primary,
      brightness: Brightness.dark,
      primary: primary,
      secondary: accentGold,
      surface: const Color(0xFF0D1E19),
      onSurface: Colors.white,
      onSurfaceVariant: const Color(0xFF9DC4AF),
      surfaceContainerLow: const Color(0xFF0D1E19),
      surfaceContainerHighest: const Color(0xFF152B22),
    );
    return ThemeData(
      useMaterial3: true,
      colorScheme: colorScheme,
      textTheme: cairoTextThemeDark,
      fontFamily: 'Cairo',
      scaffoldBackgroundColor: const Color(0xFF071210),
      appBarTheme: AppBarTheme(
        backgroundColor: const Color(0xFF0D1E19),
        foregroundColor: Colors.white,
        elevation: 0,
        centerTitle: true,
        titleTextStyle: GoogleFonts.cairo(fontSize: 20, fontWeight: FontWeight.w700, color: Colors.white),
        iconTheme: const IconThemeData(color: Colors.white, size: 24),
      ),
      cardTheme: CardTheme(
        elevation: 0,
        color: const Color(0xFF0D1E19),
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: const Color(0xFF152B22),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: Color(0xFF2A4A38), width: 1.5),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: Color(0xFF2A4A38), width: 1.5),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: const BorderSide(color: primary, width: 2),
        ),
        labelStyle: const TextStyle(color: Color(0xFF9DC4AF)),
        hintStyle: const TextStyle(color: Color(0xFF5A7A6B)),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      ),
      dialogTheme: const DialogTheme(
        backgroundColor: Color(0xFF0D1E19),
        surfaceTintColor: Colors.transparent,
      ),
      bottomSheetTheme: const BottomSheetThemeData(
        backgroundColor: Color(0xFF0D1E19),
        surfaceTintColor: Colors.transparent,
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: const Color(0xFF0D1E19),
        indicatorColor: primary.withOpacity(0.2),
        labelTextStyle: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) {
            return GoogleFonts.cairo(color: primary, fontWeight: FontWeight.w700, fontSize: 12);
          }
          return GoogleFonts.cairo(color: const Color(0xFF6B9C87), fontSize: 12);
        }),
        iconTheme: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) {
            return IconThemeData(color: primary, size: 24);
          }
          return const IconThemeData(color: Color(0xFF6B9C87), size: 24);
        }),
      ),
    );
  }
}
