import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:image_picker/image_picker.dart';
import 'dart:typed_data';
import '../services/agent_api.dart';
import '../models/agent_state.dart';
import '../app_config.dart';

class AgentProductsScreen extends StatefulWidget {
  const AgentProductsScreen({super.key, required this.state});
  final AgentState state;

  @override
  State<AgentProductsScreen> createState() => _AgentProductsScreenState();
}

class _AgentProductsScreenState extends State<AgentProductsScreen> {
  late final AgentApi _api;
  bool _loading = true;
  String? _error;
  List<Map<String, dynamic>> _products = [];
  List<Map<String, dynamic>> _categories = [];

  @override
  void initState() {
    super.initState();
    _api = AgentApi(baseUrl: kBackendBaseUrl);
    _load();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final token = widget.state.token ?? '';
      final results = await Future.wait([
        _api.getMyProducts(token),
        _api.getCategories(token),
      ]);
      _products = results[0].cast<Map<String, dynamic>>();
      _categories = results[1].cast<Map<String, dynamic>>();
    } catch (e) {
      _error = e.toString();
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _toggleAvailability(Map<String, dynamic> product) async {
    final id = product['id'] as int;
    final current = product['isAvailable'] == true;
    try {
      await _api.toggleProductAvailability(widget.state.token ?? '', id, !current);
      await _load();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('فشل التحديث: $e')),
        );
      }
    }
  }

  Future<void> _deleteProduct(int id) async {
    final confirm = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('حذف المنتج', style: GoogleFonts.cairo(fontWeight: FontWeight.w700)),
        content: Text('هل أنت متأكد من حذف هذا المنتج؟', style: GoogleFonts.cairo()),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: Text('إلغاء', style: GoogleFonts.cairo()),
          ),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: Colors.red),
            onPressed: () => Navigator.pop(ctx, true),
            child: Text('حذف', style: GoogleFonts.cairo()),
          ),
        ],
      ),
    );
    if (confirm != true) return;
    try {
      await _api.deleteProduct(widget.state.token ?? '', id);
      await _load();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('تم حذف المنتج ✅')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('فشل الحذف: $e')),
        );
      }
    }
  }

  void _openForm({Map<String, dynamic>? product}) {
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => _ProductFormSheet(
        api: _api,
        state: widget.state,
        categories: _categories,
        product: product,
        onSaved: _load,
      ),
    );
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
              const SizedBox(height: 16),
              Text(_error!, textAlign: TextAlign.center),
              const SizedBox(height: 16),
              FilledButton.icon(
                onPressed: _load,
                icon: const Icon(Icons.refresh),
                label: const Text('إعادة المحاولة'),
              ),
            ],
          ),
        ),
      );
    }

    return Scaffold(
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _openForm(),
        icon: const Icon(Icons.add_rounded),
        label: Text('إضافة منتج', style: GoogleFonts.cairo(fontWeight: FontWeight.w700)),
        backgroundColor: cs.primary,
        foregroundColor: Colors.white,
      ),
      body: _products.isEmpty
          ? Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(Icons.inventory_2_outlined, size: 64, color: cs.primary.withOpacity(0.4)),
                  const SizedBox(height: 16),
                  Text(
                    'لا توجد منتجات بعد',
                    style: GoogleFonts.cairo(fontSize: 18, fontWeight: FontWeight.w700, color: cs.onSurfaceVariant),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'اضغط + لإضافة أول منتج لك',
                    style: GoogleFonts.cairo(color: cs.onSurfaceVariant),
                  ),
                ],
              ),
            )
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView.builder(
                padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
                itemCount: _products.length,
                itemBuilder: (ctx, i) {
                  final p = _products[i];
                  final isAvailable = p['isAvailable'] == true;
                  final name = (p['name'] ?? '').toString();
                  final price = (p['price'] ?? 0).toDouble();
                  final category = (p['categoryName'] ?? p['category'] ?? '').toString();
                  final imageUrl = _api.absoluteUrl(p['imageUrl']?.toString() ?? p['image']?.toString());
                  final trackStock = p['trackStock'] == true;
                  final stockQty = p['stockQuantity'] as int? ?? 0;
                  final isLowStock = trackStock && stockQty < 5 && stockQty > 0;
                  final isOutOfStock = trackStock && stockQty == 0;

                  return Container(
                    margin: const EdgeInsets.only(bottom: 12),
                    decoration: BoxDecoration(
                      color: cs.surfaceContainerLow,
                      borderRadius: BorderRadius.circular(16),
                      boxShadow: [
                        BoxShadow(
                          color: Colors.black.withOpacity(0.05),
                          blurRadius: 10,
                          offset: const Offset(0, 2),
                        ),
                      ],
                    ),
                    child: Column(
                      children: [
                        ListTile(
                          contentPadding: const EdgeInsets.fromLTRB(16, 8, 8, 8),
                          leading: ClipRRect(
                            borderRadius: BorderRadius.circular(12),
                            child: imageUrl.isNotEmpty
                                ? Image.network(
                                    imageUrl,
                                    width: 60,
                                    height: 60,
                                    fit: BoxFit.cover,
                                    errorBuilder: (_, __, ___) => _imagePlaceholder(cs),
                                  )
                                : _imagePlaceholder(cs),
                          ),
                          title: Text(
                            name,
                            style: GoogleFonts.cairo(fontWeight: FontWeight.w700, fontSize: 15),
                          ),
                          subtitle: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              if (category.isNotEmpty)
                                Text(category, style: GoogleFonts.cairo(fontSize: 12, color: cs.onSurfaceVariant)),
                              Text(
                                '${price.toStringAsFixed(0)} ل.س',
                                style: GoogleFonts.cairo(
                                  fontSize: 14,
                                  fontWeight: FontWeight.w700,
                                  color: cs.primary,
                                ),
                              ),
                            ],
                          ),
                          trailing: PopupMenuButton<String>(
                            icon: const Icon(Icons.more_vert_rounded),
                            onSelected: (v) {
                              if (v == 'edit') _openForm(product: p);
                              if (v == 'delete') _deleteProduct(p['id'] as int);
                            },
                            itemBuilder: (_) => [
                              const PopupMenuItem(value: 'edit', child: Row(children: [Icon(Icons.edit_rounded), SizedBox(width: 8), Text('تعديل')])),
                              const PopupMenuItem(value: 'delete', child: Row(children: [Icon(Icons.delete_rounded, color: Colors.red), SizedBox(width: 8), Text('حذف', style: TextStyle(color: Colors.red))])),
                            ],
                          ),
                        ),
                        Divider(height: 1, color: cs.outlineVariant),
                        Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                          child: Row(
                            children: [
                              Icon(
                                isAvailable ? Icons.check_circle_rounded : Icons.cancel_rounded,
                                size: 16,
                                color: isAvailable ? Colors.green : Colors.red,
                              ),
                              const SizedBox(width: 6),
                              Text(
                                isAvailable ? 'متاح' : 'غير متاح',
                                style: GoogleFonts.cairo(
                                  fontSize: 13,
                                  color: isAvailable ? Colors.green : Colors.red,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                              if (trackStock) ...[
                                const SizedBox(width: 8),
                                Container(
                                  padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                                  decoration: BoxDecoration(
                                    color: isOutOfStock
                                        ? Colors.grey.shade200
                                        : isLowStock
                                            ? Colors.red.shade100
                                            : Colors.green.shade100,
                                    borderRadius: BorderRadius.circular(10),
                                  ),
                                  child: Row(
                                    mainAxisSize: MainAxisSize.min,
                                    children: [
                                      Icon(
                                        Icons.inventory_2_outlined,
                                        size: 12,
                                        color: isOutOfStock
                                            ? Colors.grey.shade600
                                            : isLowStock
                                                ? Colors.red.shade700
                                                : Colors.green.shade700,
                                      ),
                                      const SizedBox(width: 4),
                                      Text(
                                        isOutOfStock ? 'نفد المخزون' : '$stockQty وحدة',
                                        style: GoogleFonts.cairo(
                                          fontSize: 11,
                                          fontWeight: FontWeight.w700,
                                          color: isOutOfStock
                                              ? Colors.grey.shade600
                                              : isLowStock
                                                  ? Colors.red.shade700
                                                  : Colors.green.shade700,
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              ],
                              const Spacer(),
                              Switch.adaptive(
                                value: isAvailable,
                                onChanged: (_) => _toggleAvailability(p),
                                activeColor: cs.primary,
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  );
                },
              ),
            ),
    );
  }

  Widget _imagePlaceholder(ColorScheme cs) {
    return Container(
      width: 60,
      height: 60,
      decoration: BoxDecoration(
        color: cs.primaryContainer,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Icon(Icons.inventory_2_rounded, color: cs.onPrimaryContainer, size: 30),
    );
  }
}

class _ProductFormSheet extends StatefulWidget {
  const _ProductFormSheet({
    required this.api,
    required this.state,
    required this.categories,
    required this.onSaved,
    this.product,
  });
  final AgentApi api;
  final AgentState state;
  final List<Map<String, dynamic>> categories;
  final Map<String, dynamic>? product;
  final VoidCallback onSaved;

  @override
  State<_ProductFormSheet> createState() => _ProductFormSheetState();
}

class _ProductFormSheetState extends State<_ProductFormSheet> {
  final _nameCtl = TextEditingController();
  final _descCtl = TextEditingController();
  final _priceCtl = TextEditingController();
  int? _selectedCategoryId;
  bool _isAvailable = true;
  bool _loading = false;
  int _stockQty = 0;
  bool _trackStock = false;
  late TextEditingController _stockCtl;

  String? _existingImageUrl;
  Uint8List? _pickedImageBytes;
  String? _pickedImageName;

  @override
  void initState() {
    super.initState();
    _stockCtl = TextEditingController(text: '0');
    final p = widget.product;
    if (p != null) {
      _nameCtl.text = (p['name'] ?? '').toString();
      _descCtl.text = (p['description'] ?? '').toString();
      _priceCtl.text = (p['price'] ?? 0).toString();
      _existingImageUrl = (p['imageUrl'] ?? p['image'] ?? '').toString();
      if (_existingImageUrl!.isEmpty) _existingImageUrl = null;
      _isAvailable = p['isAvailable'] != false;
      _selectedCategoryId = p['categoryId'] as int?;
      _stockQty = p['stockQuantity'] as int? ?? 0;
      _trackStock = p['trackStock'] == true;
      _stockCtl.text = _stockQty.toString();
    }
  }

  @override
  void dispose() {
    _nameCtl.dispose();
    _descCtl.dispose();
    _priceCtl.dispose();
    _stockCtl.dispose();
    super.dispose();
  }

  Future<void> _pickImage() async {
    try {
      final picker = ImagePicker();
      
      // محاولة الاختيار
      final xfile = await picker.pickImage(
        source: ImageSource.gallery, 
        imageQuality: 85, 
        maxWidth: 1024
      );
      
      if (xfile == null) {
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('لم يتم اختيار صورة'))
          );
        }
        return;
      }
      
      // قراءة البايتات
      final bytes = await xfile.readAsBytes();
      
      if (bytes.isEmpty) {
        if (mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('الصورة فارغة أو تالفة'))
          );
        }
        return;
      }
      
      // تحديث الحالة
      if (mounted) {
        setState(() {
          _pickedImageBytes = bytes;
          _pickedImageName = xfile.name;
        });
        
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('✅ تم اختيار الصورة بنجاح'))
        );
      }
    } on Exception catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('خطأ: ${e.toString()}'))
        );
      }
    }
  }

  Future<void> _save() async {
    final name = _nameCtl.text.trim();
    final price = double.tryParse(_priceCtl.text.trim()) ?? 0;
    if (name.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('يرجى إدخال اسم المنتج')));
      return;
    }
    if (price <= 0) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('يرجى إدخال سعر صحيح')));
      return;
    }
    if (_selectedCategoryId == null && widget.categories.isNotEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('يرجى اختيار الفئة')));
      return;
    }

    setState(() => _loading = true);
    try {
      final token = widget.state.token ?? '';
      final catId = _selectedCategoryId ?? (widget.categories.isNotEmpty ? widget.categories.first['id'] as int : 0);
      String? imageUrl = _existingImageUrl;

      if (widget.product != null) {
        await widget.api.updateProduct(
          token,
          widget.product!['id'] as int,
          name: name,
          description: _descCtl.text.trim(),
          price: price,
          categoryId: catId,
          isAvailable: _isAvailable,
          imageUrl: imageUrl,
          stockQuantity: _trackStock ? _stockQty : 0,
          trackStock: _trackStock,
        );
        // Upload image after update if picked
        if (_pickedImageBytes != null && _pickedImageName != null) {
          await widget.api.uploadProductImage(token, widget.product!['id'] as int, _pickedImageBytes!, _pickedImageName!);
        }
      } else {
        final result = await widget.api.createProduct(
          token,
          name: name,
          description: _descCtl.text.trim(),
          price: price,
          categoryId: catId,
          isAvailable: _isAvailable,
          imageUrl: imageUrl,
          stockQuantity: _trackStock ? _stockQty : 0,
          trackStock: _trackStock,
        );
        final newId = result['id'] as int?;
        // Upload image for new product
        if (_pickedImageBytes != null && _pickedImageName != null && newId != null) {
          await widget.api.uploadProductImage(token, newId, _pickedImageBytes!, _pickedImageName!);
        }
      }
      if (mounted) {
        Navigator.pop(context);
        
        // إعادة تحميل البيانات لضمان ظهور الصورة الجديدة
        widget.onSaved();
        
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(widget.product != null ? 'تم تحديث المنتج ✅' : 'تم إضافة المنتج ✅'),
            duration: const Duration(seconds: 3),
          ),
        );
        
        // تأخير صغير للسماح بالتحديث
        await Future.delayed(const Duration(milliseconds: 500));
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('فشل الحفظ: $e')));
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final isEdit = widget.product != null;

    return Container(
      decoration: BoxDecoration(
        color: Theme.of(context).colorScheme.surface,
        borderRadius: const BorderRadius.vertical(top: Radius.circular(24)),
      ),
      padding: EdgeInsets.fromLTRB(24, 20, 24, 24 + MediaQuery.viewInsetsOf(context).bottom),
      child: SingleChildScrollView(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          children: [
            
            Center(
              child: Container(
                width: 40, height: 4,
                decoration: BoxDecoration(
                  color: cs.outlineVariant,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
            ),
            const SizedBox(height: 20),
            Text(
              isEdit ? 'تعديل المنتج' : 'إضافة منتج جديد',
              textAlign: TextAlign.center,
              style: GoogleFonts.cairo(fontSize: 20, fontWeight: FontWeight.w800, color: cs.onSurface),
            ),
            const SizedBox(height: 24),

            TextField(
              controller: _nameCtl,
              textDirection: TextDirection.rtl,
              decoration: const InputDecoration(
                labelText: 'اسم المنتج *',
                prefixIcon: Icon(Icons.inventory_2_rounded),
              ),
            ),
            const SizedBox(height: 14),

            TextField(
              controller: _descCtl,
              textDirection: TextDirection.rtl,
              maxLines: 3,
              decoration: const InputDecoration(
                labelText: 'الوصف',
                prefixIcon: Icon(Icons.description_rounded),
                alignLabelWithHint: true,
              ),
            ),
            const SizedBox(height: 14),

            TextField(
              controller: _priceCtl,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              textDirection: TextDirection.ltr,
              decoration: const InputDecoration(
                labelText: 'السعر *',
                prefixIcon: Icon(Icons.attach_money_rounded),
                suffixText: 'ل.س',
              ),
            ),
            const SizedBox(height: 14),

            if (widget.categories.isNotEmpty) ...[
              DropdownButtonFormField<int>(
                value: _selectedCategoryId,
                decoration: const InputDecoration(
                  labelText: 'الفئة',
                  prefixIcon: Icon(Icons.category_rounded),
                ),
                items: widget.categories.map((c) {
                  return DropdownMenuItem<int>(
                    value: c['id'] as int,
                    child: Text((c['name'] ?? '').toString(), style: GoogleFonts.cairo()),
                  );
                }).toList(),
                onChanged: (v) => setState(() => _selectedCategoryId = v),
              ),
              const SizedBox(height: 14),
            ],

            // حقل اختيار صورة المنتج
            GestureDetector(
              onTap: _pickImage,
              child: Container(
                height: 120,
                decoration: BoxDecoration(
                  color: cs.surfaceContainerHighest.withOpacity(0.5),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: cs.outlineVariant, style: BorderStyle.solid),
                ),
                child: _pickedImageBytes != null
                    ? ClipRRect(
                        borderRadius: BorderRadius.circular(12),
                        child: Image.memory(_pickedImageBytes!, fit: BoxFit.cover, width: double.infinity),
                      )
                    : (_existingImageUrl != null && _existingImageUrl!.isNotEmpty)
                        ? ClipRRect(
                            borderRadius: BorderRadius.circular(12),
                            child: Image.network(
                              widget.api.absoluteUrl(_existingImageUrl),
                              fit: BoxFit.cover,
                              width: double.infinity,
                              errorBuilder: (_, __, ___) => _noImageWidget(cs),
                            ),
                          )
                        : _noImageWidget(cs),
              ),
            ),
            const SizedBox(height: 8),
            TextButton.icon(
              onPressed: _pickImage,
              icon: Icon(Icons.photo_library_rounded, color: cs.primary),
              label: Text('اختيار صورة من المعرض', style: GoogleFonts.cairo(color: cs.primary, fontWeight: FontWeight.w600)),
            ),
            const SizedBox(height: 14),

            Container(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
              decoration: BoxDecoration(
                color: cs.surfaceContainerHighest.withOpacity(0.4),
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: cs.outlineVariant),
              ),
              child: Row(
                children: [
                  Icon(
                    _isAvailable ? Icons.check_circle_rounded : Icons.cancel_rounded,
                    color: _isAvailable ? Colors.green : Colors.red,
                  ),
                  const SizedBox(width: 12),
                  Text(
                    'متاح للبيع',
                    style: GoogleFonts.cairo(fontSize: 15, fontWeight: FontWeight.w600),
                  ),
                  const Spacer(),
                  Switch.adaptive(
                    value: _isAvailable,
                    onChanged: (v) => setState(() => _isAvailable = v),
                    activeColor: cs.primary,
                  ),
                ],
              ),
            ),
            const SizedBox(height: 14),

            Container(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
              decoration: BoxDecoration(
                color: cs.surfaceContainerHighest.withOpacity(0.4),
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: cs.outlineVariant),
              ),
              child: Row(
                children: [
                  Icon(Icons.inventory_outlined, color: cs.primary),
                  const SizedBox(width: 12),
                  Text('تتبع المخزون', style: GoogleFonts.cairo(fontSize: 15, fontWeight: FontWeight.w600)),
                  const Spacer(),
                  Switch.adaptive(
                    value: _trackStock,
                    onChanged: (v) => setState(() => _trackStock = v),
                    activeColor: cs.primary,
                  ),
                ],
              ),
            ),
            if (_trackStock) ...[
              const SizedBox(height: 14),
              TextField(
                controller: _stockCtl,
                keyboardType: TextInputType.number,
                textDirection: TextDirection.ltr,
                onChanged: (v) => _stockQty = int.tryParse(v) ?? 0,
                decoration: const InputDecoration(
                  labelText: 'الكمية المتاحة في المخزون',
                  prefixIcon: Icon(Icons.warehouse_outlined),
                  suffixText: 'وحدة',
                ),
              ),
            ],
            const SizedBox(height: 24),

            FilledButton(
              onPressed: _loading ? null : _save,
              style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(52)),
              child: _loading
                  ? const SizedBox(
                      width: 22, height: 22,
                      child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white),
                    )
                  : Text(
                      isEdit ? 'حفظ التعديلات' : 'إضافة المنتج',
                      style: GoogleFonts.cairo(fontSize: 16, fontWeight: FontWeight.w700),
                    ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _noImageWidget(ColorScheme cs) {
    return Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Icon(Icons.add_photo_alternate_outlined, size: 36, color: cs.onSurfaceVariant),
        const SizedBox(height: 6),
        Text('اضغط لاختيار صورة', style: GoogleFonts.cairo(fontSize: 13, color: cs.onSurfaceVariant)),
      ],
    );
  }
}
