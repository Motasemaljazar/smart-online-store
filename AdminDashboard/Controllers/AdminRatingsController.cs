using AdminDashboard.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminDashboard.Controllers;

[ApiController]
[Route("api/admin/ratings")]
[Authorize(Policy = "AdminOnly")]
public class AdminRatingsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminRatingsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? type = null)
    {
        var filterType = (type ?? "all").ToLower();
        var conn = _db.Database.GetDbConnection();
        try
        {
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            bool isMySql = _db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true
                        || _db.Database.ProviderName?.Contains("Pomelo", StringComparison.OrdinalIgnoreCase) == true;

            // تأكد من وجود الجداول
            await EnsureTablesAsync(conn, isMySql);

            var orderItems   = new List<object>();
            var productItems = new List<object>();
            double? storeAvg = null, productAvg = null;

            // ── تقييمات المتجر (OrderRatings) ──────────────────────────────────
            if (filterType == "all" || filterType == "order")
            {
                string orderSql = isMySql
                    ? @"SELECT r.OrderId, r.StoreRate, r.StoreComment, r.Comment,
                               c.Name AS CustomerName
                        FROM `OrderRatings` r
                        LEFT JOIN `Orders` o ON o.Id = r.OrderId
                        LEFT JOIN `Customers` c ON c.Id = o.CustomerId
                        WHERE r.StoreRate >= 1
                        ORDER BY r.CreatedAtUtc DESC LIMIT 500"
                    : @"SELECT r.OrderId, r.StoreRate, r.StoreComment, r.Comment,
                               c.Name AS CustomerName
                        FROM ""OrderRatings"" r
                        LEFT JOIN ""Orders"" o ON o.Id = r.OrderId
                        LEFT JOIN ""Customers"" c ON c.Id = o.CustomerId
                        WHERE r.StoreRate >= 1
                        ORDER BY r.CreatedAtUtc DESC LIMIT 500";

                var rows = await QueryAsync(conn, orderSql);

                if (rows.Count > 0)
                {
                    var vals = rows.Select(r => ToInt(r, "StoreRate")).Where(v => v >= 1).Select(v => (double)v).ToList();
                    storeAvg = vals.Count > 0 ? Math.Round(vals.Average(), 2) : null;

                    orderItems = rows.Select(r => (object)new
                    {
                        orderId      = ToInt(r, "OrderId"),
                        storeRate    = ToInt(r, "StoreRate"),
                        customerName = r.GetValueOrDefault("CustomerName")?.ToString() ?? "—",
                    }).ToList();
                }
            }

            // ── تقييمات المنتجات (ProductRatings) ──────────────────────────────
            if (filterType == "all" || filterType == "product")
            {
                string prodSql = isMySql
                    ? @"SELECT pr.ProductId, pr.Stars,
                               c.Name AS CustomerName,
                               p.Name AS ProductName
                        FROM `ProductRatings` pr
                        LEFT JOIN `Customers` c ON c.Id = pr.CustomerId
                        LEFT JOIN `Products` p ON p.Id = pr.ProductId
                        WHERE pr.Stars >= 1
                        ORDER BY pr.CreatedAtUtc DESC LIMIT 500"
                    : @"SELECT pr.ProductId, pr.Stars,
                               c.Name AS CustomerName,
                               p.Name AS ProductName
                        FROM ""ProductRatings"" pr
                        LEFT JOIN ""Customers"" c ON c.Id = pr.CustomerId
                        LEFT JOIN ""Products"" p ON p.Id = pr.ProductId
                        WHERE pr.Stars >= 1
                        ORDER BY pr.CreatedAtUtc DESC LIMIT 500";

                var rows = await QueryAsync(conn, prodSql);

                if (rows.Count > 0)
                {
                    var vals = rows.Select(r => ToInt(r, "Stars")).Where(v => v >= 1).Select(v => (double)v).ToList();
                    productAvg = vals.Count > 0 ? Math.Round(vals.Average(), 2) : null;

                    productItems = rows.Select(r => (object)new
                    {
                        productName  = r.GetValueOrDefault("ProductName")?.ToString() ?? $"منتج #{ToInt(r, "ProductId")}",
                        stars        = ToInt(r, "Stars"),
                        customerName = r.GetValueOrDefault("CustomerName")?.ToString() ?? "—",
                    }).ToList();
                }
            }

            return Ok(new { orderItems, productItems, storeAvg, productAvg });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error    = "ratings_error",
                message  = ex.Message,
                inner    = ex.InnerException?.Message ?? "",
                provider = _db.Database.ProviderName ?? "unknown"
            });
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────
    private static async Task EnsureTablesAsync(System.Data.Common.DbConnection conn, bool isMySql)
    {
        try
        {
            if (isMySql)
            {
                await ExecSafeAsync(conn, @"CREATE TABLE IF NOT EXISTS `OrderRatings` (
                    `OrderId` INT NOT NULL,
                    `StoreRate` INT NOT NULL DEFAULT 0,
                    `DriverRate` INT NOT NULL DEFAULT 0,
                    `Comment` LONGTEXT NULL,
                    `StoreComment` LONGTEXT NULL,
                    `DriverComment` LONGTEXT NULL,
                    `CreatedAtUtc` DATETIME(6) NOT NULL DEFAULT (NOW()),
                    PRIMARY KEY (`OrderId`)
                ) CHARACTER SET utf8mb4");
                await ExecSafeAsync(conn, "ALTER TABLE `OrderRatings` ADD COLUMN IF NOT EXISTS `StoreComment` LONGTEXT NULL");
                await ExecSafeAsync(conn, "ALTER TABLE `OrderRatings` ADD COLUMN IF NOT EXISTS `DriverComment` LONGTEXT NULL");
                await ExecSafeAsync(conn, @"CREATE TABLE IF NOT EXISTS `ProductRatings` (
                    `Id` INT NOT NULL AUTO_INCREMENT,
                    `CustomerId` INT NOT NULL,
                    `ProductId` INT NOT NULL,
                    `OrderId` INT NOT NULL,
                    `Stars` INT NOT NULL,
                    `Comment` LONGTEXT NULL,
                    `CreatedAtUtc` DATETIME(6) NOT NULL DEFAULT (NOW()),
                    PRIMARY KEY (`Id`),
                    UNIQUE KEY `uq_pr` (`CustomerId`,`ProductId`,`OrderId`)
                ) CHARACTER SET utf8mb4");
            }
            else
            {
                await ExecSafeAsync(conn, @"CREATE TABLE IF NOT EXISTS ""OrderRatings"" (
                    ""OrderId"" INTEGER PRIMARY KEY,
                    ""StoreRate"" INTEGER NOT NULL DEFAULT 0,
                    ""DriverRate"" INTEGER NOT NULL DEFAULT 0,
                    ""Comment"" TEXT NULL,
                    ""StoreComment"" TEXT NULL,
                    ""DriverComment"" TEXT NULL,
                    ""CreatedAtUtc"" TEXT NOT NULL DEFAULT (datetime('now')))");
                await ExecSafeAsync(conn, @"ALTER TABLE ""OrderRatings"" ADD COLUMN ""StoreComment"" TEXT NULL");
                await ExecSafeAsync(conn, @"ALTER TABLE ""OrderRatings"" ADD COLUMN ""DriverComment"" TEXT NULL");
                await ExecSafeAsync(conn, @"CREATE TABLE IF NOT EXISTS ""ProductRatings"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""CustomerId"" INTEGER NOT NULL,
                    ""ProductId"" INTEGER NOT NULL,
                    ""OrderId"" INTEGER NOT NULL,
                    ""Stars"" INTEGER NOT NULL,
                    ""Comment"" TEXT NULL,
                    ""CreatedAtUtc"" TEXT NOT NULL DEFAULT (datetime('now')))");
                await ExecSafeAsync(conn, @"CREATE UNIQUE INDEX IF NOT EXISTS uq_pr ON ""ProductRatings""(""CustomerId"",""ProductId"",""OrderId"")");
            }
        }
        catch { }
    }

    private static async Task ExecSafeAsync(System.Data.Common.DbConnection conn, string sql)
    {
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    private static async Task<List<Dictionary<string, object?>>> QueryAsync(
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

    private static int ToInt(Dictionary<string, object?> row, string col)
    {
        if (!row.TryGetValue(col, out var v) || v is null) return 0;
        return Convert.ToInt32(v);
    }
}
