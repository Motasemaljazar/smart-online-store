using AdminDashboard.Data;
using AdminDashboard.Entities;
using AdminDashboard.Hubs;
using AdminDashboard.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/customer")]
public class CustomerController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotifyHub> _hub;
    private readonly NotificationService _notifications;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;

    public CustomerController(AppDbContext db, IHubContext<NotifyHub> hub, NotificationService notifications, IHttpClientFactory httpFactory, IConfiguration config, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _hub = hub;
        _notifications = notifications;
        _httpFactory = httpFactory;
        _config = config;
        _scopeFactory = scopeFactory;
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    public record LoginRequest(string Phone);

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var phone = (req.Phone ?? "").Trim();
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { error = "phone_required" });

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Phone == phone);

        if (customer == null)
            return Ok(new { requiresProfile = true });

        if (customer.IsAppBlocked)
            return StatusCode(403, new { error = "customer_blocked", message = "تم منعك من الدخول" });

        return Ok(new
        {
            customer.Id,
            customer.Name,
            customer.Phone,
            customer.DefaultLat,
            customer.DefaultLng,
            customer.DefaultAddress,
        });
    }

    public record RegisterRequest(string Name, string Phone, double Lat, double Lng, string? Address);

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Phone == req.Phone);
        if (customer != null && customer.IsAppBlocked)
            return StatusCode(403, new { error = "customer_blocked", message = "تم منعك من الدخول" });
        if (customer == null)
        {
            customer = new Customer
            {
                Name = req.Name,
                Phone = req.Phone,
                DefaultLat = req.Lat,
                DefaultLng = req.Lng,
                LastLat = req.Lat,
                LastLng = req.Lng,
                DefaultAddress = req.Address
            };
            _db.Customers.Add(customer);
        }
        else
        {
            customer.Name = req.Name;
            customer.DefaultLat = req.Lat;
            customer.DefaultLng = req.Lng;
            customer.LastLat = req.Lat;
            customer.LastLng = req.Lng;
            customer.DefaultAddress = req.Address;
        }

        await _db.SaveChangesAsync();
        return Ok(new { customer.Id, customer.Name, customer.Phone, customer.DefaultLat, customer.DefaultLng, customer.DefaultAddress });
    }

