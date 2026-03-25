using System;
using Microsoft.EntityFrameworkCore;

namespace AdminDashboard.Data;

public static class SchemaUpgrader
{
    public static async Task EnsureAsync(AppDbContext db)
    {

        if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.OrdinalIgnoreCase))
            return;

        await ExecAsync(db, "DROP TABLE IF EXISTS Coupons;");
        await ExecAsync(db, "DROP INDEX IF EXISTS IX_Coupons_Code;");

        await EnsureTableAsync(db, "ProductImages",
            "CREATE TABLE IF NOT EXISTS ProductImages (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProductId INTEGER NOT NULL, Url TEXT NOT NULL, SortOrder INTEGER NOT NULL DEFAULT 0, IsPrimary INTEGER NOT NULL DEFAULT 0)");

        await EnsureTableAsync(db, "ProductVariants",
            "CREATE TABLE IF NOT EXISTS ProductVariants (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProductId INTEGER NOT NULL, Name TEXT NOT NULL, PriceDelta TEXT NOT NULL DEFAULT 0, IsActive INTEGER NOT NULL DEFAULT 1, SortOrder INTEGER NOT NULL DEFAULT 0)");

        await EnsureTableAsync(db, "ProductAddons",
            "CREATE TABLE IF NOT EXISTS ProductAddons (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProductId INTEGER NOT NULL, Name TEXT NOT NULL, Price TEXT NOT NULL DEFAULT 0, IsActive INTEGER NOT NULL DEFAULT 1, SortOrder INTEGER NOT NULL DEFAULT 0)");

        await EnsureTableAsync(db, "Offers",
            "CREATE TABLE IF NOT EXISTS Offers (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL, Description TEXT NULL, ImageUrl TEXT NULL, PriceBefore TEXT NULL, PriceAfter TEXT NULL, Code TEXT NULL, StartsAtUtc TEXT NULL, EndsAtUtc TEXT NULL, IsActive INTEGER NOT NULL DEFAULT 1)");

        await EnsureTableAsync(db, "DeviceTokens",
            "CREATE TABLE IF NOT EXISTS DeviceTokens (Id INTEGER PRIMARY KEY AUTOINCREMENT, UserType INTEGER NOT NULL DEFAULT 0, UserId INTEGER NOT NULL, FcmToken TEXT NOT NULL, Platform TEXT NULL, CreatedAtUtc TEXT NOT NULL, LastSeenAtUtc TEXT NOT NULL)");

        await EnsureTableAsync(db, "Ratings",
            "CREATE TABLE IF NOT EXISTS Ratings (Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderId INTEGER NOT NULL, DriverId INTEGER NOT NULL, CustomerId INTEGER NOT NULL, Stars INTEGER NOT NULL, Comment TEXT NULL, CreatedAtUtc TEXT NOT NULL)");

        await EnsureColumnAsync(db, "Ratings", "StoreStars", "ALTER TABLE Ratings ADD COLUMN StoreStars INTEGER NULL");
        await EnsureColumnAsync(db, "Ratings", "StoreComment", "ALTER TABLE Ratings ADD COLUMN StoreComment TEXT NULL");

        await RenameColumnIfExistsAsync(db, "StoreSettings", "RestaurantName", "StoreName");
        await RenameColumnIfExistsAsync(db, "StoreSettings", "RestaurantLat",  "StoreLat");
        await RenameColumnIfExistsAsync(db, "StoreSettings", "RestaurantLng",  "StoreLng");
        await RenameColumnIfExistsAsync(db, "Ratings",       "RestaurantStars",   "StoreStars");
        await RenameColumnIfExistsAsync(db, "Ratings",       "RestaurantComment", "StoreComment");

        await EnsureTableAsync(db, "OrderRatings",
            "CREATE TABLE IF NOT EXISTS OrderRatings (OrderId INTEGER PRIMARY KEY, StoreRate INTEGER NOT NULL, DriverRate INTEGER NOT NULL, Comment TEXT NULL, StoreComment TEXT NULL, DriverComment TEXT NULL, CreatedAtUtc TEXT NOT NULL)");
        await EnsureColumnAsync(db, "OrderRatings", "StoreComment",  "ALTER TABLE OrderRatings ADD COLUMN StoreComment TEXT NULL");
        await EnsureColumnAsync(db, "OrderRatings", "DriverComment", "ALTER TABLE OrderRatings ADD COLUMN DriverComment TEXT NULL");

        await EnsureTableAsync(db, "CustomerAddresses",
            "CREATE TABLE IF NOT EXISTS CustomerAddresses (Id INTEGER PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL, Title TEXT NOT NULL DEFAULT 'البيت', AddressText TEXT NOT NULL DEFAULT '', Latitude REAL NOT NULL DEFAULT 0, Longitude REAL NOT NULL DEFAULT 0, Building TEXT NULL, Floor TEXT NULL, Apartment TEXT NULL, Notes TEXT NULL, IsDefault INTEGER NOT NULL DEFAULT 0, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL)");
        await ExecAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_OrderRatings_OrderId ON OrderRatings(OrderId);");

        await EnsureColumnAsync(db, "Offers", "ImageUrl", "ALTER TABLE Offers ADD COLUMN ImageUrl TEXT NULL");
        await EnsureColumnAsync(db, "Offers", "PriceBefore", "ALTER TABLE Offers ADD COLUMN PriceBefore TEXT NULL");
        await EnsureColumnAsync(db, "Offers", "PriceAfter", "ALTER TABLE Offers ADD COLUMN PriceAfter TEXT NULL");
        await EnsureColumnAsync(db, "Offers", "Code", "ALTER TABLE Offers ADD COLUMN Code TEXT NULL");

        await EnsureTableAsync(db, "OfferProducts", @"CREATE TABLE OfferProducts (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            OfferId INTEGER NOT NULL,
            ProductId INTEGER NOT NULL
        );");

        await EnsureTableAsync(db, "OfferCategories", @"CREATE TABLE OfferCategories (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            OfferId INTEGER NOT NULL,
            CategoryId INTEGER NOT NULL
        );");

