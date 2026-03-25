import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

class DriverTheme {
  static const Color primaryGold      = Color(0xFFD4AF37);  
  static const Color primaryGoldDark  = Color(0xFFB8960C);  
  static const Color accentGray       = Color(0xFF6B7280);  
  static const Color surfaceWhite     = Color(0xFFFFFFFF);  

  static const Color primaryRed       = primaryGold;
  static const Color primaryBlue      = primaryGold;
  static const Color primaryBlueDark  = primaryGoldDark;
  static const Color primaryOrange    = primaryGold;
  static const Color primaryOrangeDark= primaryGoldDark;
  static const Color accentTeal       = accentGray;

  static TextTheme get cairoTextTheme =>
      GoogleFonts.cairoTextTheme(ThemeData.light().textTheme);

  static TextTheme get cairoTextThemeDark =>
      GoogleFonts.cairoTextTheme(ThemeData.dark().textTheme);

  static ThemeData lightTheme(Color? primaryOverride) {
    final primary = primaryOverride ?? primaryGold;
    final colorScheme = ColorScheme.fromSeed(
      seedColor: primary,
      brightness: Brightness.light,
      primary: primary,
      secondary: const Color(0xFF6B7280),
      surface: const Color(0xFFFFFFFF),
      onSurface: const Color(0xFF111827),
      onSurfaceVariant: const Color(0xFF4B5563),
      outline: const Color(0xFFD1D5DB),
      primaryContainer: const Color(0xFFFBF3D0),
      onPrimaryContainer: const Color(0xFF3D2C00),
    );
    return ThemeData(
      useMaterial3: true,
      colorScheme: colorScheme,
      textTheme: cairoTextTheme,
      fontFamily: 'Cairo',
      scaffoldBackgroundColor: const Color(0xFFF3F4F6),
      appBarTheme: AppBarTheme(
        backgroundColor: const Color(0xFFFFFFFF),
        foregroundColor: const Color(0xFF111827),
        elevation: 0,
        centerTitle: true,
        titleTextStyle: GoogleFonts.cairo(
          fontSize: 20, fontWeight: FontWeight.w700, color: const Color(0xFF111827),
        ),
        iconTheme: const IconThemeData(color: Color(0xFF111827), size: 24),
        shadowColor: Colors.black.withOpacity(0.08),
      ),
      cardTheme: CardTheme(
        elevation: 0,
        color: const Color(0xFFFFFFFF),
        surfaceTintColor: Colors.transparent,
        shadowColor: primaryGold.withOpacity(0.10),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: const Color(0xFFF9FAFB),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: Color(0xFFD1D5DB)),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: Color(0xFFD1D5DB)),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: BorderSide(color: primary, width: 2),
        ),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: primary,
          foregroundColor: Colors.white,
          elevation: 2,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
          textStyle: GoogleFonts.cairo(fontWeight: FontWeight.w700, fontSize: 16),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: primary,
          side: BorderSide(color: primary),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
          textStyle: GoogleFonts.cairo(fontWeight: FontWeight.w600, fontSize: 16),
        ),
      ),
      dividerColor: const Color(0xFFE5E7EB),
    );
  }

  static ThemeData darkTheme(Color? primaryOverride) {
    final primary = primaryOverride ?? const Color(0xFFE8C84A); 
    const surfaceDark            = Color(0xFF111827);
    const surfaceContainer       = Color(0xFF1F2937);
    const onSurfaceDark          = Color(0xFFF9FAFB);
    const onSurfaceVariantDark   = Color(0xFF9CA3AF);
    const outlineDark            = Color(0xFF374151);

    final colorScheme = ColorScheme.dark(
      primary: primary,
      onPrimary: Colors.black,
      secondary: const Color(0xFF9CA3AF),
      surface: surfaceDark,
      onSurface: onSurfaceDark,
      onSurfaceVariant: onSurfaceVariantDark,
      outline: outlineDark,
      surfaceContainerHighest: surfaceContainer,
    );

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      colorScheme: colorScheme,
      textTheme: cairoTextThemeDark.apply(bodyColor: onSurfaceDark, displayColor: onSurfaceDark),
      fontFamily: 'Cairo',
      scaffoldBackgroundColor: const Color(0xFF0D1117),
      appBarTheme: AppBarTheme(
        backgroundColor: surfaceContainer,
        foregroundColor: onSurfaceDark,
        elevation: 0,
        centerTitle: true,
        titleTextStyle: GoogleFonts.cairo(fontSize: 20, fontWeight: FontWeight.w700, color: onSurfaceDark),
        iconTheme: const IconThemeData(color: onSurfaceDark, size: 24),
      ),
      cardTheme: CardTheme(
        elevation: 0,
        color: surfaceContainer,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: surfaceDark,
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(14)),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: outlineDark),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: BorderSide(color: primary, width: 2),
        ),
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
        labelStyle: const TextStyle(color: onSurfaceVariantDark),
        hintStyle: const TextStyle(color: onSurfaceVariantDark),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: primary,
          foregroundColor: Colors.black,
          elevation: 2,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
          textStyle: GoogleFonts.cairo(fontWeight: FontWeight.w700, fontSize: 16),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: primary,
          side: BorderSide(color: primary),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
          textStyle: GoogleFonts.cairo(fontWeight: FontWeight.w600, fontSize: 16),
        ),
      ),
      dividerColor: outlineDark,
    );
  }
}