public record AddressDto(
    int Id,
    string Title,
    string AddressText,
    double Latitude,
    double Longitude,
    string? Building,
    string? Floor,
    string? Apartment,
    string? Notes,
    bool IsDefault,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

[HttpGet("addresses/{customerId:int}")]
public async Task<IActionResult> GetAddresses(int customerId)
{
    var list = await _db.CustomerAddresses.AsNoTracking()
        .Where(a => a.CustomerId == customerId)
        .OrderByDescending(a => a.IsDefault)
        .ThenByDescending(a => a.UpdatedAtUtc)
        .Select(a => new AddressDto(a.Id, a.Title, a.AddressText, a.Latitude, a.Longitude, a.Building, a.Floor, a.Apartment, a.Notes, a.IsDefault, a.CreatedAtUtc, a.UpdatedAtUtc))
        .ToListAsync();
    return Ok(list);
}

public record CreateAddressReq(
    int CustomerId,
    string Title,
    string AddressText,
    double Latitude,
    double Longitude,
    string? Building,
    string? Floor,
    string? Apartment,
    string? Notes,
    bool SetDefault);

[HttpPost("addresses")]
public async Task<IActionResult> CreateAddress(CreateAddressReq req)
{
    var customer = await _db.Customers.FindAsync(req.CustomerId);
    if (customer == null) return NotFound(new { error = "customer_not_found" });

    var a = new CustomerAddress
    {
        CustomerId = req.CustomerId,
        Title = string.IsNullOrWhiteSpace(req.Title) ? "البيت" : req.Title.Trim(),
        AddressText = (req.AddressText ?? "").Trim(),
        Latitude = req.Latitude,
        Longitude = req.Longitude,
        Building = string.IsNullOrWhiteSpace(req.Building) ? null : req.Building.Trim(),
        Floor = string.IsNullOrWhiteSpace(req.Floor) ? null : req.Floor.Trim(),
        Apartment = string.IsNullOrWhiteSpace(req.Apartment) ? null : req.Apartment.Trim(),
        Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
        IsDefault = false,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    var hasAny = await _db.CustomerAddresses.AsNoTracking().AnyAsync(x => x.CustomerId == req.CustomerId);
    if (!hasAny) a.IsDefault = true;
    if (req.SetDefault) a.IsDefault = true;

    if (a.IsDefault)
    {
        var others = await _db.CustomerAddresses.Where(x => x.CustomerId == req.CustomerId && x.IsDefault).ToListAsync();
        foreach (var o in others) o.IsDefault = false;
    }

    _db.CustomerAddresses.Add(a);
    await _db.SaveChangesAsync();

    return Ok(new AddressDto(a.Id, a.Title, a.AddressText, a.Latitude, a.Longitude, a.Building, a.Floor, a.Apartment, a.Notes, a.IsDefault, a.CreatedAtUtc, a.UpdatedAtUtc));
}

public record UpdateAddressReq(
    int CustomerId,
    string Title,
    string AddressText,
    double Latitude,
    double Longitude,
    string? Building,
    string? Floor,
    string? Apartment,
    string? Notes);

[HttpPut("addresses/{id:int}")]
public async Task<IActionResult> UpdateAddress(int id, UpdateAddressReq req)
{
    var a = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == req.CustomerId);
    if (a == null) return NotFound(new { error = "address_not_found" });

    a.Title = string.IsNullOrWhiteSpace(req.Title) ? a.Title : req.Title.Trim();
    a.AddressText = (req.AddressText ?? "").Trim();
    a.Latitude = req.Latitude;
    a.Longitude = req.Longitude;
    a.Building = string.IsNullOrWhiteSpace(req.Building) ? null : req.Building.Trim();
    a.Floor = string.IsNullOrWhiteSpace(req.Floor) ? null : req.Floor.Trim();
    a.Apartment = string.IsNullOrWhiteSpace(req.Apartment) ? null : req.Apartment.Trim();
    a.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
    a.UpdatedAtUtc = DateTime.UtcNow;

    await _db.SaveChangesAsync();
    return Ok(new AddressDto(a.Id, a.Title, a.AddressText, a.Latitude, a.Longitude, a.Building, a.Floor, a.Apartment, a.Notes, a.IsDefault, a.CreatedAtUtc, a.UpdatedAtUtc));
}

[HttpDelete("addresses/{id:int}")]
public async Task<IActionResult> DeleteAddress(int id, [FromQuery] int customerId)
{
    var a = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == customerId);
    if (a == null) return NotFound(new { error = "address_not_found" });

    var wasDefault = a.IsDefault;
    _db.CustomerAddresses.Remove(a);
    await _db.SaveChangesAsync();

    if (wasDefault)
    {
        var next = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.CustomerId == customerId);
        if (next != null)
        {
            next.IsDefault = true;
            next.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    return Ok(new { ok = true });
}

[HttpPost("addresses/{id:int}/set-default")]
public async Task<IActionResult> SetDefaultAddress(int id, [FromQuery] int customerId)
{
    var a = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == id && x.CustomerId == customerId);
    if (a == null) return NotFound(new { error = "address_not_found" });

    var others = await _db.CustomerAddresses.Where(x => x.CustomerId == customerId && x.Id != id && x.IsDefault).ToListAsync();
    foreach (var o in others) o.IsDefault = false;

    a.IsDefault = true;
    a.UpdatedAtUtc = DateTime.UtcNow;

    await _db.SaveChangesAsync();
    return Ok(new { ok = true });
}

    public record OrderItemReq(int ProductId, int Quantity, string? OptionsSnapshot);
    public record CreateOrderRequest(
        int CustomerId,
        string IdempotencyKey,
        List<OrderItemReq> Items,
        string? Notes,
        int? AddressId,
        double DeliveryLat,
        double DeliveryLng,
        string? DeliveryAddress,
        PaymentMethod PaymentMethod = PaymentMethod.Cash);

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest req)
    {

        var settings = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync()
            ?? new StoreSettings
            {
                MinOrderAmount = 0,
                DeliveryFeeType = DeliveryFeeType.Fixed,
                DeliveryFeeValue = 0,
                IsAcceptingOrders = true,
                IsManuallyClosed = false,
                ClosedMessage = "المتجر مغلق حالياً"
            };

        if (settings.IsManuallyClosed)
            return BadRequest(new { error = "store_closed", message = string.IsNullOrWhiteSpace(settings.ClosedMessage) ? "المتجر مغلق حالياً" : settings.ClosedMessage });

        var customer = await _db.Customers.FindAsync(req.CustomerId);
        if (customer == null) return NotFound(new { error = "الزبون غير موجود" });

        if (customer.IsAppBlocked)
            return StatusCode(403, new { error = "customer_blocked", message = "تم منعك من الدخول" });

        CustomerAddress? addr = null;
        if (req.AddressId.HasValue)
        {
            addr = await _db.CustomerAddresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.AddressId.Value && a.CustomerId == req.CustomerId);
            if (addr == null) return BadRequest(new { error = "address_not_found", message = "العنوان المختار غير موجود" });
        }

        var key = (req.IdempotencyKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { error = "idempotency_required", message = "تعذر إرسال الطلب. حاول مجدداً." });

        var window = DateTime.UtcNow.AddMinutes(-5);
        var existing = await _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == req.CustomerId && o.IdempotencyKey == key && o.CreatedAtUtc >= window)
            .OrderByDescending(o => o.Id)
            .Select(o => new { o.Id })
            .FirstOrDefaultAsync();
        if (existing != null)
            return Ok(new { id = existing.Id, alreadyCreated = true });

        if (addr == null && req.DeliveryLat == 0 && req.DeliveryLng == 0)
            return BadRequest(new { error = "gps_required", message = "يجب اختيار عنوان أو تفعيل الموقع لإرسال الطلب" });

        var finalLat = (addr != null) ? addr.Latitude : req.DeliveryLat;
        var finalLng = (addr != null) ? addr.Longitude : req.DeliveryLng;
        var finalAddress = (addr != null) ? addr.BuildFullText() : (req.DeliveryAddress ?? customer.DefaultAddress);

        var productIds = req.Items.Where(i => i.ProductId > 0).Select(i => i.ProductId).Distinct().ToList();
        var offerIds = req.Items.Where(i => i.ProductId < 0).Select(i => Math.Abs(i.ProductId)).Distinct().ToList();

        var offerProductLinks = (offerIds.Count == 0)
            ? new List<OfferProduct>()
            : await _db.OfferProducts.AsNoTracking().Where(x => offerIds.Contains(x.OfferId)).ToListAsync();
        var offerPrimaryProduct = offerProductLinks
            .GroupBy(x => x.OfferId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ProductId).FirstOrDefault());

        var linkedProductIds = offerProductLinks.Select(x => x.ProductId).Distinct().ToList();
        foreach (var pid in linkedProductIds)
            if (!productIds.Contains(pid)) productIds.Add(pid);

        var products = (productIds.Count == 0)
            ? new List<Product>()
            : await _db.Products.Where(p => productIds.Contains(p.Id) && p.IsActive).ToListAsync();
        if (products.Count != productIds.Count) return BadRequest(new { error = "بعض الأصناف غير صحيحة" });

        var offers = (offerIds.Count == 0)
            ? new List<Offer>()
            : await _db.Offers.AsNoTracking().Where(o => offerIds.Contains(o.Id) && o.IsActive).ToListAsync();
        if (offers.Count != offerIds.Count) return BadRequest(new { error = "بعض العروض غير صحيحة" });

        var now = DateTime.UtcNow;
        var discounts = await _db.Discounts.AsNoTracking()
            .Where(d => d.IsActive
                        && d.TargetType != DiscountTargetType.Cart 
                        && (d.StartsAtUtc == null || d.StartsAtUtc <= now)
                        && (d.EndsAtUtc == null || d.EndsAtUtc >= now))
            .ToListAsync();

        decimal ApplyDiscount(decimal original, Discount d)
        {
            if (original <= 0) return 0;
            decimal v = original;
            if (d.ValueType == DiscountValueType.Percent)
            {
                var p = d.Percent ?? 0;
                v = original - (original * p / 100m);
            }
            else
            {
                var a = d.Amount ?? 0;
                v = original - a;
            }
            if (v < 0) v = 0;
            return Math.Round(v, 2);
        }

        (decimal finalBasePrice, string? badgeText, decimal? percent) BestDiscountForProduct(int productId, int categoryId, decimal original)
        {
            if (discounts.Count == 0) return (original, null, null);
            var prod = discounts.Where(x => x.TargetType == DiscountTargetType.Product && x.TargetId == productId).ToList();
            var cat = discounts.Where(x => x.TargetType == DiscountTargetType.Category && x.TargetId == categoryId).ToList();
            Discount? best = null;
            decimal bestFinal = original;
            foreach (var d in prod.Concat(cat))
            {
                var f = ApplyDiscount(original, d);
                if (f < bestFinal)
                {
                    bestFinal = f;
                    best = d;
                }
            }
            if (best == null || bestFinal >= original) return (original, null, null);
            decimal? pct = null;
            if (best.ValueType == DiscountValueType.Percent) pct = best.Percent;
            else if (original > 0) pct = Math.Round((1m - (bestFinal / original)) * 100m, 0);
            var badge = !string.IsNullOrWhiteSpace(best.BadgeText) ? best.BadgeText : (pct != null ? $"خصم {pct}%" : "خصم");
            return (bestFinal, badge, pct);
        }

        var variants = (productIds.Count == 0)
            ? new List<ProductVariant>()
            : await _db.ProductVariants.AsNoTracking()
                .Where(v => productIds.Contains(v.ProductId) && v.IsActive)
                .ToListAsync();
        var addons = (productIds.Count == 0)
            ? new List<ProductAddon>()
            : await _db.ProductAddons.AsNoTracking()
                .Where(a => productIds.Contains(a.ProductId) && a.IsActive)
                .ToListAsync();

        decimal subtotalAfter = 0;
        decimal subtotalBefore = 0;
        var orderItems = new List<OrderItem>();
        foreach (var it in req.Items)
        {
            
            if (it.ProductId < 0)
            {
                var oid = Math.Abs(it.ProductId);
                var off = offers.First(x => x.Id == oid);
                decimal baseUnit = (off.PriceAfter ?? off.PriceBefore ?? 0);

                if (baseUnit <= 0)
                {
                    var linked = offerProductLinks.Where(x => x.OfferId == oid).Select(x => x.ProductId).Distinct().ToList();
                    if (linked.Count > 0)
                    {
                        foreach (var pid in linked)
                        {
                            var pr = products.FirstOrDefault(p => p.Id == pid);
                            if (pr != null) baseUnit += pr.Price;
                        }
                    }
                }

                if (baseUnit <= 0) return BadRequest(new { error = "offer_price_missing", offerId = oid });

                int? templateProductId = offerPrimaryProduct.ContainsKey(oid) ? offerPrimaryProduct[oid] : null;

                int? offerVariantId = null;
                List<int> offerAddonIds = new();

				string? offerItemNote = null;
                if (!string.IsNullOrWhiteSpace(it.OptionsSnapshot))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(it.OptionsSnapshot);
                        var root = doc.RootElement;
                        
                        if (root.TryGetProperty("offerVariantId", out var vEl) && vEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                            offerVariantId = vEl.GetInt32();
                        else if (root.TryGetProperty("variantId", out var vEl2) && vEl2.ValueKind == System.Text.Json.JsonValueKind.Number)
                            offerVariantId = vEl2.GetInt32();

                        if (root.TryGetProperty("offerAddonIds", out var aEl) && aEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var x in aEl.EnumerateArray())
                                if (x.ValueKind == System.Text.Json.JsonValueKind.Number) offerAddonIds.Add(x.GetInt32());
                        }
                        else if (root.TryGetProperty("addonIds", out var aEl2) && aEl2.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var x in aEl2.EnumerateArray())
                                if (x.ValueKind == System.Text.Json.JsonValueKind.Number) offerAddonIds.Add(x.GetInt32());
                        }

							if (root.TryGetProperty("note", out var nEl) && nEl.ValueKind == System.Text.Json.JsonValueKind.String)
								offerItemNote = nEl.GetString();
							else if (root.TryGetProperty("offerNote", out var nEl2) && nEl2.ValueKind == System.Text.Json.JsonValueKind.String)
								offerItemNote = nEl2.GetString();
                    }
                    catch { }
                }

                decimal offerVariantDelta = 0;
                decimal offerAddonsSum = 0;
                if (templateProductId != null)
                {
                    if (offerVariantId != null)
                    {
                        var v = variants.FirstOrDefault(x => x.ProductId == templateProductId.Value && x.Id == offerVariantId.Value);
                        if (v != null) offerVariantDelta = v.PriceDelta;
                    }
                    foreach (var aid in offerAddonIds.Distinct())
                    {
                        var a = addons.FirstOrDefault(x => x.ProductId == templateProductId.Value && x.Id == aid);
                        if (a != null) offerAddonsSum += a.Price;
                    }
                }

                var unit = baseUnit + offerVariantDelta + offerAddonsSum;
                subtotalAfter += unit * it.Quantity;
                subtotalBefore += unit * it.Quantity;
                orderItems.Add(new OrderItem
                {
                    ProductId = -oid,
                    ProductNameSnapshot = off.Title,
                    UnitPriceSnapshot = unit,
                    Quantity = it.Quantity,
                    OptionsSnapshot = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        type = "offer",
                        offerId = oid,
                        templateProductId,
                        offerVariantId,
                        offerAddonIds = offerAddonIds.Distinct().OrderBy(x => x).ToList(),
						
						note = string.IsNullOrWhiteSpace(offerItemNote) ? null : offerItemNote
                    })
                });
                continue;
            }

            var p = products.First(x => x.Id == it.ProductId);

            int? variantId = null;
            List<int> addonIds = new();
            string? note = null;
            if (!string.IsNullOrWhiteSpace(it.OptionsSnapshot))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(it.OptionsSnapshot);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("variantId", out var vEl) && vEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                        variantId = vEl.GetInt32();
                    if (root.TryGetProperty("addonIds", out var aEl) && aEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var x in aEl.EnumerateArray())
                            if (x.ValueKind == System.Text.Json.JsonValueKind.Number) addonIds.Add(x.GetInt32());
                    }
                    if (root.TryGetProperty("note", out var nEl) && nEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        note = nEl.GetString();
                }
                catch
                {
                    
                }
            }

            decimal variantDelta = 0;
            string? variantName = null;
            if (variantId != null)
            {
                var v = variants.FirstOrDefault(x => x.ProductId == p.Id && x.Id == variantId.Value);
                if (v != null)
                {
                    variantDelta = v.PriceDelta;
                    variantName = v.Name;
                }
            }

            decimal addonsSum = 0;
            var addonSnapshots = new List<object>();
            foreach (var aid in addonIds.Distinct())
            {
                var a = addons.FirstOrDefault(x => x.ProductId == p.Id && x.Id == aid);
                if (a != null)
                {
                    addonsSum += a.Price;
                    addonSnapshots.Add(new { a.Id, a.Name, a.Price });
                }
            }

            var baseOriginal = p.Price;
            var d = BestDiscountForProduct(p.Id, p.CategoryId, baseOriginal);
            var baseAfter = d.finalBasePrice;

            var unitPriceBefore = baseOriginal + variantDelta + addonsSum;
            var unitPriceAfter = baseAfter + variantDelta + addonsSum;

            subtotalBefore += unitPriceBefore * it.Quantity;
            subtotalAfter += unitPriceAfter * it.Quantity;

            orderItems.Add(new OrderItem
            {
                ProductId = p.Id,
                ProductNameSnapshot = p.Name,
                UnitPriceSnapshot = unitPriceAfter,
                Quantity = it.Quantity,
                OptionsSnapshot = System.Text.Json.JsonSerializer.Serialize(new
                {
                    variantId,
                    variantName,
                    variantDelta,
                    addons = addonSnapshots,
                    note,
                    discount = new
                    {
                        baseOriginal,
                        baseAfter,
                        percent = d.percent,
                        badge = d.badgeText
                    }
                })
            });
        }

        if (subtotalAfter < settings.MinOrderAmount)
            return BadRequest(new { error = "الطلب أقل من الحد الأدنى", minOrder = settings.MinOrderAmount });

        double deliveryDistanceKm = 0;
        if (settings.StoreLat != 0 || settings.StoreLng != 0)
            deliveryDistanceKm = HaversineKm(settings.StoreLat, settings.StoreLng, finalLat, finalLng);

        decimal deliveryFee;
        if (settings.DeliveryFeePerKm > 0 && deliveryDistanceKm >= 0)
        {
            deliveryFee = Math.Round((decimal)deliveryDistanceKm * settings.DeliveryFeePerKm, 2);
        }
        else if (settings.DeliveryFeeType == DeliveryFeeType.ByZone)
        {
            // ByZone: search matching zone by order subtotal
            var zones = await _db.DeliveryZones.AsNoTracking()
                .Where(z => z.IsActive)
                .OrderBy(z => z.SortOrder)
                .ToListAsync();
            var matched = zones.FirstOrDefault(z => z.MinOrder == null || subtotalAfter >= z.MinOrder);
            deliveryFee = matched?.Fee ?? settings.DeliveryFeeValue;
        }
        else
        {
            deliveryFee = settings.DeliveryFeeValue;
        }

        var totalBeforeDiscount = subtotalBefore + deliveryFee;
        var cartDiscount = subtotalBefore - subtotalAfter;
        if (cartDiscount < 0) cartDiscount = 0;
        var total = (subtotalAfter + deliveryFee);

        customer.LastLat = finalLat;
        customer.LastLng = finalLng;

        var order = new Order
        {
            CustomerId = customer.Id,
            IdempotencyKey = key,
            CustomerAddressId = addr?.Id,
            DeliveryLat = finalLat,
            DeliveryLng = finalLng,
            DeliveryAddress = finalAddress,
            DeliveryDistanceKm = Math.Round(deliveryDistanceKm, 3),
            Notes = req.Notes,
            Subtotal = subtotalAfter,
            DeliveryFee = deliveryFee,
            TotalBeforeDiscount = totalBeforeDiscount,
            CartDiscount = cartDiscount,
            Total = total,
            OrderEditableUntilUtc = DateTime.UtcNow.AddMinutes(5),
            CurrentStatus = OrderStatus.New,
            PaymentMethod = req.PaymentMethod,
            Items = orderItems,
            StatusHistory = new List<OrderStatusHistory>
            {
                new()
                {
                    Status = OrderStatus.New,
                    Comment = "تم إنشاء الطلب",
                    ReasonCode = "created",
                    ChangedByType = "customer",
                    ChangedById = customer.Id
                }
            }
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        // تخفيض المخزون للمنتجات التي تتبع المخزون
        var stockChangedProductIds = new List<int>();
        foreach (var oi in orderItems)
        {
            if (oi.ProductId > 0)
            {
                var prod = products.FirstOrDefault(p => p.Id == oi.ProductId);
                if (prod != null && prod.TrackStock && prod.StockQuantity > 0)
                {
                    prod.StockQuantity = Math.Max(0, prod.StockQuantity - oi.Quantity);
                    if (prod.StockQuantity == 0)
                    {
                        prod.IsAvailable = false;
                        stockChangedProductIds.Add(prod.Id);
                    }
                }
            }
        }
        if (products.Any(p => p.TrackStock))
        {
            await _db.SaveChangesAsync();
            // إشعار real-time لتطبيق الزبون ولوحة التحكم عند نفاد المخزون
            if (stockChangedProductIds.Any())
            {
                await _hub.Clients.All.SendAsync("menu_updated");
                await _hub.Clients.Group("admin").SendAsync("stock_depleted", new { productIds = stockChangedProductIds });
            }
        }

        var agentProductMap = products
            .Where(p => p.AgentId.HasValue)
            .GroupBy(p => p.AgentId!.Value)
            .ToList();

        if (agentProductMap.Any())
        {
            var agents = await _db.Agents
                .Where(a => agentProductMap.Select(g => g.Key).Contains(a.Id))
                .ToListAsync();

            foreach (var grp in agentProductMap)
            {
                var agent = agents.FirstOrDefault(a => a.Id == grp.Key);
                if (agent == null) continue;

                var agentSubtotal = orderItems
                    .Where(oi => grp.Select(p => p.Id).Contains(oi.ProductId))
                    .Sum(oi => oi.UnitPriceSnapshot * oi.Quantity);

                var oai = new OrderAgentItem
                {
                    OrderId = order.Id,
                    AgentId = grp.Key,
                    AgentStatus = AgentOrderStatus.Pending,
                    AutoAcceptAt = DateTime.UtcNow.AddMinutes(30),
                    CommissionPercent = agent.CommissionPercent,
                    AgentSubtotal = agentSubtotal,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _db.OrderAgentItems.Add(oai);

                _db.Notifications.Add(new Notification
                {
                    UserType = NotificationUserType.Agent,
                    UserId = grp.Key,
                    Title = "طلب جديد",
                    Body = $"لديك طلب جديد #{order.Id} بقيمة {agentSubtotal:0.##}",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
        }

        await _hub.Clients.Group("admin").SendAsync("order_new", new { order.Id, order.Total, order.CreatedAtUtc });
        await _hub.Clients.Group($"customer-{customer.Id}").SendAsync("order_status", new { orderId = order.Id, status = order.CurrentStatus });

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "طلب جديد", $"طلب جديد رقم #{order.Id} بقيمة {order.Total:0.##}", order.Id);
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, customer.Id,
            "تم استلام طلبك", $"تم إنشاء طلبك رقم #{order.Id} بنجاح", order.Id);

        await _notifications.SendCustomerOrderStatusPushIfNeededAsync(customer.Id, order.Id, order.CurrentStatus, order.ProcessingEtaMinutes, order.DeliveryEtaMinutes);

        return Ok(new { id = order.Id });
    }

    [HttpGet("orders/{customerId:int}")]
    public async Task<IActionResult> ListOrders(int customerId)
    {

        var now = DateTime.UtcNow;

        var raw = await _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync();

        var orders = raw.Select(o =>
        {
            
            var createdUtc = DateTime.SpecifyKind(o.CreatedAtUtc, DateTimeKind.Utc);
            DateTime? editableUtc = o.OrderEditableUntilUtc.HasValue
                ? DateTime.SpecifyKind(o.OrderEditableUntilUtc.Value, DateTimeKind.Utc)
                : null;
            DateTime? expectedUtc = o.ExpectedDeliveryAtUtc.HasValue
                ? DateTime.SpecifyKind(o.ExpectedDeliveryAtUtc.Value, DateTimeKind.Utc)
                : null;
            DateTime? lastEtaUtc = o.LastEtaUpdatedAtUtc.HasValue
                ? DateTime.SpecifyKind(o.LastEtaUpdatedAtUtc.Value, DateTimeKind.Utc)
                : null;

            var canCancel = (now - createdUtc) <= TimeSpan.FromMinutes(2)
                            && o.CurrentStatus != OrderStatus.Delivered
                            && o.CurrentStatus != OrderStatus.Cancelled;

            return new
            {
                o.Id,
                o.CurrentStatus,
                o.Total,
                orderEditableUntilUtc = editableUtc,
                canEdit = (editableUtc != null && now <= editableUtc
                           && o.CurrentStatus != OrderStatus.Delivered && o.CurrentStatus != OrderStatus.Cancelled),
                canCancel,
                createdAtUtc = createdUtc,
                o.ProcessingEtaMinutes,
                o.DeliveryEtaMinutes,
                expectedDeliveryAtUtc = expectedUtc,
                lastEtaUpdatedAtUtc = lastEtaUtc
            };
        }).ToList();

        return Ok(orders);
    }

    [HttpGet("order/{orderId:int}")]
    public async Task<IActionResult> GetOrder(int orderId)
    {
        var o = await _db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound(new { error = "not_found" });

        var rating = await _db.OrderRatings.AsNoTracking().FirstOrDefaultAsync(r => r.OrderId == o.Id);

        var createdUtc = DateTime.SpecifyKind(o.CreatedAtUtc, DateTimeKind.Utc);
        DateTime? editableUtc = o.OrderEditableUntilUtc.HasValue
            ? DateTime.SpecifyKind(o.OrderEditableUntilUtc.Value, DateTimeKind.Utc)
            : null;
        DateTime? expectedUtc = o.ExpectedDeliveryAtUtc.HasValue
            ? DateTime.SpecifyKind(o.ExpectedDeliveryAtUtc.Value, DateTimeKind.Utc)
            : null;
        DateTime? lastEtaUtc = o.LastEtaUpdatedAtUtc.HasValue
            ? DateTime.SpecifyKind(o.LastEtaUpdatedAtUtc.Value, DateTimeKind.Utc)
            : null;

        var canEdit = editableUtc != null && DateTime.UtcNow <= editableUtc && o.CurrentStatus != OrderStatus.Delivered && o.CurrentStatus != OrderStatus.Cancelled;
        
        var canCancel = (DateTime.UtcNow - createdUtc) <= TimeSpan.FromMinutes(2)
                        && o.CurrentStatus != OrderStatus.Delivered
                        && o.CurrentStatus != OrderStatus.Cancelled;

        var editedByCustomer = o.StatusHistory != null && o.StatusHistory.Any(h =>
            (h.ReasonCode ?? "") == "customer_edit" ||
            ((h.Comment ?? "").Contains("تم تعديل الطلب")));
        return Ok(new
        {
            o.Id,
            o.CustomerId,
            o.DriverId,
            o.CurrentStatus,
            o.Subtotal,
            o.DeliveryFee,
            o.Total,
            createdAtUtc = createdUtc,
            orderEditableUntilUtc = editableUtc,
            canEdit,
            canCancel,
            editedByCustomer,
            o.DeliveryLat,
            o.DeliveryLng,
            o.DeliveryAddress,
            o.Notes,
            o.ProcessingEtaMinutes,
            o.DeliveryEtaMinutes,
            expectedDeliveryAtUtc = expectedUtc,
            lastEtaUpdatedAtUtc = lastEtaUtc,
            orderRating = rating == null ? null : new { rating.OrderId, storeRate = rating.StoreRate, driverRate = rating.DriverRate, storeComment = rating.StoreComment ?? rating.Comment, driverComment = rating.DriverComment, createdAtUtc = DateTime.SpecifyKind(rating.CreatedAtUtc, DateTimeKind.Utc) },
            items = o.Items.Select(i => new { i.ProductId, i.ProductNameSnapshot, i.UnitPriceSnapshot, i.Quantity, i.OptionsSnapshot }),
            history = o.StatusHistory.OrderBy(h => h.ChangedAtUtc).Select(h => new { h.Status, changedAtUtc = DateTime.SpecifyKind(h.ChangedAtUtc, DateTimeKind.Utc), h.Comment })
        });
    }

    public record EditOrderRequest(
        int CustomerId,
        List<OrderItemReq> Items,
        string? Notes,
        double? DeliveryLat,
        double? DeliveryLng,
        string? DeliveryAddress
    );

    [HttpPost("order/{orderId:int}/edit")]
    public async Task<IActionResult> EditOrder(int orderId, EditOrderRequest req)
    {
        var o = await _db.Orders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound(new { error = "not_found" });
        if (o.CustomerId != req.CustomerId) return Forbid();

        var editableUntilUtc = o.OrderEditableUntilUtc.HasValue
            ? DateTime.SpecifyKind(o.OrderEditableUntilUtc.Value, DateTimeKind.Utc)
            : (DateTime?)null;

        if (editableUntilUtc == null || DateTime.UtcNow > editableUntilUtc)
            return BadRequest(new { error = "edit_window_closed", message = "انتهت مدة التعديل" });

        if (o.CurrentStatus == OrderStatus.Delivered || o.CurrentStatus == OrderStatus.Cancelled)
            return BadRequest(new { error = "not_editable", message = "لا يمكن تعديل هذا الطلب" });

        if (req.Items == null || req.Items.Count == 0)
            return BadRequest(new { error = "empty_items", message = "لا يمكن أن يكون الطلب فارغاً" });

        var productIds = req.Items.Where(i => i.ProductId > 0).Select(i => i.ProductId).Distinct().ToList();
        var offerIds = req.Items.Where(i => i.ProductId < 0).Select(i => Math.Abs(i.ProductId)).Distinct().ToList();

        var offerProductLinks = (offerIds.Count == 0)
            ? new List<OfferProduct>()
            : await _db.OfferProducts.AsNoTracking().Where(x => offerIds.Contains(x.OfferId)).ToListAsync();
        var offerPrimaryProduct = offerProductLinks
            .GroupBy(x => x.OfferId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ProductId).FirstOrDefault());

        var linkedProductIds = offerProductLinks.Select(x => x.ProductId).Distinct().ToList();
        foreach (var pid in linkedProductIds)
            if (!productIds.Contains(pid)) productIds.Add(pid);

        var products = (productIds.Count == 0)
            ? new List<Product>()
            : await _db.Products.Where(p => productIds.Contains(p.Id) && p.IsActive).ToListAsync();
        if (products.Count != productIds.Count)
            return BadRequest(new { error = "invalid_items", message = "بعض الأصناف غير صحيحة" });

        var offers = (offerIds.Count == 0)
            ? new List<Offer>()
            : await _db.Offers.AsNoTracking().Where(o2 => offerIds.Contains(o2.Id) && o2.IsActive).ToListAsync();
        if (offers.Count != offerIds.Count)
            return BadRequest(new { error = "invalid_items", message = "بعض العروض غير صحيحة" });

        var variants = (productIds.Count == 0)
            ? new List<ProductVariant>()
            : await _db.ProductVariants.AsNoTracking()
                .Where(v => productIds.Contains(v.ProductId) && v.IsActive)
                .ToListAsync();
        var addons = (productIds.Count == 0)
            ? new List<ProductAddon>()
            : await _db.ProductAddons.AsNoTracking()
                .Where(a => productIds.Contains(a.ProductId) && a.IsActive)
                .ToListAsync();

        var now = DateTime.UtcNow;
        var discounts = await _db.Discounts.AsNoTracking()
            .Where(d => d.IsActive
                        && d.TargetType != DiscountTargetType.Cart
                        && (d.StartsAtUtc == null || d.StartsAtUtc <= now)
                        && (d.EndsAtUtc == null || d.EndsAtUtc >= now))
            .ToListAsync();

        decimal ApplyDiscount(decimal original, Discount d)
        {
            if (original <= 0) return 0;
            decimal v = original;
            if (d.ValueType == DiscountValueType.Percent)
            {
                var p = d.Percent ?? 0;
                v = original - (original * p / 100m);
            }
            else
            {
                var a = d.Amount ?? 0;
                v = original - a;
            }
            if (v < 0) v = 0;
            return Math.Round(v, 2);
        }

        (decimal finalBasePrice, string? badgeText, decimal? percent) BestDiscountForProduct(int productId, int categoryId, decimal original)
        {
            if (discounts.Count == 0) return (original, null, null);
            var prod = discounts.Where(x => x.TargetType == DiscountTargetType.Product && x.TargetId == productId).ToList();
            var cat = discounts.Where(x => x.TargetType == DiscountTargetType.Category && x.TargetId == categoryId).ToList();
            Discount? best = null;
            decimal bestFinal = original;
            foreach (var d in prod.Concat(cat))
            {
                var f = ApplyDiscount(original, d);
                if (f < bestFinal)
                {
                    bestFinal = f;
                    best = d;
                }
            }
            if (best == null || bestFinal >= original) return (original, null, null);
            decimal? pct = null;
            if (best.ValueType == DiscountValueType.Percent) pct = best.Percent;
            else if (original > 0) pct = Math.Round((1m - (bestFinal / original)) * 100m, 0);
            var badge = !string.IsNullOrWhiteSpace(best.BadgeText) ? best.BadgeText : (pct != null ? $"خصم {pct}%" : "خصم");
            return (bestFinal, badge, pct);
        }

        decimal subtotalAfter = 0;
        decimal subtotalBefore = 0;
        var newItems = new List<OrderItem>();
        foreach (var it in req.Items)
        {
            
            if (it.ProductId < 0)
            {
                var oid = Math.Abs(it.ProductId);
                var off = offers.First(x => x.Id == oid);

                decimal baseUnit = (off.PriceAfter ?? off.PriceBefore ?? 0);
                if (baseUnit <= 0)
                {
                    var linked = offerProductLinks.Where(x => x.OfferId == oid).Select(x => x.ProductId).Distinct().ToList();
                    if (linked.Count > 0)
                    {
                        foreach (var pid in linked)
                        {
                            var pr = products.FirstOrDefault(p2 => p2.Id == pid);
                            if (pr != null) baseUnit += pr.Price;
                        }
                    }
                }
                if (baseUnit <= 0)
                    return BadRequest(new { error = "offer_price_missing", offerId = oid, message = "سعر العرض غير متوفر" });

                int? templateProductId = offerPrimaryProduct.ContainsKey(oid) ? offerPrimaryProduct[oid] : null;

                int? offerVariantId = null;
                List<int> offerAddonIds = new();

                string? offerItemNote = null;
                if (!string.IsNullOrWhiteSpace(it.OptionsSnapshot))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(it.OptionsSnapshot);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("offerVariantId", out var vEl) && vEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                            offerVariantId = vEl.GetInt32();
                        else if (root.TryGetProperty("variantId", out var vEl2) && vEl2.ValueKind == System.Text.Json.JsonValueKind.Number)
                            offerVariantId = vEl2.GetInt32();

                        if (root.TryGetProperty("offerAddonIds", out var aEl) && aEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var x in aEl.EnumerateArray())
                                if (x.ValueKind == System.Text.Json.JsonValueKind.Number) offerAddonIds.Add(x.GetInt32());
                        }
                        else if (root.TryGetProperty("addonIds", out var aEl2) && aEl2.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var x in aEl2.EnumerateArray())
                                if (x.ValueKind == System.Text.Json.JsonValueKind.Number) offerAddonIds.Add(x.GetInt32());
                        }

							if (root.TryGetProperty("note", out var nEl) && nEl.ValueKind == System.Text.Json.JsonValueKind.String)
								offerItemNote = nEl.GetString();
							else if (root.TryGetProperty("offerNote", out var nEl2) && nEl2.ValueKind == System.Text.Json.JsonValueKind.String)
								offerItemNote = nEl2.GetString();
                    }
                    catch { }
                }

                decimal offerVariantDelta = 0;
                decimal offerAddonsSum = 0;
                if (templateProductId != null)
                {
                    if (offerVariantId != null)
                    {
                        var v = variants.FirstOrDefault(x => x.ProductId == templateProductId.Value && x.Id == offerVariantId.Value);
                        if (v != null) offerVariantDelta = v.PriceDelta;
                    }
                    foreach (var aid in offerAddonIds.Distinct())
                    {
                        var a = addons.FirstOrDefault(x => x.ProductId == templateProductId.Value && x.Id == aid);
                        if (a != null) offerAddonsSum += a.Price;
                    }
                }

                var unit = baseUnit + offerVariantDelta + offerAddonsSum;
                if (it.Quantity < 1)
                    return BadRequest(new { error = "invalid_qty", message = "الكمية غير صحيحة" });

                subtotalBefore += unit * it.Quantity;
                subtotalAfter += unit * it.Quantity;

                newItems.Add(new OrderItem
                {
                    OrderId = o.Id,
                    ProductId = -oid,
                    ProductNameSnapshot = off.Title,
                    UnitPriceSnapshot = unit,
                    Quantity = it.Quantity,
                    OptionsSnapshot = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        type = "offer",
                        offerId = oid,
                        templateProductId,
                        offerVariantId,
                        offerAddonIds = offerAddonIds.Distinct().OrderBy(x => x).ToList(),
						
						note = string.IsNullOrWhiteSpace(offerItemNote) ? null : offerItemNote
                    })
                });
                continue;
            }

            var p = products.First(x => x.Id == it.ProductId);

            int? variantId = null;
            List<int> addonIds = new();
            if (!string.IsNullOrWhiteSpace(it.OptionsSnapshot))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(it.OptionsSnapshot);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("variantId", out var vEl) && vEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                        variantId = vEl.GetInt32();
                    if (root.TryGetProperty("addonIds", out var aEl) && aEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var x in aEl.EnumerateArray())
                            if (x.ValueKind == System.Text.Json.JsonValueKind.Number) addonIds.Add(x.GetInt32());
                    }
                }
                catch { }
            }

	            decimal variantDelta = 0;
            string? variantName = null;
            if (variantId != null)
            {
                var v = variants.FirstOrDefault(x => x.ProductId == p.Id && x.Id == variantId.Value);
                if (v != null)
                {
                    variantName = v.Name;
	                    
	                    variantDelta = v.PriceDelta;
                }
            }

            decimal addonsTotal = 0;
            if (addonIds.Count > 0)
            {
                foreach (var aid in addonIds)
                {
                    var a = addons.FirstOrDefault(x => x.ProductId == p.Id && x.Id == aid);
                    if (a != null)
                    {
	                        
	                        addonsTotal += a.Price;
                    }
                }
            }

            var baseOriginal = p.Price;
            var d = BestDiscountForProduct(p.Id, p.CategoryId, baseOriginal);
            var baseAfter = d.finalBasePrice;

            var unitBefore = baseOriginal + variantDelta + addonsTotal;
            var unitAfter = baseAfter + variantDelta + addonsTotal;
            if (it.Quantity < 1) return BadRequest(new { error = "invalid_qty", message = "الكمية غير صحيحة" });

            subtotalBefore += unitBefore * it.Quantity;
            subtotalAfter += unitAfter * it.Quantity;

            newItems.Add(new OrderItem
            {
                OrderId = o.Id,
                ProductId = p.Id,
                ProductNameSnapshot = p.Name,
                UnitPriceSnapshot = unitAfter,
                Quantity = it.Quantity,
                OptionsSnapshot = System.Text.Json.JsonSerializer.Serialize(new
                {
                    variantId,
                    variantName,
                    variantDelta,
                    addonIds,
                    discount = new
                    {
                        baseOriginal,
                        baseAfter,
                        percent = d.percent,
                        badge = d.badgeText
                    }
                })
            });
        }

        if (req.DeliveryLat != null && req.DeliveryLng != null)
        {
            var lat = req.DeliveryLat.Value;
            var lng = req.DeliveryLng.Value;
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180)
                return BadRequest(new { error = "invalid_location", message = "الموقع غير صحيح" });

            o.DeliveryLat = lat;
            o.DeliveryLng = lng;
            if (!string.IsNullOrWhiteSpace(req.DeliveryAddress))
                o.DeliveryAddress = req.DeliveryAddress.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(req.DeliveryAddress))
        {
            
            o.DeliveryAddress = req.DeliveryAddress.Trim();
        }

        var settings = await _db.StoreSettings.AsNoTracking().FirstOrDefaultAsync();
        decimal deliveryFee = o.DeliveryFee;
        if (settings != null)
        {
            if (settings.DeliveryFeeType == DeliveryFeeType.Fixed)
                deliveryFee = settings.DeliveryFeeValue;
        }

        o.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        o.Subtotal = subtotalAfter;
        o.DeliveryFee = deliveryFee;
        o.TotalBeforeDiscount = subtotalBefore + deliveryFee;
        o.CartDiscount = Math.Max(0, subtotalBefore - subtotalAfter);
        o.Total = subtotalAfter + deliveryFee;

        o.StatusHistory.Add(new OrderStatusHistory
        {
            OrderId = o.Id,
            Status = o.CurrentStatus,
            ChangedAtUtc = DateTime.UtcNow,
            ChangedByType = "customer",
            ChangedById = o.CustomerId,
            ReasonCode = "customer_edit",
            Comment = "تم تعديل الطلب من قبل الزبون"
        });

        _db.OrderItems.RemoveRange(o.Items);
        await _db.SaveChangesAsync(); 
        _db.OrderItems.AddRange(newItems);
        await _db.SaveChangesAsync();

        var custName = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == o.CustomerId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync();
        custName = string.IsNullOrWhiteSpace(custName) ? $"#{o.CustomerId}" : custName.Trim();

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "تم تعديل الطلب",
            $"قام {custName} بتعديل الطلب رقم #{o.Id}",
            relatedOrderId: o.Id);

        await _hub.Clients.Group("admin").SendAsync("order_edited", new { orderId = o.Id, customerName = custName, o.Subtotal, o.DeliveryFee, o.Total });
        await _hub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_edited", new { orderId = o.Id });

        return Ok(new { ok = true, orderId = o.Id, editableUntilUtc = o.OrderEditableUntilUtc, o.Subtotal, o.DeliveryFee, o.Total });
    }

    [HttpGet("pending-rating/{customerId:int}")]
    public async Task<IActionResult> PendingRating(int customerId)
    {
        var lastDelivered = await _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.CurrentStatus == OrderStatus.Delivered)
            .OrderByDescending(o => o.DeliveredAtUtc ?? o.CreatedAtUtc)
            .Select(o => new { o.Id, o.DeliveredAtUtc, o.DriverId })
            .FirstOrDefaultAsync();

        if (lastDelivered == null) return Ok(new { hasPending = false });

        var r2 = await _db.OrderRatings.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == lastDelivered.Id);
        var storeOk = r2 != null && r2.StoreRate >= 1 && r2.StoreRate <= 5;
        var driverOk = lastDelivered.DriverId == null || (r2 != null && r2.DriverRate >= 1 && r2.DriverRate <= 5);
        if (storeOk && driverOk) return Ok(new { hasPending = false });

        return Ok(new { hasPending = true, orderId = lastDelivered.Id, hasDriver = lastDelivered.DriverId != null });
    }

    public record CancelOrderReq(int CustomerId, string? Reason = null, string? ReasonCode = null);

    private static readonly Dictionary<string, string> CancelReasonLabelsLegacy = new()
    {
        ["changed_mind"] = "غيرت رأيي",
        ["wrong_items"] = "طلبت أصناف بالخطأ",
        ["wrong_address"] = "العنوان غير صحيح",
        ["too_expensive"] = "السعر مرتفع",
        ["other"] = "سبب آخر"
    };