await EnsureTableAsync(db, "Discounts",
    "CREATE TABLE IF NOT EXISTS Discounts (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL DEFAULT 'خصم', TargetType INTEGER NOT NULL, TargetId INTEGER NULL, ValueType INTEGER NOT NULL, Percent REAL NULL, Amount REAL NULL, MinOrderAmount REAL NULL, IsActive INTEGER NOT NULL DEFAULT 1, StartsAtUtc TEXT NULL, EndsAtUtc TEXT NULL, BadgeText TEXT NULL);");
await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_Discounts_Target ON Discounts(TargetType, TargetId);");
await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_Discounts_Active ON Discounts(IsActive);");
        await ExecAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_OfferProducts_Offer_Product ON OfferProducts(OfferId,ProductId);");
        await ExecAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_OfferCategories_Offer_Category ON OfferCategories(OfferId,CategoryId);");

        await EnsureColumnAsync(db, "ProductImages", "IsPrimary", "ALTER TABLE ProductImages ADD COLUMN IsPrimary INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnAsync(db, "Orders", "DriverConfirmedAtUtc", "ALTER TABLE Orders ADD COLUMN DriverConfirmedAtUtc TEXT NULL");
        await EnsureColumnAsync(db, "Orders", "DeliveredAtUtc", "ALTER TABLE Orders ADD COLUMN DeliveredAtUtc TEXT NULL");
        await EnsureColumnAsync(db, "Orders", "OrderEditableUntilUtc", "ALTER TABLE Orders ADD COLUMN OrderEditableUntilUtc TEXT NULL");
        await EnsureColumnAsync(db, "Orders", "CustomerAddressId", "ALTER TABLE Orders ADD COLUMN CustomerAddressId INTEGER NULL");
        await EnsureColumnAsync(db, "Orders", "DeliveryDistanceKm", "ALTER TABLE Orders ADD COLUMN DeliveryDistanceKm REAL NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, "Orders", "DistanceKm", "ALTER TABLE Orders ADD COLUMN DistanceKm REAL NOT NULL DEFAULT 0");

        await EnsureColumnAsync(db, "Orders", "ProcessingEtaMinutes", "ALTER TABLE Orders ADD COLUMN ProcessingEtaMinutes INTEGER NULL");
        await EnsureColumnAsync(db, "Orders", "DeliveryEtaMinutes", "ALTER TABLE Orders ADD COLUMN DeliveryEtaMinutes INTEGER NULL");
        await EnsureColumnAsync(db, "Orders", "ExpectedDeliveryAtUtc", "ALTER TABLE Orders ADD COLUMN ExpectedDeliveryAtUtc TEXT NULL");
        await EnsureColumnAsync(db, "Orders", "LastEtaUpdatedAtUtc", "ALTER TABLE Orders ADD COLUMN LastEtaUpdatedAtUtc TEXT NULL");

        await EnsureColumnAsync(db, "Orders", "IdempotencyKey", "ALTER TABLE Orders ADD COLUMN IdempotencyKey TEXT NULL");
        await EnsureColumnAsync(db, "Orders", "CancelReasonCode", "ALTER TABLE Orders ADD COLUMN CancelReasonCode TEXT NULL");

await EnsureColumnAsync(db, "Orders", "TotalBeforeDiscount", "ALTER TABLE Orders ADD COLUMN TotalBeforeDiscount REAL NOT NULL DEFAULT 0");
await EnsureColumnAsync(db, "Orders", "CartDiscount", "ALTER TABLE Orders ADD COLUMN CartDiscount REAL NOT NULL DEFAULT 0");

        await EnsureColumnAsync(db, "StoreSettings", "StoreLat", "ALTER TABLE StoreSettings ADD COLUMN StoreLat REAL NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, "StoreSettings", "StoreLng", "ALTER TABLE StoreSettings ADD COLUMN StoreLng REAL NOT NULL DEFAULT 0");

        await EnsureColumnAsync(db, "StoreSettings", "IsAcceptingOrders", "ALTER TABLE StoreSettings ADD COLUMN IsAcceptingOrders INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, "StoreSettings", "IsManuallyClosed", "ALTER TABLE StoreSettings ADD COLUMN IsManuallyClosed INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, "StoreSettings", "ClosedMessage", "ALTER TABLE StoreSettings ADD COLUMN ClosedMessage TEXT NOT NULL DEFAULT 'المتجر مغلق حالياً'");
        await EnsureColumnAsync(db, "StoreSettings", "ClosedScreenImageUrl", "ALTER TABLE StoreSettings ADD COLUMN ClosedScreenImageUrl TEXT NULL");

        await EnsureColumnAsync(db, "StoreSettings", "FacebookUrl", "ALTER TABLE StoreSettings ADD COLUMN FacebookUrl TEXT NULL");
        await EnsureColumnAsync(db, "StoreSettings", "InstagramUrl", "ALTER TABLE StoreSettings ADD COLUMN InstagramUrl TEXT NULL");
        await EnsureColumnAsync(db, "StoreSettings", "TelegramUrl", "ALTER TABLE StoreSettings ADD COLUMN TelegramUrl TEXT NULL");

        await EnsureColumnAsync(db, "StoreSettings", "OffersColorHex", "ALTER TABLE StoreSettings ADD COLUMN OffersColorHex TEXT NOT NULL DEFAULT '#D4AF37'");
        await EnsureColumnAsync(db, "StoreSettings", "WelcomeText", "ALTER TABLE StoreSettings ADD COLUMN WelcomeText TEXT NOT NULL DEFAULT 'أهلاً بك'");
        await EnsureColumnAsync(db, "StoreSettings", "OnboardingJson", "ALTER TABLE StoreSettings ADD COLUMN OnboardingJson TEXT NULL");
        await EnsureColumnAsync(db, "StoreSettings", "HomeBannersJson", "ALTER TABLE StoreSettings ADD COLUMN HomeBannersJson TEXT NULL");
        await EnsureColumnAsync(db, "StoreSettings", "SplashBackground1Url", "ALTER TABLE StoreSettings ADD COLUMN SplashBackground1Url TEXT NULL");
        await EnsureColumnAsync(db, "StoreSettings", "SplashBackground2Url", "ALTER TABLE StoreSettings ADD COLUMN SplashBackground2Url TEXT NULL");
        await EnsureColumnAsync(db, "StoreSettings", "RoutingProfile", "ALTER TABLE StoreSettings ADD COLUMN RoutingProfile TEXT NOT NULL DEFAULT 'driving'");

        await EnsureColumnAsync(db, "StoreSettings", "DriverSpeedBikeKmH", "ALTER TABLE StoreSettings ADD COLUMN DriverSpeedBikeKmH TEXT NOT NULL DEFAULT 18");
        await EnsureColumnAsync(db, "StoreSettings", "DriverSpeedCarKmH", "ALTER TABLE StoreSettings ADD COLUMN DriverSpeedCarKmH TEXT NOT NULL DEFAULT 30");

        await EnsureColumnAsync(db, "StoreSettings", "DeliveryFeePerKm", "ALTER TABLE StoreSettings ADD COLUMN DeliveryFeePerKm REAL NOT NULL DEFAULT 0");

        await EnsureColumnAsync(db, "StoreSettings", "UpdatedAtUtc", "ALTER TABLE StoreSettings ADD COLUMN UpdatedAtUtc TEXT NOT NULL DEFAULT (datetime('now'))");

        await EnsureColumnAsync(db, "Categories", "ImageUrl", "ALTER TABLE Categories ADD COLUMN ImageUrl TEXT NULL");

        await EnsureColumnAsync(db, "Categories", "IsActive", "ALTER TABLE Categories ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(db, "Categories", "SortOrder", "ALTER TABLE Categories ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnAsync(db, "Customers", "Email", "ALTER TABLE Customers ADD COLUMN Email TEXT NULL");

        await EnsureColumnAsync(db, "Customers", "LastLat", "ALTER TABLE Customers ADD COLUMN LastLat REAL NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, "Customers", "LastLng", "ALTER TABLE Customers ADD COLUMN LastLng REAL NOT NULL DEFAULT 0");

        await EnsureColumnAsync(db, "Customers", "IsChatBlocked", "ALTER TABLE Customers ADD COLUMN IsChatBlocked INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnAsync(db, "Customers", "IsAppBlocked", "ALTER TABLE Customers ADD COLUMN IsAppBlocked INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnAsync(db, "OrderStatusHistory", "ChangedByType", "ALTER TABLE OrderStatusHistory ADD COLUMN ChangedByType TEXT NULL");
        await EnsureColumnAsync(db, "OrderStatusHistory", "ChangedById", "ALTER TABLE OrderStatusHistory ADD COLUMN ChangedById INTEGER NULL");
        await EnsureColumnAsync(db, "OrderStatusHistory", "ReasonCode", "ALTER TABLE OrderStatusHistory ADD COLUMN ReasonCode TEXT NULL");

        await EnsureColumnAsync(db, "DriverLocations", "AccuracyMeters", "ALTER TABLE DriverLocations ADD COLUMN AccuracyMeters REAL NOT NULL DEFAULT 0");

        await EnsureColumnAsync(db, "DriverTrackPoints", "OrderId", "ALTER TABLE DriverTrackPoints ADD COLUMN OrderId INTEGER NULL");

        await EnsureColumnAsync(db, "ComplaintMessages", "IdempotencyKey", "ALTER TABLE ComplaintMessages ADD COLUMN IdempotencyKey TEXT NOT NULL DEFAULT ''");

        await EnsureColumnAsync(db, "ComplaintThreads", "LastAdminSeenAtUtc", "ALTER TABLE ComplaintThreads ADD COLUMN LastAdminSeenAtUtc TEXT NULL");
        await EnsureColumnAsync(db, "ComplaintThreads", "LastCustomerSeenAtUtc", "ALTER TABLE ComplaintThreads ADD COLUMN LastCustomerSeenAtUtc TEXT NULL");

        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_ProductImages_ProductId ON ProductImages(ProductId);");
        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_ProductVariants_ProductId ON ProductVariants(ProductId);");
        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_ProductAddons_ProductId ON ProductAddons(ProductId);");

        await ExecAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Ratings_OrderId ON Ratings(OrderId);");
        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_Ratings_DriverId ON Ratings(DriverId);");

        await EnsureColumnAsync(db, "Ratings", "StoreStars", "ALTER TABLE Ratings ADD COLUMN StoreStars INTEGER NULL");
        await EnsureColumnAsync(db, "Ratings", "StoreComment", "ALTER TABLE Ratings ADD COLUMN StoreComment TEXT NULL");

        await RenameColumnIfExistsAsync(db, "StoreSettings", "RestaurantName", "StoreName");
        await RenameColumnIfExistsAsync(db, "StoreSettings", "RestaurantLat",  "StoreLat");
        await RenameColumnIfExistsAsync(db, "StoreSettings", "RestaurantLng",  "StoreLng");
        await RenameColumnIfExistsAsync(db, "Ratings",       "RestaurantStars",   "StoreStars");
        await RenameColumnIfExistsAsync(db, "Ratings",       "RestaurantComment", "StoreComment");

        await ExecAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_DeviceTokens_FcmToken ON DeviceTokens(FcmToken);");
        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_DeviceTokens_User ON DeviceTokens(UserType,UserId);");

        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_Customers_Phone ON Customers(Phone);");

        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_Orders_IdempotencyKey ON Orders(IdempotencyKey);");

        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_DriverTrackPoints_OrderId ON DriverTrackPoints(OrderId);");

        await EnsureColumnAsync(db, "Products", "IsAvailable", "ALTER TABLE Products ADD COLUMN IsAvailable INTEGER NOT NULL DEFAULT 1");
        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_Products_IsAvailable ON Products(IsAvailable);");

        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_DriverTrackPoints_DriverId_CreatedAt ON DriverTrackPoints(DriverId,CreatedAtUtc);");
        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_DriverTrackPoints_OrderId_CreatedAt ON DriverTrackPoints(OrderId,CreatedAtUtc);");

        await EnsureColumnAsync(db, "Products", "AgentId", "ALTER TABLE Products ADD COLUMN AgentId INTEGER NULL");
        await EnsureColumnAsync(db, "Products", "StockQuantity", "ALTER TABLE Products ADD COLUMN StockQuantity INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, "Products", "TrackStock", "ALTER TABLE Products ADD COLUMN TrackStock INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, "Products", "ImageUrl", "ALTER TABLE Products ADD COLUMN ImageUrl TEXT NULL");

        await EnsureColumnAsync(db, "Agents", "PasswordHash", "ALTER TABLE Agents ADD COLUMN PasswordHash TEXT NULL");
        await EnsureColumnAsync(db, "Agents", "CommissionPercent", "ALTER TABLE Agents ADD COLUMN CommissionPercent TEXT NOT NULL DEFAULT 0");
        await EnsureColumnAsync(db, "Agents", "CreatedAtUtc", "ALTER TABLE Agents ADD COLUMN CreatedAtUtc TEXT NOT NULL DEFAULT (datetime(\'now\'))");

        await EnsureColumnAsync(db, "Drivers", "PasswordHash", "ALTER TABLE Drivers ADD COLUMN PasswordHash TEXT NULL");

        await EnsureTableAsync(db, "OrderAgentItems",
            "CREATE TABLE IF NOT EXISTS OrderAgentItems (Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderId INTEGER NOT NULL, AgentId INTEGER NOT NULL, AgentStatus INTEGER NOT NULL DEFAULT 0, AutoAcceptAt TEXT NOT NULL, RejectionReason TEXT NULL, CommissionPercent TEXT NOT NULL DEFAULT 0, AgentSubtotal TEXT NOT NULL DEFAULT 0, CreatedAtUtc TEXT NOT NULL, RespondedAtUtc TEXT NULL)");
        await ExecAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_OrderAgentItems_OrderId_AgentId ON OrderAgentItems(OrderId,AgentId);");
        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_OrderAgentItems_AgentStatus ON OrderAgentItems(AgentStatus);");

        await EnsureTableAsync(db, "AgentCommissions",
            "CREATE TABLE IF NOT EXISTS AgentCommissions (Id INTEGER PRIMARY KEY AUTOINCREMENT, AgentId INTEGER NOT NULL, OrderId INTEGER NOT NULL, SaleAmount TEXT NOT NULL DEFAULT 0, CommissionPercent TEXT NOT NULL DEFAULT 0, CommissionAmount TEXT NOT NULL DEFAULT 0, CreatedAtUtc TEXT NOT NULL, SettledAt TEXT NULL)");

        await EnsureTableAsync(db, "ProductAgentChats",
            "CREATE TABLE IF NOT EXISTS ProductAgentChats (Id INTEGER PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL, AgentId INTEGER NOT NULL, ProductId INTEGER NULL, CreatedAtUtc TEXT NOT NULL)");

        await EnsureTableAsync(db, "ProductAgentChatMessages",
            "CREATE TABLE IF NOT EXISTS ProductAgentChatMessages (Id INTEGER PRIMARY KEY AUTOINCREMENT, ThreadId INTEGER NOT NULL, FromAgent INTEGER NOT NULL DEFAULT 0, Message TEXT NOT NULL DEFAULT \'\', CreatedAtUtc TEXT NOT NULL)");
        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_ProductAgentChatMessages_ThreadId ON ProductAgentChatMessages(ThreadId);");

        await EnsureTableAsync(db, "DeliveryZones",
            "CREATE TABLE IF NOT EXISTS DeliveryZones (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL DEFAULT \'\', Fee TEXT NOT NULL DEFAULT 0, MinOrder TEXT NULL, IsActive INTEGER NOT NULL DEFAULT 1, SortOrder INTEGER NOT NULL DEFAULT 1, PolygonJson TEXT NULL)");
        await ExecAsync(db, "CREATE INDEX IF NOT EXISTS IX_DeliveryZones_IsActive_SortOrder ON DeliveryZones(IsActive,SortOrder);");

        await EnsureTableAsync(db, "CustomerFavorites",
            "CREATE TABLE IF NOT EXISTS CustomerFavorites (Id INTEGER PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL, ProductId INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL DEFAULT (datetime('now')))");
        await ExecAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_CustomerFavorites_CustomerId_ProductId ON CustomerFavorites(CustomerId,ProductId);");

        await EnsureTableAsync(db, "ProductRatings",
            "CREATE TABLE IF NOT EXISTS ProductRatings (Id INTEGER PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL, ProductId INTEGER NOT NULL, OrderId INTEGER NOT NULL, Stars INTEGER NOT NULL, Comment TEXT NULL, CreatedAtUtc TEXT NOT NULL DEFAULT (datetime('now')))");
        await ExecAsync(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_ProductRatings_Customer_Product_Order ON ProductRatings(CustomerId,ProductId,OrderId);");

        // أعمدة الرد التلقائي بالذكاء الاصطناعي
        await EnsureColumnAsync(db, "StoreSettings", "AiAutoReplyEnabled", "ALTER TABLE StoreSettings ADD COLUMN AiAutoReplyEnabled INTEGER NOT NULL DEFAULT 1");
        await EnsureColumnAsync(db, "StoreSettings", "AiAutoReplySystemPrompt", "ALTER TABLE StoreSettings ADD COLUMN AiAutoReplySystemPrompt TEXT NULL");
    }

    private static async Task EnsureTableAsync(AppDbContext db, string table, string createSql)
    {
        
        await using var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$name";
        var p = cmd.CreateParameter();
        p.ParameterName = "$name";
        p.Value = table;
        cmd.Parameters.Add(p);
        var result = await cmd.ExecuteScalarAsync();
        if (result == null)
            await ExecAsync(db, createSql);
    }

    private static async Task RenameColumnIfExistsAsync(AppDbContext db, string table, string oldCol, string newCol)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{oldCol}'";
            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
            if (exists == 0) return; 

            checkCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{newCol}'";
            var newExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
            if (newExists > 0) return; 

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE \"{table}\" RENAME COLUMN \"{oldCol}\" TO \"{newCol}\"";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            
            Console.WriteLine($"[SchemaUpgrader] RenameColumn {table}.{oldCol}→{newCol} failed: {ex.Message}");
        }
    }

    private static async Task EnsureColumnAsync(AppDbContext db, string table, string column, string alterSql)
    {
        await using var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        await ExecAsync(db, alterSql);
    }

    private static Task ExecAsync(AppDbContext db, string sql)
        => db.Database.ExecuteSqlRawAsync(sql);
}