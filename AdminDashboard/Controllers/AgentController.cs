using BCrypt.Net;
using AdminDashboard.Data;
using AdminDashboard.Entities;
using AdminDashboard.Hubs;
using AdminDashboard.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/admin/agents")]
[Authorize(Policy = "AdminOnly")]
public class AdminAgentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotifyHub> _hub;

    public AdminAgentController(AppDbContext db, IHubContext<NotifyHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var list = await _db.Agents.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        return Ok(list);
    }

    public record UpsertAgentReq(int? Id, string Name, string Phone, string Pin, string? Email, AgentStatus Status, string? PhotoUrl, decimal? CommissionPercent);

    [HttpPost]
    public async Task<IActionResult> Upsert(UpsertAgentReq req)
    {
        Agent a;
        if (req.Id is null)
        {
            
            if (await _db.Agents.AnyAsync(x => x.Phone == req.Phone))
                return BadRequest(new { error = "phone_taken", message = "رقم الهاتف مستخدم من قبل مندوب آخر." });
            a = new Agent();
            _db.Agents.Add(a);
        }
        else
        {
            a = await _db.Agents.FirstOrDefaultAsync(x => x.Id == req.Id.Value) ?? new Agent();
            if (a.Id == 0) return NotFound(new { error = "not_found" });
            if (await _db.Agents.AnyAsync(x => x.Phone == req.Phone && x.Id != req.Id.Value))
                return BadRequest(new { error = "phone_taken", message = "رقم الهاتف مستخدم من قبل مندوب آخر." });
        }

        a.Name = req.Name;
        a.Phone = req.Phone;
        a.Pin = req.Pin; 
        
        if (!string.IsNullOrWhiteSpace(req.Pin))
            a.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Pin, workFactor: 12);
        if (req.CommissionPercent.HasValue) a.CommissionPercent = req.CommissionPercent.Value;
        a.Email = req.Email;
        a.Status = req.Status;
        a.PhotoUrl = req.PhotoUrl;

        await _db.SaveChangesAsync();
        await _hub.Clients.Group("admin").SendAsync("agent_changed", new { a.Id });
        return Ok(a);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await _db.Agents.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound(new { error = "not_found" });

        _db.Agents.Remove(a);
        try
        {
            await _db.SaveChangesAsync();
            await _hub.Clients.Group("admin").SendAsync("agent_deleted", new { Id = id });
            return Ok(new { ok = true });
        }
        catch (DbUpdateException)
        {
            return BadRequest(new { error = "delete_failed", message = "تعذر حذف المندوب لأنه مرتبط ببيانات أخرى." });
        }
    }
}

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IOptions<AppSecurityOptions> _opts;
    private readonly IWebHostEnvironment _env;

    public AgentController(AppDbContext db, IOptions<AppSecurityOptions> opts, IWebHostEnvironment env)
    {
        _db = db;
        _opts = opts;
        _env = env;
    }

    private bool TryGetAgentId(out int agentId)
    {
        agentId = 0;
        if (!Request.Headers.TryGetValue("X-AGENT-TOKEN", out var token)) return false;
        return AgentAuth.TryValidate(token!, _opts, out agentId);
    }

    public record LoginReq(string Phone, string? Pin, string? Password);

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginReq req)
    {
        var inputPassword = req.Pin ?? req.Password ?? "";

        var a = await _db.Agents.FirstOrDefaultAsync(x => x.Phone == req.Phone);
        if (a == null) return Unauthorized(new { error = "بيانات الدخول غير صحيحة" });

        bool valid;
        if (!string.IsNullOrWhiteSpace(a.PasswordHash))
        {
            valid = BCrypt.Net.BCrypt.Verify(inputPassword, a.PasswordHash);
        }
        else
        {
            
            valid = (a.Pin == inputPassword);
            if (valid)
            {
                a.PasswordHash = BCrypt.Net.BCrypt.HashPassword(inputPassword, workFactor: 12);
            }
        }

        if (!valid) return Unauthorized(new { error = "بيانات الدخول غير صحيحة" });

        a.Status = AgentStatus.Available;
        await _db.SaveChangesAsync();

        var token = AgentAuth.IssueToken(a.Id, _opts);
        return Ok(new { token, agentId = a.Id, id = a.Id, name = a.Name, agent = new { a.Id, a.Name, a.Phone, a.Email, a.Status, a.PhotoUrl } });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!TryGetAgentId(out var agentId)) return Unauthorized(new { error = "unauthorized" });
        var a = await _db.Agents.FirstOrDefaultAsync(x => x.Id == agentId);
        if (a == null) return Unauthorized(new { error = "unauthorized" });
        a.Status = AgentStatus.Offline;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!TryGetAgentId(out var agentId)) return Unauthorized(new { error = "unauthorized" });
        var a = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == agentId);
        if (a == null) return Unauthorized(new { error = "unauthorized" });
        return Ok(new { a.Id, a.Name, a.Phone, a.Email, a.Status, a.PhotoUrl });
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        if (!TryGetAgentId(out var agentId)) return Unauthorized(new { error = "unauthorized" });
        var a = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == agentId);
        if (a == null) return Unauthorized(new { error = "unauthorized" });
        return Ok(new { a.Id, a.Name, a.Phone, a.Email, a.Status, a.PhotoUrl });
    }

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts()
    {
        if (!TryGetAgentId(out int agentId)) return Unauthorized(new { error = "unauthorized" });
        var products = await _db.Products.AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Category)
            .Where(p => p.IsActive && p.AgentId == agentId)
            .OrderBy(p => p.CategoryId).ThenBy(p => p.Name)
            .ToListAsync();

        var result = products.Select(p => new
        {
            p.Id, p.Name, p.Description, p.Price, p.CategoryId,
            categoryName = p.Category != null ? p.Category.Name : null,
            p.IsActive, p.IsAvailable, p.ImageUrl,
            p.StockQuantity, p.TrackStock, p.AgentId,
            images = p.Images
                .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                .Select(i => new { i.Id, url = i.Url, i.IsPrimary, i.SortOrder })
                .ToList()
        });
        return Ok(result);
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        if (!TryGetAgentId(out _)) return Unauthorized(new { error = "unauthorized" });
        var cats = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.ImageUrl, c.IsActive })
            .ToListAsync();
        return Ok(cats);
    }

    public record AgentProductReq(
        string Name,
        string? Description,
        decimal Price,
        int CategoryId,
        bool IsAvailable = true,
        string? ImageUrl = null,
        int StockQuantity = 0,
        bool TrackStock = false
    );

    [HttpGet("my-products")]
    public async Task<IActionResult> GetAgentProducts()
    {
        if (!TryGetAgentId(out int agentId)) return Unauthorized(new { error = "unauthorized" });
        var products = await _db.Products.AsNoTracking()
            .Include(p => p.Images)
            .Where(p => p.IsActive && p.AgentId == agentId)
            .OrderBy(p => p.CategoryId).ThenBy(p => p.Name)
            .ToListAsync();

        var result = products.Select(p => new
        {
            p.Id, p.Name, p.Description, p.Price, p.CategoryId,
            p.IsActive, p.IsAvailable, p.ImageUrl,
            p.StockQuantity, p.TrackStock, p.AgentId,
            images = p.Images
                .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                .Select(i => new { i.Id, url = i.Url, i.IsPrimary, i.SortOrder })
                .ToList()
        });
        return Ok(result);
    }

    [HttpPost("products")]
    public async Task<IActionResult> CreateAgentProduct(AgentProductReq req)
    {
        if (!TryGetAgentId(out int agentId)) return Unauthorized(new { error = "unauthorized" });
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "name_required", message = "اسم المنتج مطلوب" });
        var cat = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CategoryId);
        if (cat == null) return BadRequest(new { error = "category_not_found", message = "الفئة غير موجودة" });

        var product = new Product
        {
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            Price = req.Price,
            CategoryId = req.CategoryId,
            IsAvailable = req.IsAvailable,
            IsActive = true,
            ImageUrl = req.ImageUrl,
            AgentId = agentId,
            StockQuantity = req.StockQuantity,
            TrackStock = req.TrackStock
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return Ok(new { product.Id, product.Name, product.Description, product.Price, product.CategoryId, product.IsActive, product.IsAvailable, product.ImageUrl, product.StockQuantity, product.TrackStock });
    }

    [HttpPut("products/{productId:int}")]
    public async Task<IActionResult> UpdateAgentProduct(int productId, AgentProductReq req)
    {
        if (!TryGetAgentId(out _)) return Unauthorized(new { error = "unauthorized" });
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return NotFound(new { error = "not_found" });

        product.Name = req.Name.Trim();
        product.Description = req.Description?.Trim();
        product.Price = req.Price;
        product.CategoryId = req.CategoryId;
        product.IsAvailable = req.IsAvailable;
        if (req.ImageUrl != null) product.ImageUrl = req.ImageUrl;
        product.StockQuantity = req.StockQuantity;
        product.TrackStock = req.TrackStock;
        
        if (product.TrackStock && product.StockQuantity == 0)
            product.IsAvailable = false;

        await _db.SaveChangesAsync();
        return Ok(new { product.Id, product.Name, product.Description, product.Price, product.CategoryId, product.IsActive, product.IsAvailable, product.ImageUrl, product.StockQuantity, product.TrackStock });
    }

    [HttpDelete("products/{productId:int}")]
    public async Task<IActionResult> DeleteAgentProduct(int productId)
    {
        if (!TryGetAgentId(out _)) return Unauthorized(new { error = "unauthorized" });
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return NotFound(new { error = "not_found" });

        product.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    public record ToggleAvailabilityReq(bool IsAvailable);

    [HttpPatch("products/{productId:int}/availability")]
    public async Task<IActionResult> ToggleAgentProductAvailability(int productId, ToggleAvailabilityReq req)
    {
        if (!TryGetAgentId(out _)) return Unauthorized(new { error = "unauthorized" });
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return NotFound(new { error = "not_found" });

        product.IsAvailable = req.IsAvailable;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, product.Id, product.IsAvailable });
    }

    [HttpPost("products/{productId:int}/image")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAgentProductImage(int productId, [FromForm] IFormFile? file)
    {
        if (!TryGetAgentId(out var agentId)) return Unauthorized(new { error = "unauthorized" });
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.AgentId == agentId);
        if (product == null) return NotFound(new { error = "not_found" });
        if (file == null || file.Length == 0) return BadRequest(new { error = "no_file" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        if (!allowed.Contains(ext)) return BadRequest(new { error = "invalid_type" });

        var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var folderRel = $"uploads/products/{productId}";
        var folderAbs = Path.Combine(webRoot, folderRel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folderAbs);

        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(folderAbs, fileName);
        await using var stream = System.IO.File.Create(filePath);
        await file.CopyToAsync(stream);

        var relUrl = $"/{folderRel}/{fileName}";
        product.ImageUrl = relUrl;
        
        // Also add to ProductImages table so it shows in customer app
        var existingPrimary = await _db.ProductImages.Where(i => i.ProductId == productId && i.IsPrimary).ToListAsync();
        foreach (var old in existingPrimary) old.IsPrimary = false;
        
        _db.ProductImages.Add(new ProductImage
        {
            ProductId = productId,
            Url = relUrl,
            IsPrimary = true,
            SortOrder = 0
        });
        
        await _db.SaveChangesAsync();
        return Ok(new { url = relUrl });
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // تقييمات منتجات المندوب
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    [HttpGet("my-product-ratings")]
    public async Task<IActionResult> GetMyProductRatings([FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        if (!TryGetAgentId(out int agentId)) return Unauthorized(new { error = "unauthorized" });

        try
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            bool isMySql = _db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true
                        || _db.Database.ProviderName?.Contains("Pomelo", StringComparison.OrdinalIgnoreCase) == true;

            // تأكد من وجود جدول ProductRatings
            try
            {
                if (isMySql)
                    await ExecRawAsync(conn, @"CREATE TABLE IF NOT EXISTS `ProductRatings` (`Id` INT NOT NULL AUTO_INCREMENT,`CustomerId` INT NOT NULL,`ProductId` INT NOT NULL,`OrderId` INT NOT NULL,`Stars` INT NOT NULL,`Comment` LONGTEXT NULL,`CreatedAtUtc` DATETIME(6) NOT NULL DEFAULT (NOW()),PRIMARY KEY (`Id`),UNIQUE KEY `uq_pr` (`CustomerId`,`ProductId`,`OrderId`)) CHARACTER SET utf8mb4");
                else
                    await ExecRawAsync(conn, @"CREATE TABLE IF NOT EXISTS ""ProductRatings"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,""CustomerId"" INTEGER NOT NULL,""ProductId"" INTEGER NOT NULL,""OrderId"" INTEGER NOT NULL,""Stars"" INTEGER NOT NULL,""Comment"" TEXT NULL,""CreatedAtUtc"" TEXT NOT NULL DEFAULT (datetime('now')))");
            }
            catch { }

            // جلب منتجات المندوب (أولاً: مباشرة من AgentId، ثانياً: من خلال الطلبات)
            var myProductIds = new List<int>();

            // طريقة 1: منتجات مرتبطة بـ AgentId مباشرة
            var direct = await _db.Products.AsNoTracking()
                .Where(p => p.AgentId == agentId)
                .Select(p => p.Id)
                .ToListAsync();
            myProductIds.AddRange(direct);

            // طريقة 2: منتجات من خلال الطلبات التي اشترك فيها المندوب
            if (myProductIds.Count == 0)
            {
                var myOrderIds = await _db.OrderAgentItems.AsNoTracking()
                    .Where(ai => ai.AgentId == agentId)
                    .Select(ai => ai.OrderId)
                    .ToListAsync();

                if (myOrderIds.Count > 0)
                {
                    var productsFromOrders = await _db.OrderItems.AsNoTracking()
                        .Where(oi => myOrderIds.Contains(oi.OrderId))
                        .Select(oi => oi.ProductId)
                        .Distinct()
                        .ToListAsync();
                    myProductIds.AddRange(productsFromOrders);
                }
            }

            if (myProductIds.Count == 0)
                return Ok(new { ratings = new List<object>(), totalCount = 0, avgStars = (double?)null });

            var idsStr = string.Join(",", myProductIds.Distinct());
            var skip   = (page - 1) * limit;

            // جلب التقييمات
            List<Dictionary<string, object?>> rows;
            if (isMySql)
                rows = await QueryRawAsync(conn,
                    $"SELECT pr.Id, pr.ProductId, pr.OrderId, pr.Stars, pr.Comment, pr.CreatedAtUtc, " +
                    $"p.Name as ProductName, c.Name as CustomerName, c.Phone as CustomerPhone " +
                    $"FROM `ProductRatings` pr " +
                    $"LEFT JOIN `Products` p ON p.Id = pr.ProductId " +
                    $"LEFT JOIN `Customers` c ON c.Id = pr.CustomerId " +
                    $"WHERE pr.ProductId IN ({idsStr}) AND pr.Stars >= 1 " +
                    $"ORDER BY pr.CreatedAtUtc DESC LIMIT {limit} OFFSET {skip}");
            else
                rows = await QueryRawAsync(conn,
                    $@"SELECT pr.Id, pr.ProductId, pr.OrderId, pr.Stars, pr.Comment, pr.CreatedAtUtc, " +
                    $@"p.Name as ProductName, c.Name as CustomerName, c.Phone as CustomerPhone " +
                    $@"FROM ""ProductRatings"" pr " +
                    $@"LEFT JOIN ""Products"" p ON p.Id = pr.ProductId " +
                    $@"LEFT JOIN ""Customers"" c ON c.Id = pr.CustomerId " +
                    $@"WHERE pr.ProductId IN ({idsStr}) AND pr.Stars >= 1 " +
                    $@"ORDER BY pr.CreatedAtUtc DESC LIMIT {limit} OFFSET {skip}");

            var totalCount = rows.Count; // تقريبي للصفحة الحالية
            // جلب العدد الكلي
            List<Dictionary<string, object?>> countRes;
            if (isMySql)
                countRes = await QueryRawAsync(conn, $"SELECT COUNT(*) as cnt FROM `ProductRatings` WHERE `ProductId` IN ({idsStr}) AND `Stars` >= 1");
            else
                countRes = await QueryRawAsync(conn, $@"SELECT COUNT(*) as cnt FROM ""ProductRatings"" WHERE ""ProductId"" IN ({idsStr}) AND ""Stars"" >= 1");
            totalCount = countRes.Count > 0 ? Convert.ToInt32(countRes[0]["cnt"] ?? 0) : 0;

            if (totalCount == 0)
                return Ok(new { ratings = new List<object>(), totalCount = 0, avgStars = (double?)null });

            // متوسط التقييم
            List<Dictionary<string, object?>> avgRes;
            if (isMySql)
                avgRes = await QueryRawAsync(conn, $"SELECT AVG(`Stars`) as avg FROM `ProductRatings` WHERE `ProductId` IN ({idsStr}) AND `Stars` >= 1");
            else
                avgRes = await QueryRawAsync(conn, $@"SELECT AVG(""Stars"") as avg FROM ""ProductRatings"" WHERE ""ProductId"" IN ({idsStr}) AND ""Stars"" >= 1");
            double? avgStars = null;
            if (avgRes.Count > 0 && avgRes[0]["avg"] != null)
                avgStars = Math.Round(Convert.ToDouble(avgRes[0]["avg"]), 2);

            var result = rows.Select(r => (object)new
            {
                productId    = Convert.ToInt32(r["ProductId"] ?? 0),
                productName  = r["ProductName"]?.ToString() ?? $"منتج #{r["ProductId"]}",
                stars        = Convert.ToInt32(r["Stars"] ?? 0),
                comment      = r["Comment"]?.ToString(),
                orderId      = Convert.ToInt32(r["OrderId"] ?? 0),
                createdAtUtc = r["CreatedAtUtc"] is DateTime dt
                    ? DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("O")
                    : r["CreatedAtUtc"]?.ToString(),
                customer = (object)new
                {
                    name  = r["CustomerName"]?.ToString() ?? "—",
                    phone = r["CustomerPhone"]?.ToString() ?? ""
                }
            }).ToList();

            return Ok(new { ratings = result, totalCount, avgStars });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "ratings_error", message = ex.Message });
        }
    }

    private static async Task ExecRawAsync(System.Data.Common.DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<Dictionary<string, object?>>> QueryRawAsync(
        System.Data.Common.DbConnection conn, string sql)
    {
        var result = new List<Dictionary<string, object?>>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            result.Add(row);
        }
        return result;
    }
}
