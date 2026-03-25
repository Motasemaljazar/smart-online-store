import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import '../models/app_state.dart';

class BrandSchemes {
  BrandSchemes({required this.light, required this.dark});
  final ColorScheme light;
  final ColorScheme dark;
}

class AppTheme {
  
  static const Color fallbackPrimary   = Color(0xFF5C4A8E);  
  static const Color fallbackSecondary = Color(0xFFD4AF37);  
  static const Color accentColor       = Color(0xFFB8A040);  
  static const Color errorColor        = Color(0xFFCF4B43);  
  static const Color successColor      = Color(0xFF2A7F5B);  
  static const Color warningColor      = Color(0xFFB8860B);  
  static const Color infoColor         = Color(0xFF4A3F6B);  

  static const Color cardBg     = Color(0xFFF8F7FC);
  static const Color surfaceBg  = Color(0xFFEFEDF8);
  static const Color scaffoldBg = Color(0xFFE8E6F4);

  static const LinearGradient primaryGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF3D2C6E), Color(0xFF5C4A8E)],
  );

  static const LinearGradient secondaryGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFFB8A040), Color(0xFFD4AF37)],
  );

  static const LinearGradient accentGradient = LinearGradient(
    begin: Alignment.topLeft,
    end: Alignment.bottomRight,
    colors: [Color(0xFF4A3F6B), Color(0xFF5C4A8E)],
  );

  static List<BoxShadow> get cardShadow => [
    BoxShadow(color: const Color(0xFF3D2C6E).withOpacity(0.06), offset: const Offset(0, 2), blurRadius: 10),
    BoxShadow(color: const Color(0xFF3D2C6E).withOpacity(0.03), offset: const Offset(0, 6), blurRadius: 20),
  ];

  static List<BoxShadow> get elevatedShadow => [
    BoxShadow(color: const Color(0xFF3D2C6E).withOpacity(0.10), offset: const Offset(0, 4), blurRadius: 16),
    BoxShadow(color: const Color(0xFF3D2C6E).withOpacity(0.05), offset: const Offset(0, 10), blurRadius: 32),
  ];

  static List<BoxShadow> get buttonShadow => [
    BoxShadow(color: fallbackPrimary.withOpacity(0.25), offset: const Offset(0, 4), blurRadius: 14),
  ];

  static TextTheme get arabicTextTheme {
    return TextTheme(
      displayLarge:  GoogleFonts.cairo(fontWeight: FontWeight.w900, fontSize: 34, letterSpacing: -0.5, height: 1.2),
      displayMedium: GoogleFonts.cairo(fontWeight: FontWeight.w900, fontSize: 30, letterSpacing: -0.5, height: 1.2),
      displaySmall:  GoogleFonts.cairo(fontWeight: FontWeight.w800, fontSize: 26, letterSpacing: -0.25, height: 1.2),
      headlineLarge:  GoogleFonts.cairo(fontWeight: FontWeight.w800, fontSize: 30, height: 1.3),
      headlineMedium: GoogleFonts.cairo(fontWeight: FontWeight.w800, fontSize: 26, height: 1.3),
      headlineSmall:  GoogleFonts.cairo(fontWeight: FontWeight.w700, fontSize: 22, height: 1.3),
      titleLarge:  GoogleFonts.cairo(fontWeight: FontWeight.w700, fontSize: 20, height: 1.4, letterSpacing: 0.15),
      titleMedium: GoogleFonts.cairo(fontWeight: FontWeight.w600, fontSize: 18, height: 1.4, letterSpacing: 0.15),
      titleSmall:  GoogleFonts.cairo(fontWeight: FontWeight.w600, fontSize: 16, height: 1.4, letterSpacing: 0.1),
      bodyLarge:   GoogleFonts.cairo(fontWeight: FontWeight.w500, fontSize: 16, height: 1.6, letterSpacing: 0.5),
      bodyMedium:  GoogleFonts.cairo(fontWeight: FontWeight.w400, fontSize: 14, height: 1.6, letterSpacing: 0.25),
      bodySmall:   GoogleFonts.cairo(fontWeight: FontWeight.w400, fontSize: 12, height: 1.5, letterSpacing: 0.4),
      labelLarge:  GoogleFonts.cairo(fontWeight: FontWeight.w700, fontSize: 16, letterSpacing: 0.5, height: 1.2),
      labelMedium: GoogleFonts.cairo(fontWeight: FontWeight.w600, fontSize: 14, letterSpacing: 0.5, height: 1.2),
      labelSmall:  GoogleFonts.cairo(fontWeight: FontWeight.w600, fontSize: 12, letterSpacing: 0.5, height: 1.2),
    );
  }

  static Color _parseHex(String? hex) {
    final h = (hex ?? '').replaceAll('#', '').trim();
    if (h.length == 6) return Color(int.parse('FF$h', radix: 16));
    if (h.length == 8) return Color(int.parse(h, radix: 16));
    return fallbackPrimary;
  }

  static BrandSchemes brandFromState(AppState state) {
    final primary = (state.primaryColorHex.trim().isNotEmpty)
        ? _parseHex(state.primaryColorHex)
        : fallbackPrimary;
    final secondary = (state.secondaryColorHex.trim().isNotEmpty)
        ? _parseHex(state.secondaryColorHex)
        : fallbackSecondary;

    ColorScheme withCustomizations(ColorScheme cs) {
      return cs.copyWith(
        primary: primary,
        secondary: secondary,
        error: errorColor,
        surface: cs.brightness == Brightness.light ? const Color(0xFFF8F7FC) : const Color(0xFF0F0C1A),
        surfaceContainerHighest: cs.brightness == Brightness.light ? surfaceBg : const Color(0xFF0A0814),
        onPrimary: Colors.white,
        onSecondary: Colors.white,
        onSurface: cs.brightness == Brightness.light ? const Color(0xFF1A1230) : const Color(0xFFE8E0F8),
        onSurfaceVariant: cs.brightness == Brightness.light ? const Color(0xFF4A3F6B) : const Color(0xFF9080B0),
        outline: cs.brightness == Brightness.light ? const Color(0xFFD0C8E8) : const Color(0xFF2A1F40),
        outlineVariant: cs.brightness == Brightness.light ? const Color(0xFFE8E4F4) : const Color(0xFF18102A),
        shadow: cs.brightness == Brightness.light
            ? const Color(0xFF3D2C6E).withOpacity(0.08)
            : Colors.black.withOpacity(0.5),
        surfaceTint: Colors.transparent,
        primaryContainer: cs.brightness == Brightness.light ? const Color(0xFFE0D8F8) : const Color(0xFF1E1540),
        onPrimaryContainer: cs.brightness == Brightness.light ? const Color(0xFF1A0E40) : const Color(0xFFC0A8E8),
        secondaryContainer: cs.brightness == Brightness.light ? const Color(0xFFF8F0D0) : const Color(0xFF251C05),
        onSecondaryContainer: cs.brightness == Brightness.light ? const Color(0xFF2A1E00) : const Color(0xFFD4AF37),
      );
    }

    final light = withCustomizations(ColorScheme.fromSeed(
      seedColor: primary, brightness: Brightness.light, surfaceTint: Colors.transparent,
    ));
    final dark = withCustomizations(ColorScheme.fromSeed(
      seedColor: primary, brightness: Brightness.dark, surfaceTint: Colors.transparent,
    ));

    return BrandSchemes(light: light, dark: dark);
  }

  static const double radiusSmall  = 12.0;
  static const double radiusMedium = 16.0;
  static const double radiusLarge  = 20.0;
  static const double radiusXLarge = 24.0;

  static const double spaceXS  = 4.0;
  static const double spaceS   = 8.0;
  static const double spaceM   = 16.0;
  static const double spaceL   = 24.0;
  static const double spaceXL  = 32.0;
  static const double spaceXXL = 48.0;
}