[HttpPost("order/{orderId:int}/cancel")]
	    public async Task<IActionResult> CancelOrder(int orderId, CancelOrderReq req)
    {
        var o = await _db.Orders
            .Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound(new { error = "not_found" });
        if (o.CustomerId != req.CustomerId) return Forbid();

        if (o.CurrentStatus == OrderStatus.Delivered || o.CurrentStatus == OrderStatus.Cancelled)
            return BadRequest(new { error = "cannot_cancel", message = "لا يمكن إلغاء هذا الطلب" });

        var createdUtc = DateTime.SpecifyKind(o.CreatedAtUtc, DateTimeKind.Utc);
        if ((DateTime.UtcNow - createdUtc) > TimeSpan.FromMinutes(2))
            return BadRequest(new { error = "cancel_window_closed", message = "لم يعد بإمكانك إلغاء الطلب. راجع الإدارة في قسم الدردشة أو اتصال." });

        var reason = (req.Reason ?? "").Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            var legacy = (req.ReasonCode ?? "").Trim();
            reason = CancelReasonLabelsLegacy.TryGetValue(legacy, out var l) ? l : "";
        }
        if (string.IsNullOrWhiteSpace(reason))
            return BadRequest(new { error = "reason_required", message = "يرجى كتابة سبب الإلغاء" });

        o.CurrentStatus = OrderStatus.Cancelled;
        o.CancelReasonCode = reason.Length <= 80 ? reason : reason[..80];

        var reasonForHistory = reason.Length <= 200 ? reason : reason[..200];
        _db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = o.Id,
            Status = OrderStatus.Cancelled,
            ReasonCode = "customer_cancel",
            Comment = $"ملغي من قبل الزبون — {reasonForHistory}",
            ChangedByType = "customer",
            ChangedById = req.CustomerId,
            ChangedAtUtc = DateTime.UtcNow
        });

        if (o.DriverId.HasValue)
        {
            var d = await _db.Drivers.FindAsync(o.DriverId.Value);
            if (d != null) d.Status = DriverStatus.Available;
        }

        await _db.SaveChangesAsync();

        await _hub.Clients.Group("admin").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId });
        await _hub.Clients.Group($"customer-{o.CustomerId}").SendAsync("order_status", new { orderId = o.Id, status = o.CurrentStatus, driverId = o.DriverId });

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "إلغاء من الزبون", $"الزبون ألغى الطلب #{o.Id} — {reasonForHistory}", o.Id);

        return Ok(new { ok = true });
    }

    public record RateDriverReq(int CustomerId, int Stars, string? Comment);

    [HttpPost("order/{orderId:int}/rate-driver")]
    public async Task<IActionResult> RateDriver(int orderId, RateDriverReq req)
    {
        if (req.Stars < 1 || req.Stars > 5)
            return BadRequest(new { error = "invalid_stars", message = "التقييم يجب أن يكون بين 1 و 5" });

        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound(new { error = "not_found" });
        if (o.CustomerId != req.CustomerId)
            return Forbid();
        if (o.CurrentStatus != OrderStatus.Delivered)
        {
            var hasDelivered = await _db.OrderStatusHistory.AsNoTracking()
                .AnyAsync(h => h.OrderId == orderId && h.Status == OrderStatus.Delivered);
            if (!hasDelivered) return BadRequest(new { error = "not_delivered", message = "يمكن التقييم بعد تسليم الطلب فقط" });
        }
        if (o.DriverId == null)
            return BadRequest(new { error = "no_driver", message = "لا يوجد سائق لهذا الطلب" });

        var or = await _db.OrderRatings.FirstOrDefaultAsync(x => x.OrderId == o.Id);
        if (or == null)
        {
            // StoreRate = 0 يعني لم يُقيَّم المتجر بعد (الزبون سيقيّمه لاحقاً)
            or = new OrderRating
            {
                OrderId = o.Id,
                StoreRate = 0,
                DriverRate = req.Stars,
                DriverComment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.OrderRatings.Add(or);
        }
        else
        {
            or.DriverRate = req.Stars;
            if (!string.IsNullOrWhiteSpace(req.Comment))
                or.DriverComment = req.Comment.Trim();
            or.CreatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();

        await _hub.Clients.Group("admin").SendAsync("rating_added", new { orderId = o.Id, storeRate = or.StoreRate, driverRate = or.DriverRate, or.CreatedAtUtc });
        await _hub.Clients.All.SendAsync("ratings_updated");

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "تقييم جديد", $"تم إضافة/تحديث تقييم الطلب #{o.Id}", o.Id);
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, o.CustomerId,
            "شكراً لتقييمك", "تم حفظ تقييمك بنجاح", o.Id);

        return Ok(new { ok = true, rating = new { orderId = or.OrderId, storeRate = or.StoreRate, driverRate = or.DriverRate, storeComment = or.StoreComment, driverComment = or.DriverComment, or.CreatedAtUtc } });
    }

    public record RateStoreReq(int CustomerId, int Stars, string? Comment);

    [HttpPost("order/{orderId:int}/rate-store")]
    public async Task<IActionResult> RateStore(int orderId, RateStoreReq req)
    {
        if (req.Stars < 1 || req.Stars > 5)
            return BadRequest(new { error = "invalid_stars", message = "التقييم يجب أن يكون بين 1 و 5" });

        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return NotFound(new { error = "not_found" });
        if (o.CustomerId != req.CustomerId)
            return Forbid();
        if (o.CurrentStatus != OrderStatus.Delivered)
        {
            var hasDeliveredHistory3 = await _db.OrderStatusHistory.AsNoTracking()
                .AnyAsync(h => h.OrderId == orderId && h.Status == OrderStatus.Delivered);
            if (!hasDeliveredHistory3) return BadRequest(new { error = "not_delivered", message = "يمكن التقييم بعد تسليم الطلب فقط" });
        }

        var or = await _db.OrderRatings.FirstOrDefaultAsync(x => x.OrderId == o.Id);
        if (or == null)
        {
            // DriverRate = 0 يعني لم يُقيَّم السائق بعد (الزبون سيقيّمه لاحقاً إن وُجد سائق)
            or = new OrderRating
            {
                OrderId = o.Id,
                StoreRate = req.Stars,
                DriverRate = 0,
                StoreComment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.OrderRatings.Add(or);
        }
        else
        {
            or.StoreRate = req.Stars;
            if (!string.IsNullOrWhiteSpace(req.Comment))
                or.StoreComment = req.Comment.Trim();
            or.CreatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        await _hub.Clients.Group("admin").SendAsync("rating_added", new { orderId = o.Id, storeRate = or.StoreRate, driverRate = or.DriverRate, or.CreatedAtUtc });
        await _hub.Clients.All.SendAsync("ratings_updated");

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "تقييم جديد", $"تم إضافة/تحديث تقييم الطلب #{o.Id}", o.Id);
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, o.CustomerId,
            "شكراً لتقييمك", "تم حفظ تقييمك بنجاح", o.Id);

        return Ok(new { ok = true, rating = new { orderId = or.OrderId, storeRate = or.StoreRate, driverRate = or.DriverRate, storeComment = or.StoreComment, driverComment = or.DriverComment, or.CreatedAtUtc } });
    }

    public record CreateThreadReq(int CustomerId, int? OrderId, string Title, string Message);

    [HttpGet("chat-thread/{customerId:int}")]
    public async Task<IActionResult> GetOrCreateChatThread(int customerId)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null) return NotFound(new { error = "not_found" });

        var now = DateTime.UtcNow;
        var thread = await _db.ComplaintThreads
            .OrderByDescending(t => t.UpdatedAtUtc)
            .FirstOrDefaultAsync(t => t.CustomerId == customerId);

        if (thread == null)
        {
            thread = new ComplaintThread
            {
                CustomerId = customerId,
                OrderId = null,
                Title = "دردشة مع المتجر",
                UpdatedAtUtc = now,
                CreatedAtUtc = now,
                LastCustomerSeenAtUtc = now
            };
            _db.ComplaintThreads.Add(thread);
            await _db.SaveChangesAsync();
        }

        return Ok(new { threadId = thread.Id, customerId, isChatBlocked = customer.IsChatBlocked });
    }

    [HttpPost("complaints")]
    public async Task<IActionResult> CreateComplaint(CreateThreadReq req)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CustomerId);
        if (customer == null) return NotFound(new { error = "not_found" });
        if (customer.IsChatBlocked)
            return StatusCode(403, new { error = "chat_blocked", message = "تم إيقاف الدردشة من قبل الإدارة" });

        var now = DateTime.UtcNow;
        var cleanMsg = (req.Message ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cleanMsg))
            return BadRequest(new { error = "empty_message", message = "الرسالة فارغة" });

        var thread = await _db.ComplaintThreads
            .OrderByDescending(t => t.UpdatedAtUtc)
            .FirstOrDefaultAsync(t => t.CustomerId == req.CustomerId);

        var isNew = false;
        if (thread == null)
        {
            isNew = true;
            thread = new ComplaintThread
            {
                CustomerId = req.CustomerId,
                OrderId = req.OrderId,
                Title = "دردشة مع المتجر",
                UpdatedAtUtc = now,
                LastCustomerSeenAtUtc = now
            };
            _db.ComplaintThreads.Add(thread);
            await _db.SaveChangesAsync();
        }

        var msg = new ComplaintMessage { ThreadId = thread.Id, FromAdmin = false, Message = cleanMsg };
        _db.ComplaintMessages.Add(msg);
        thread.UpdatedAtUtc = now;
        thread.LastCustomerSeenAtUtc = now;
        if (thread.OrderId == null && req.OrderId != null) thread.OrderId = req.OrderId;
        await _db.SaveChangesAsync();

        if (isNew)
        {
            await _hub.Clients.Group("admin").SendAsync("complaint_new", new { thread.Id, thread.Title, thread.CustomerId, thread.OrderId });
        }

        var payload = new { id = msg.Id, threadId = thread.Id, fromAdmin = false, message = cleanMsg, createdAtUtc = msg.CreatedAtUtc };
        await _hub.Clients.Group("admin").SendAsync("chat_message_received", payload);
        await _hub.Clients.Group($"customer-{thread.CustomerId}").SendAsync("chat_message_received", payload);

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "رسالة جديدة", "رسالة جديدة من زبون", thread.OrderId);
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, thread.CustomerId,
            "تم إرسال رسالتك", "تم استلام رسالتك وسنقوم بالرد بأقرب وقت", thread.OrderId);

        // الرد التلقائي عبر Groq AI — scope منفصل لأن DbContext scoped
        var threadIdSnap = thread.Id;
        var customerIdSnap = thread.CustomerId;
        var orderIdSnap = thread.OrderId;
        var scopeFactory1 = _scopeFactory;
        var hub1 = _hub;
        var httpFactory1 = _httpFactory;
        var config1 = _config;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1200);

                using var scope1 = scopeFactory1.CreateScope();
                var db1 = scope1.ServiceProvider.GetRequiredService<AppDbContext>();

                var storeSettings = await db1.StoreSettings.AsNoTracking().FirstOrDefaultAsync();
                if (storeSettings?.AiAutoReplyEnabled == false) return;

                var groqApiKey = config1["Groq:ApiKey"] ?? "";
                if (string.IsNullOrWhiteSpace(groqApiKey)) return;

                var groqModel = "llama-3.3-70b-versatile";
                var storeName = storeSettings?.StoreName?.Trim() ?? config1["Store:Name"] ?? "المتجر";

                var recentMsgs = await db1.ComplaintMessages
                    .Where(m => m.ThreadId == threadIdSnap)
                    .OrderByDescending(m => m.CreatedAtUtc)
                    .Take(8)
                    .OrderBy(m => m.CreatedAtUtc)
                    .Select(m => new { m.FromAdmin, m.Message })
                    .ToListAsync();

                var customerInfo = await db1.Customers.AsNoTracking()
                    .Where(c => c.Id == customerIdSnap)
                    .Select(c => new { c.Name, c.Phone })
                    .FirstOrDefaultAsync();

                var recentOrders = await db1.Orders.AsNoTracking()
                    .Where(o => o.CustomerId == customerIdSnap)
                    .OrderByDescending(o => o.CreatedAtUtc)
                    .Take(3)
                    .Select(o => new { o.Id, Status = o.CurrentStatus.ToString(), o.Total, o.CreatedAtUtc })
                    .ToListAsync();

                var customerCtx = customerInfo?.Name != null ? $"اسم الزبون: {customerInfo.Name}" : "";
                var ordersCtx = recentOrders.Any()
                    ? "آخر طلبات الزبون: " + string.Join("، ", recentOrders.Select(o => $"طلب #{o.Id} ({o.Status}) بقيمة {o.Total:F0}"))
                    : "لا توجد طلبات سابقة";

                var defaultPrompt = $"""
أنت مساعد خدمة عملاء محترف يعمل لدى متجر "{storeName}".
{customerCtx}
{ordersCtx}

قواعد الرد الإلزامية:
- ابدأ بتحية ودية ومخصصة (استخدم اسم الزبون إن وُجد)
- أظهر تفهماً حقيقياً للمشكلة أو الاستفسار
- قدّم حلاً عملياً أو معلومة مفيدة بثقة
- اختم بجملة إيجابية تطمئن الزبون أن طلبه مهم
- 2-4 جمل فقط — بالعربية الفصحى البسيطة — بدون كلمات إنجليزية أو رموز
- لا تذكر أنك AI أو روبوت
- قدّم الرد مباشرة بدون أي مقدمة أو تفسير
""";

                var systemPrompt = !string.IsNullOrWhiteSpace(storeSettings?.AiAutoReplySystemPrompt)
                    ? storeSettings.AiAutoReplySystemPrompt
                    : defaultPrompt;

                var groqMessages = new List<object> { new { role = "system", content = systemPrompt } };
                foreach (var m in recentMsgs)
                    groqMessages.Add(new { role = m.FromAdmin ? "assistant" : "user", content = m.Message.Replace("🤖 ", "").Trim() });

                var groqPayload = new { model = groqModel, messages = groqMessages, max_tokens = 450, temperature = 0.65, top_p = 0.9, stream = false };
                var gc = httpFactory1.CreateClient();
                gc.Timeout = TimeSpan.FromSeconds(30);
                gc.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", groqApiKey);
                var gRes = await gc.PostAsync("https://api.groq.com/openai/v1/chat/completions",
                    new StringContent(JsonSerializer.Serialize(groqPayload), Encoding.UTF8, "application/json"));

                if (!gRes.IsSuccessStatusCode) return;

                var gBody = await gRes.Content.ReadAsStringAsync();
                using var gDoc = JsonDocument.Parse(gBody);
                var aiText = gDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();

                if (string.IsNullOrWhiteSpace(aiText)) return;

                var aiNow = DateTime.UtcNow;
                var aiMsg = new ComplaintMessage { ThreadId = threadIdSnap, FromAdmin = true, Message = $"🤖 {aiText}", CreatedAtUtc = aiNow };
                db1.ComplaintMessages.Add(aiMsg);
                var threadRef = await db1.ComplaintThreads.FirstOrDefaultAsync(x => x.Id == threadIdSnap);
                if (threadRef != null) threadRef.UpdatedAtUtc = aiNow;
                await db1.SaveChangesAsync();

                var aiPayload = new { id = aiMsg.Id, threadId = threadIdSnap, fromAdmin = true, message = aiMsg.Message, createdAtUtc = aiMsg.CreatedAtUtc, isAutoReply = true };
                await hub1.Clients.Group("admin").SendAsync("chat_message_received", aiPayload);
                await hub1.Clients.Group($"customer-{customerIdSnap}").SendAsync("chat_message_received", aiPayload);
            }
            catch { /* Groq غير متاح — لا يؤثر على عمل النظام */ }
        });

        return Ok(new { threadId = thread.Id, messageId = msg.Id, createdAtUtc = msg.CreatedAtUtc });
    }

    [HttpGet("complaints/{customerId:int}")]
    public async Task<IActionResult> ListComplaintThreads(int customerId)
    {
        var threads = await _db.ComplaintThreads.AsNoTracking()
            .Where(t => t.CustomerId == customerId)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.OrderId,
                t.CreatedAtUtc,
                t.UpdatedAtUtc,
                t.LastCustomerSeenAtUtc,
                lastMsg = _db.ComplaintMessages
                    .Where(m => m.ThreadId == t.Id)
                    .OrderByDescending(m => m.CreatedAtUtc)
                    .Select(m => new { m.FromAdmin, m.Message, m.CreatedAtUtc })
                    .FirstOrDefault(),
                unreadCount = _db.ComplaintMessages
                    .Where(m => m.ThreadId == t.Id && m.FromAdmin && (t.LastCustomerSeenAtUtc == null || m.CreatedAtUtc > t.LastCustomerSeenAtUtc))
                    .Count()
            })
            .OrderByDescending(x => x.lastMsg != null ? x.lastMsg.CreatedAtUtc : x.UpdatedAtUtc)
            .ToListAsync();

        var list = threads.Select(x => new
        {
            x.Id,
            x.Title,
            x.OrderId,
            x.CreatedAtUtc,
            x.UpdatedAtUtc,
            unreadCount = x.unreadCount,
            lastMessagePreview = x.lastMsg == null ? "" : (x.lastMsg.FromAdmin ? "الإدارة: " : "أنت: ") + (x.lastMsg.Message.Length > 60 ? x.lastMsg.Message.Substring(0, 60) + "…" : x.lastMsg.Message),
            lastMessageAtUtc = x.lastMsg?.CreatedAtUtc
        }).ToList();

        return Ok(list);
    }

    [HttpGet("complaint/{threadId:int}")]
    public async Task<IActionResult> GetComplaint(int threadId)
    {
        var t = await _db.ComplaintThreads.Include(x => x.Messages).FirstOrDefaultAsync(x => x.Id == threadId);
        if (t == null) return NotFound(new { error = "not_found" });

        t.LastCustomerSeenAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new
        {
            t.Id,
            t.Title,
            t.OrderId,
            t.CustomerId,
            messages = t.Messages.OrderBy(m => m.CreatedAtUtc).Select(m => new { m.Id, fromAdmin = m.FromAdmin, message = m.Message, m.CreatedAtUtc })
        });
    }

    public record SendComplaintMessageReq(string Message); 

    [HttpPost("complaint/{threadId:int}/message")]
    public async Task<IActionResult> SendComplaintMessage(int threadId, SendComplaintMessageReq req)
    {
        var t = await _db.ComplaintThreads.FirstOrDefaultAsync(x => x.Id == threadId);
        if (t == null) return NotFound(new { error = "not_found" });

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == t.CustomerId);
        if (customer == null) return NotFound(new { error = "not_found" });
        if (customer.IsChatBlocked)
            return StatusCode(403, new { error = "chat_blocked", message = "تم إيقاف الدردشة من قبل الإدارة" });

        var cleanMsg = (req.Message ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cleanMsg))
            return BadRequest(new { error = "empty_message", message = "الرسالة فارغة" });

        var now = DateTime.UtcNow;
        var msg = new ComplaintMessage { ThreadId = t.Id, FromAdmin = false, Message = cleanMsg, CreatedAtUtc = now };
        _db.ComplaintMessages.Add(msg);
        t.UpdatedAtUtc = now;
        await _db.SaveChangesAsync();

        var payload = new { id = msg.Id, threadId = t.Id, fromAdmin = false, message = cleanMsg, createdAtUtc = msg.CreatedAtUtc };
        await _hub.Clients.Group("admin").SendAsync("chat_message_received", payload);
        await _hub.Clients.Group($"customer-{t.CustomerId}").SendAsync("chat_message_received", payload);

        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Admin, null,
            "رسالة جديدة", "لديك رسالة جديدة من زبون", t.OrderId);

        await _notifications.SendAdminChatPushAsync(t.OrderId, t.CustomerId, cleanMsg);

        var threadIdForAi = t.Id;
        var customerIdForAi = t.CustomerId;
        var orderIdForAi = t.OrderId;
        var scopeFactory2 = _scopeFactory;
        var hub2 = _hub;
        var httpFactory2 = _httpFactory;
        var config2 = _config;
        // رد تلقائي على رسائل الزبون عبر Groq AI — scope منفصل لأن DbContext scoped
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1200);

                using var scope2 = scopeFactory2.CreateScope();
                var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();

                var storeSettings = await db2.StoreSettings.AsNoTracking().FirstOrDefaultAsync();
                if (storeSettings?.AiAutoReplyEnabled == false) return;

                var groqApiKey = config2["Groq:ApiKey"] ?? "";
                if (string.IsNullOrWhiteSpace(groqApiKey)) return;

                var groqModel = "llama-3.3-70b-versatile";
                var storeName = storeSettings?.StoreName?.Trim() ?? config2["Store:Name"] ?? "المتجر";

                var recentMsgs = await db2.ComplaintMessages
                    .Where(m => m.ThreadId == threadIdForAi)
                    .OrderByDescending(m => m.CreatedAtUtc)
                    .Take(8)
                    .OrderBy(m => m.CreatedAtUtc)
                    .Select(m => new { m.FromAdmin, m.Message })
                    .ToListAsync();

                var customerInfo = await db2.Customers.AsNoTracking()
                    .Where(c => c.Id == customerIdForAi)
                    .Select(c => new { c.Name, c.Phone })
                    .FirstOrDefaultAsync();

                var recentOrders = await db2.Orders.AsNoTracking()
                    .Where(o => o.CustomerId == customerIdForAi)
                    .OrderByDescending(o => o.CreatedAtUtc)
                    .Take(3)
                    .Select(o => new { o.Id, Status = o.CurrentStatus.ToString(), o.Total, o.CreatedAtUtc })
                    .ToListAsync();

                var customerCtx = customerInfo?.Name != null ? $"اسم الزبون: {customerInfo.Name}" : "";
                var ordersCtx = recentOrders.Any()
                    ? "آخر طلبات الزبون: " + string.Join("، ", recentOrders.Select(o => $"طلب #{o.Id} ({o.Status}) بقيمة {o.Total:F0}"))
                    : "لا توجد طلبات سابقة";

                var defaultPrompt = $"""
أنت مساعد خدمة عملاء محترف يعمل لدى متجر "{storeName}".
{customerCtx}
{ordersCtx}

قواعد الرد الإلزامية:
- ابدأ بتحية ودية ومخصصة (استخدم اسم الزبون إن وُجد)
- أظهر تفهماً حقيقياً للمشكلة أو الاستفسار
- قدّم حلاً عملياً أو معلومة مفيدة بثقة
- اختم بجملة إيجابية تطمئن الزبون أن طلبه مهم
- 2-4 جمل فقط — بالعربية الفصحى البسيطة — بدون كلمات إنجليزية أو رموز
- لا تذكر أنك AI أو روبوت
- قدّم الرد مباشرة بدون أي مقدمة أو تفسير
""";

                var systemPrompt = !string.IsNullOrWhiteSpace(storeSettings?.AiAutoReplySystemPrompt)
                    ? storeSettings.AiAutoReplySystemPrompt
                    : defaultPrompt;

                var groqMessages = new List<object> { new { role = "system", content = systemPrompt } };
                foreach (var m in recentMsgs)
                    groqMessages.Add(new { role = m.FromAdmin ? "assistant" : "user", content = m.Message.Replace("🤖 ", "").Trim() });

                var groqPayload = new { model = groqModel, messages = groqMessages, max_tokens = 450, temperature = 0.65, top_p = 0.9, stream = false };
                var gc = httpFactory2.CreateClient();
                gc.Timeout = TimeSpan.FromSeconds(30);
                gc.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", groqApiKey);
                var gRes = await gc.PostAsync("https://api.groq.com/openai/v1/chat/completions",
                    new StringContent(JsonSerializer.Serialize(groqPayload), Encoding.UTF8, "application/json"));

                if (!gRes.IsSuccessStatusCode) return;

                var gBody = await gRes.Content.ReadAsStringAsync();
                using var gDoc = JsonDocument.Parse(gBody);
                var aiText = gDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();

                if (string.IsNullOrWhiteSpace(aiText)) return;

                var aiNow = DateTime.UtcNow;
                var aiMsg = new ComplaintMessage { ThreadId = threadIdForAi, FromAdmin = true, Message = $"🤖 {aiText}", CreatedAtUtc = aiNow };
                db2.ComplaintMessages.Add(aiMsg);
                var threadRef = await db2.ComplaintThreads.FirstOrDefaultAsync(x => x.Id == threadIdForAi);
                if (threadRef != null) threadRef.UpdatedAtUtc = aiNow;
                await db2.SaveChangesAsync();

                var aiPayload = new { id = aiMsg.Id, threadId = threadIdForAi, fromAdmin = true, message = aiMsg.Message, createdAtUtc = aiMsg.CreatedAtUtc, isAutoReply = true };
                await hub2.Clients.Group("admin").SendAsync("chat_message_received", aiPayload);
                await hub2.Clients.Group($"customer-{customerIdForAi}").SendAsync("chat_message_received", aiPayload);
            }
            catch { /* Groq غير متاح أو خطأ */ }
        });

        return Ok(new { ok = true });
    }

    public record AdminComplaintMessageReq(string Message);

    [HttpPost("admin/complaint/{threadId:int}/reply")]
    [AdminAuth]
    public async Task<IActionResult> AdminReplyToComplaint(int threadId, [FromBody] AdminComplaintMessageReq req)
    {
        var t = await _db.ComplaintThreads.FirstOrDefaultAsync(x => x.Id == threadId);
        if (t == null) return NotFound(new { error = "not_found" });

        var cleanMsg = (req.Message ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cleanMsg))
            return BadRequest(new { error = "empty_message", message = "الرسالة فارغة" });

        var now = DateTime.UtcNow;
        var msg = new ComplaintMessage { ThreadId = t.Id, FromAdmin = true, Message = cleanMsg, CreatedAtUtc = now };
        _db.ComplaintMessages.Add(msg);
        t.UpdatedAtUtc = now;
        t.LastAdminSeenAtUtc = now;
        await _db.SaveChangesAsync();

        // Send to customer (use same event as AI replies so customer app receives it)
        var payload = new { id = msg.Id, threadId = t.Id, fromAdmin = true, message = cleanMsg, createdAtUtc = msg.CreatedAtUtc };
        await _hub.Clients.Group($"customer-{t.CustomerId}").SendAsync("chat_message_received", payload);
        await _hub.Clients.Group($"customer-{t.CustomerId}").SendAsync("complaint_message", payload);
        await _hub.Clients.Group("admin").SendAsync("complaint_message_sent", payload);
        await _hub.Clients.Group("admin").SendAsync("chat_message_received", payload);

        // Notify customer
        await _notifications.CreateAndBroadcastAsync(NotificationUserType.Customer, t.CustomerId,
            "رد على شكواك", cleanMsg.Length > 80 ? cleanMsg[..80] + "..." : cleanMsg, t.OrderId);

        return Ok(new { ok = true, messageId = msg.Id });
    }

    [HttpGet("admin/complaints")]
    [AdminAuth]
    public async Task<IActionResult> GetAllComplaints([FromQuery] int skip = 0, [FromQuery] int take = 20)
    {
        var threads = await _db.ComplaintThreads.AsNoTracking()
            .Include(t => t.Messages)
            .OrderByDescending(t => t.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.CustomerId,
                t.OrderId,
                t.CreatedAtUtc,
                t.UpdatedAtUtc,
                t.IsArchivedByAdmin,
                messageCount = t.Messages.Count,
                lastMessage = t.Messages.OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault().Message,
                lastMessageAt = t.Messages.OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault().CreatedAtUtc
            })
            .ToListAsync();

        return Ok(threads);
    }

    [HttpPut("admin/complaint/{threadId:int}/archive")]
    [AdminAuth]
    public async Task<IActionResult> ArchiveComplaint(int threadId)
    {
        var t = await _db.ComplaintThreads.FirstOrDefaultAsync(x => x.Id == threadId);
        if (t == null) return NotFound(new { error = "not_found" });

        t.IsArchivedByAdmin = true;
        t.LastAdminSeenAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _hub.Clients.Group("admin").SendAsync("complaint_archived", new { threadId });
        return Ok(new { ok = true });
    }

    [HttpGet("admin/complaint/{threadId:int}")]
    [AdminAuth]
    public async Task<IActionResult> GetAdminComplaint(int threadId)
    {
        var t = await _db.ComplaintThreads
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == threadId);
        if (t == null) return NotFound(new { error = "not_found" });

        t.LastAdminSeenAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            t.Id,
            t.Title,
            t.OrderId,
            t.CustomerId,
            t.CreatedAtUtc,
            t.IsArchivedByAdmin,
            messages = t.Messages.OrderBy(m => m.CreatedAtUtc).Select(m => new { m.Id, fromAdmin = m.FromAdmin, message = m.Message, m.CreatedAtUtc })
        });
    }

    [HttpGet("admin/complaints/unread")]
    [AdminAuth]
    public async Task<IActionResult> GetUnreadComplaintsCount()
    {
        var count = await _db.ComplaintThreads
            .Where(t => !t.IsArchivedByAdmin && (t.LastAdminSeenAtUtc == null || t.UpdatedAtUtc > t.LastAdminSeenAtUtc))
            .CountAsync();

        return Ok(new { unreadCount = count });
    }

    [HttpGet("admin/complaints/stat")]
    [AdminAuth]
    public async Task<IActionResult> GetComplaintsStats()
    {
        var total = await _db.ComplaintThreads.CountAsync();
        var unread = await _db.ComplaintThreads.Where(t => t.LastAdminSeenAtUtc == null || t.UpdatedAtUtc > t.LastAdminSeenAtUtc).CountAsync();
        var archived = await _db.ComplaintThreads.Where(t => t.IsArchivedByAdmin).CountAsync();

        return Ok(new { total, unread, archived, active = total - archived });
    }

    [HttpGet("favorites/{customerId:int}")]
    public async Task<IActionResult> GetFavorites(int customerId)
    {
        var favs = await _db.CustomerFavorites
            .Where(f => f.CustomerId == customerId)
            .Include(f => f.Product)
            .Select(f => new {
                productId = f.ProductId,
                name = f.Product != null ? f.Product.Name : "",
                price = f.Product != null ? f.Product.Price : 0,
                imageUrl = f.Product != null ? f.Product.ImageUrl : null,
                isAvailable = f.Product != null ? f.Product.IsAvailable : false,
                createdAtUtc = f.CreatedAtUtc
            })
            .ToListAsync();
        return Ok(favs);
    }

    [HttpPost("favorites/toggle")]
    public async Task<IActionResult> ToggleFavorite([FromBody] FavoriteToggleReq req)
    {
        var existing = await _db.CustomerFavorites
            .FirstOrDefaultAsync(f => f.CustomerId == req.CustomerId && f.ProductId == req.ProductId);
        if (existing != null)
        {
            _db.CustomerFavorites.Remove(existing);
            await _db.SaveChangesAsync();
            return Ok(new { isFavorite = false });
        }
        _db.CustomerFavorites.Add(new CustomerFavorite { CustomerId = req.CustomerId, ProductId = req.ProductId });
        await _db.SaveChangesAsync();
        return Ok(new { isFavorite = true });
    }
    public record FavoriteToggleReq(int CustomerId, int ProductId);

    [HttpPost("product-rating")]
    public async Task<IActionResult> RateProduct([FromBody] ProductRatingReq req)
    {
        try
        {
            // التحقق أن الطلب موجود وينتمي للزبون
            var orderExists = await _db.Orders
                .AnyAsync(o => o.Id == req.OrderId && o.CustomerId == req.CustomerId
                               && (o.CurrentStatus == OrderStatus.Delivered
                                   || _db.OrderStatusHistory.Any(h => h.OrderId == o.Id && h.Status == OrderStatus.Delivered)));
            if (!orderExists) return BadRequest(new { error = "order_not_found" });
            // للتوافق: القيمة السالبة تعني عرضاً - نقبل التقييم لأي منتج في الطلب
            if (req.ProductId > 0)
            {
                var ordered = await _db.OrderItems
                    .AnyAsync(i => i.OrderId == req.OrderId && i.ProductId == req.ProductId);
                if (!ordered) return BadRequest(new { error = "not_ordered" });
            }

            bool isMySql = _db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true
                        || _db.Database.ProviderName?.Contains("Pomelo", StringComparison.OrdinalIgnoreCase) == true;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            // إنشاء الجدول إذا لم يكن موجوداً
            try {
                if (isMySql)
                    await ExecRawAsync(conn, @"CREATE TABLE IF NOT EXISTS `ProductRatings` (`Id` INT NOT NULL AUTO_INCREMENT,`CustomerId` INT NOT NULL,`ProductId` INT NOT NULL,`OrderId` INT NOT NULL,`Stars` INT NOT NULL,`Comment` LONGTEXT NULL,`CreatedAtUtc` DATETIME(6) NOT NULL DEFAULT (NOW()),PRIMARY KEY (`Id`),UNIQUE KEY `uq_pr` (`CustomerId`,`ProductId`,`OrderId`)) CHARACTER SET utf8mb4");
                else
                    await ExecRawAsync(conn, @"CREATE TABLE IF NOT EXISTS ""ProductRatings"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,""CustomerId"" INTEGER NOT NULL,""ProductId"" INTEGER NOT NULL,""OrderId"" INTEGER NOT NULL,""Stars"" INTEGER NOT NULL,""Comment"" TEXT NULL,""CreatedAtUtc"" TEXT NOT NULL DEFAULT (datetime('now')))");
            } catch { }

            if (isMySql)
            {
                var comment = string.IsNullOrWhiteSpace(req.Comment) ? "NULL" : $"'{req.Comment.Replace("'","''")}'";
                await ExecRawAsync(conn,
                    $"INSERT INTO `ProductRatings` (`CustomerId`,`ProductId`,`OrderId`,`Stars`,`Comment`,`CreatedAtUtc`) " +
                    $"VALUES ({req.CustomerId},{req.ProductId},{req.OrderId},{req.Stars},{comment},NOW()) " +
                    $"ON DUPLICATE KEY UPDATE `Stars`={req.Stars}, `Comment`={comment}");
            }
            else
            {
                var comment = string.IsNullOrWhiteSpace(req.Comment) ? "NULL" : $"'{req.Comment.Replace("'","''")}'";
                await ExecRawAsync(conn,
                    $@"INSERT INTO ""ProductRatings"" (""CustomerId"",""ProductId"",""OrderId"",""Stars"",""Comment"") " +
                    $@"VALUES ({req.CustomerId},{req.ProductId},{req.OrderId},{req.Stars},{comment}) " +
                    $@"ON CONFLICT(""CustomerId"",""ProductId"",""OrderId"") DO UPDATE SET ""Stars""={req.Stars},""Comment""={comment}");
            }

            await _hub.Clients.Group("admin").SendAsync("rating_added", new { productId = req.ProductId, orderId = req.OrderId, stars = req.Stars });
            await _hub.Clients.All.SendAsync("ratings_updated");
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "rate_product_failed", message = ex.Message });
        }
    }

    private static async Task ExecRawAsync(System.Data.Common.DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
    public record ProductRatingReq(int CustomerId, int ProductId, int OrderId, int Stars, string? Comment);

    [HttpGet("product-rating/{productId:int}")]
    public async Task<IActionResult> GetProductRating(int productId)
    {
        var ratings = await _db.ProductRatings
            .Where(r => r.ProductId == productId)
            .ToListAsync();
        if (!ratings.Any()) return Ok(new { avg = 0.0, count = 0, ratings = new object[0] });
        var avg = ratings.Average(r => r.Stars);
        return Ok(new { avg = Math.Round(avg, 1), count = ratings.Count });
    }

    [HttpGet("order/{orderId:int}/product-ratings/{customerId:int}")]
    public async Task<IActionResult> GetOrderProductRatings(int orderId, int customerId)
    {
        var ratings = await _db.ProductRatings.AsNoTracking()
            .Where(r => r.OrderId == orderId && r.CustomerId == customerId)
            .Select(r => new { r.ProductId, r.Stars, r.Comment })
            .ToListAsync();
        return Ok(ratings);
    }
}

