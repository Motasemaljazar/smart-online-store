using Microsoft.EntityFrameworkCore;

namespace AdminDashboard.Data;

public static class MySqlSchemaUpgrader
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!db.Database.ProviderName!.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            return;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await EnsureStoreSettingsTableAsync(db, conn);

        await EnsureColumnMySqlAsync(conn, "Agents", "PasswordHash", "VARCHAR(256) NULL");
        await EnsureColumnMySqlAsync(conn, "Agents", "CommissionPercent", "DECIMAL(10,2) NOT NULL DEFAULT 0");
        await EnsureColumnMySqlAsync(conn, "Agents", "CreatedAtUtc", "DATETIME(6) NOT NULL DEFAULT '0001-01-01 00:00:00'");

        await EnsureColumnMySqlAsync(conn, "Drivers", "PasswordHash", "VARCHAR(256) NULL");

        await EnsureColumnMySqlAsync(conn, "Products", "IsAvailable", "TINYINT(1) NOT NULL DEFAULT 1");
        await EnsureColumnMySqlAsync(conn, "Products", "AgentId", "INT NULL");
        await EnsureColumnMySqlAsync(conn, "Products", "StockQuantity", "INT NOT NULL DEFAULT 0");
        await EnsureColumnMySqlAsync(conn, "Products", "TrackStock", "TINYINT(1) NOT NULL DEFAULT 0");
        await EnsureColumnMySqlAsync(conn, "Products", "ImageUrl", "LONGTEXT NULL");

        await EnsureColumnMySqlAsync(conn, "Categories", "ImageUrl", "LONGTEXT NULL");
        await EnsureColumnMySqlAsync(conn, "Categories", "IsActive", "TINYINT(1) NOT NULL DEFAULT 1");
        await EnsureColumnMySqlAsync(conn, "Categories", "SortOrder", "INT NOT NULL DEFAULT 0");

        await EnsureColumnMySqlAsync(conn, "Customers", "Email", "VARCHAR(180) NULL");
        await EnsureColumnMySqlAsync(conn, "Customers", "LastLat", "DOUBLE NOT NULL DEFAULT 0");
        await EnsureColumnMySqlAsync(conn, "Customers", "LastLng", "DOUBLE NOT NULL DEFAULT 0");
        await EnsureColumnMySqlAsync(conn, "Customers", "IsChatBlocked", "TINYINT(1) NOT NULL DEFAULT 0");
        await EnsureColumnMySqlAsync(conn, "Customers", "IsAppBlocked", "TINYINT(1) NOT NULL DEFAULT 0");

        await EnsureColumnMySqlAsync(conn, "Orders", "DriverConfirmedAtUtc", "DATETIME(6) NULL");
        await EnsureColumnMySqlAsync(conn, "Orders", "DeliveredAtUtc", "DATETIME(6) NULL");
        await EnsureColumnMySqlAsync(conn, "Orders", "OrderEditableUntilUtc", "DATETIME(6) NULL");
        await EnsureColumnMySqlAsync(conn, "Orders", "CustomerAddressId", "INT NULL");
        await EnsureColumnMySqlAsync(conn, "Orders", "DeliveryDistanceKm", "DOUBLE NOT NULL DEFAULT 0");
        await EnsureColumnMySqlAsync(conn, "Orders", "DistanceKm", "DOUBLE NOT NULL DEFAULT 0");
        await EnsureColumnMySqlAsync(conn, "Orders", "ProcessingEtaMinutes", "INT NULL");
        await EnsureColumnMySqlAsync(conn, "Orders", "DeliveryEtaMinutes", "INT NULL");
        await EnsureColumnMySqlAsync(conn, "Orders", "ExpectedDeliveryAtUtc", "DATETIME(6) NULL");
        await EnsureColumnMySqlAsync(conn, "Orders", "LastEtaUpdatedAtUtc", "DATETIME(6) NULL");
        await EnsureColumnMySqlAsync(conn, "Orders", "IdempotencyKey", "VARCHAR(80) NULL");
        await EnsureColumnMySqlAsync(conn, "Orders", "CancelReasonCode", "VARCHAR(64) NULL");
        await EnsureColumnMySqlAsync(conn, "Orders", "TotalBeforeDiscount", "DECIMAL(65,30) NOT NULL DEFAULT 0");
        await EnsureColumnMySqlAsync(conn, "Orders", "CartDiscount", "DECIMAL(65,30) NOT NULL DEFAULT 0");

        await EnsureColumnMySqlAsync(conn, "ComplaintMessages", "IdempotencyKey", "VARCHAR(120) NOT NULL DEFAULT ''");

        await EnsureColumnMySqlAsync(conn, "ComplaintThreads", "LastAdminSeenAtUtc", "DATETIME(6) NULL");
        await EnsureColumnMySqlAsync(conn, "ComplaintThreads", "LastCustomerSeenAtUtc", "DATETIME(6) NULL");

        await EnsureColumnMySqlAsync(conn, "StoreSettings", "OffersColorHex", "VARCHAR(16) NOT NULL DEFAULT '#D4AF37'");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "WelcomeText", "VARCHAR(200) NOT NULL DEFAULT 'أهلاً بك'");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "OnboardingJson", "LONGTEXT NULL");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "HomeBannersJson", "LONGTEXT NULL");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "SplashBackground1Url", "LONGTEXT NULL");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "SplashBackground2Url", "LONGTEXT NULL");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "FacebookUrl", "VARCHAR(400) NULL");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "InstagramUrl", "VARCHAR(400) NULL");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "TelegramUrl", "VARCHAR(400) NULL");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "DriverSpeedBikeKmH", "DECIMAL(65,30) NOT NULL DEFAULT 18");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "DriverSpeedCarKmH", "DECIMAL(65,30) NOT NULL DEFAULT 30");
        await EnsureColumnMySqlAsync(conn, "StoreSettings", "DeliveryFeePerKm", "DECIMAL(65,30) NOT NULL DEFAULT 0");

        await EnsureColumnMySqlAsync(conn, "DriverLocations", "AccuracyMeters", "DOUBLE NOT NULL DEFAULT 0");

        await EnsureColumnMySqlAsync(conn, "DriverTrackPoints", "OrderId", "INT NULL");

        await EnsureColumnMySqlAsync(conn, "OrderStatusHistory", "ChangedByType", "VARCHAR(32) NULL");
        await EnsureColumnMySqlAsync(conn, "OrderStatusHistory", "ChangedById", "INT NULL");
        await EnsureColumnMySqlAsync(conn, "OrderStatusHistory", "ReasonCode", "VARCHAR(64) NULL");

        await EnsureTableMySqlAsync(conn, "ProductImages", @"
            CREATE TABLE `ProductImages` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `ProductId` INT NOT NULL,
                `Url` VARCHAR(400) NOT NULL DEFAULT '',
                `SortOrder` INT NOT NULL DEFAULT 0,
                `IsPrimary` TINYINT(1) NOT NULL DEFAULT 0,
                PRIMARY KEY (`Id`),
                KEY `IX_ProductImages_ProductId` (`ProductId`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "ProductVariants", @"
            CREATE TABLE `ProductVariants` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `ProductId` INT NOT NULL,
                `Name` VARCHAR(120) NOT NULL DEFAULT '',
                `PriceDelta` DECIMAL(65,30) NOT NULL DEFAULT 0,
                `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
                `SortOrder` INT NOT NULL DEFAULT 0,
                PRIMARY KEY (`Id`),
                KEY `IX_ProductVariants_ProductId` (`ProductId`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "ProductAddons", @"
            CREATE TABLE `ProductAddons` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `ProductId` INT NOT NULL,
                `Name` VARCHAR(120) NOT NULL DEFAULT '',
                `Price` DECIMAL(65,30) NOT NULL DEFAULT 0,
                `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
                `SortOrder` INT NOT NULL DEFAULT 0,
                PRIMARY KEY (`Id`),
                KEY `IX_ProductAddons_ProductId` (`ProductId`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "CustomerAddresses", @"
            CREATE TABLE `CustomerAddresses` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `CustomerId` INT NOT NULL,
                `Title` VARCHAR(80) NOT NULL DEFAULT 'البيت',
                `AddressText` LONGTEXT NOT NULL,
                `Latitude` DOUBLE NOT NULL DEFAULT 0,
                `Longitude` DOUBLE NOT NULL DEFAULT 0,
                `Building` VARCHAR(60) NULL,
                `Floor` VARCHAR(30) NULL,
                `Apartment` VARCHAR(30) NULL,
                `Notes` LONGTEXT NULL,
                `IsDefault` TINYINT(1) NOT NULL DEFAULT 0,
                `CreatedAtUtc` DATETIME(6) NOT NULL,
                `UpdatedAtUtc` DATETIME(6) NOT NULL,
                PRIMARY KEY (`Id`),
                KEY `IX_CustomerAddresses_CustomerId` (`CustomerId`, `IsDefault`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "DeviceTokens", @"
            CREATE TABLE `DeviceTokens` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `UserType` INT NOT NULL DEFAULT 0,
                `UserId` INT NOT NULL,
                `FcmToken` VARCHAR(512) NOT NULL DEFAULT '',
                `Platform` VARCHAR(32) NULL,
                `CreatedAtUtc` DATETIME(6) NOT NULL,
                `LastSeenAtUtc` DATETIME(6) NOT NULL,
                PRIMARY KEY (`Id`),
                UNIQUE KEY `IX_DeviceTokens_FcmToken` (`FcmToken`(191)),
                KEY `IX_DeviceTokens_User` (`UserType`, `UserId`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "OrderRatings", @"
            CREATE TABLE `OrderRatings` (
                `OrderId` INT NOT NULL,
                `StoreRate` INT NOT NULL,
                `DriverRate` INT NOT NULL,
                `Comment` LONGTEXT NULL,
                `StoreComment` LONGTEXT NULL,
                `DriverComment` LONGTEXT NULL,
                `CreatedAtUtc` DATETIME(6) NOT NULL,
                PRIMARY KEY (`OrderId`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "CustomerFavorites", @"
            CREATE TABLE `CustomerFavorites` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `CustomerId` INT NOT NULL,
                `ProductId` INT NOT NULL,
                `CreatedAtUtc` DATETIME(6) NOT NULL DEFAULT (NOW()),
                PRIMARY KEY (`Id`),
                UNIQUE KEY `IX_CustomerFavorites_CustomerId_ProductId` (`CustomerId`, `ProductId`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "ProductRatings", @"
            CREATE TABLE `ProductRatings` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `CustomerId` INT NOT NULL,
                `ProductId` INT NOT NULL,
                `OrderId` INT NOT NULL,
                `Stars` INT NOT NULL,
                `Comment` LONGTEXT NULL,
                `CreatedAtUtc` DATETIME(6) NOT NULL DEFAULT (NOW()),
                PRIMARY KEY (`Id`),
                UNIQUE KEY `IX_ProductRatings_Customer_Product_Order` (`CustomerId`, `ProductId`, `OrderId`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "Offers", @"
            CREATE TABLE `Offers` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `Title` VARCHAR(200) NOT NULL DEFAULT '',
                `Description` LONGTEXT NULL,
                `ImageUrl` LONGTEXT NULL,
                `PriceBefore` DECIMAL(65,30) NULL,
                `PriceAfter` DECIMAL(65,30) NULL,
                `Code` VARCHAR(60) NULL,
                `StartsAtUtc` DATETIME(6) NULL,
                `EndsAtUtc` DATETIME(6) NULL,
                `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
                PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "OfferProducts", @"
            CREATE TABLE `OfferProducts` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `OfferId` INT NOT NULL,
                `ProductId` INT NOT NULL,
                PRIMARY KEY (`Id`),
                UNIQUE KEY `IX_OfferProducts_Offer_Product` (`OfferId`, `ProductId`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "OfferCategories", @"
            CREATE TABLE `OfferCategories` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `OfferId` INT NOT NULL,
                `CategoryId` INT NOT NULL,
                PRIMARY KEY (`Id`),
                UNIQUE KEY `IX_OfferCategories_Offer_Category` (`OfferId`, `CategoryId`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "Discounts", @"
            CREATE TABLE `Discounts` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `Title` VARCHAR(120) NOT NULL DEFAULT 'خصم',
                `TargetType` INT NOT NULL,
                `TargetId` INT NULL,
                `ValueType` INT NOT NULL,
                `Percent` DOUBLE NULL,
                `Amount` DECIMAL(65,30) NULL,
                `MinOrderAmount` DECIMAL(65,30) NULL,
                `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
                `StartsAtUtc` DATETIME(6) NULL,
                `EndsAtUtc` DATETIME(6) NULL,
                `BadgeText` VARCHAR(60) NULL,
                PRIMARY KEY (`Id`),
                KEY `IX_Discounts_Target` (`TargetType`, `TargetId`),
                KEY `IX_Discounts_Active` (`IsActive`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "DeliveryZones", @"
            CREATE TABLE `DeliveryZones` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `Name` VARCHAR(80) NOT NULL DEFAULT '',
                `Fee` DECIMAL(65,30) NOT NULL DEFAULT 0,
                `MinOrder` DECIMAL(65,30) NULL,
                `IsActive` TINYINT(1) NOT NULL DEFAULT 1,
                `SortOrder` INT NOT NULL DEFAULT 1,
                `PolygonJson` LONGTEXT NULL,
                PRIMARY KEY (`Id`),
                KEY `IX_DeliveryZones_IsActive_SortOrder` (`IsActive`, `SortOrder`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "OrderAgentItems", @"
            CREATE TABLE `OrderAgentItems` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `OrderId` INT NOT NULL,
                `AgentId` INT NOT NULL,
                `AgentStatus` INT NOT NULL DEFAULT 0,
                `AutoAcceptAt` DATETIME(6) NOT NULL,
                `RejectionReason` VARCHAR(500) NULL,
                `CommissionPercent` DECIMAL(10,2) NOT NULL DEFAULT 0,
                `AgentSubtotal` DECIMAL(65,30) NOT NULL DEFAULT 0,
                `CreatedAtUtc` DATETIME(6) NOT NULL,
                `RespondedAtUtc` DATETIME(6) NULL,
                PRIMARY KEY (`Id`),
                UNIQUE KEY `IX_OrderAgentItems_OrderId_AgentId` (`OrderId`, `AgentId`),
                KEY `IX_OrderAgentItems_AgentStatus` (`AgentStatus`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "AgentCommissions", @"
            CREATE TABLE `AgentCommissions` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `AgentId` INT NOT NULL,
                `OrderId` INT NOT NULL,
                `SaleAmount` DECIMAL(65,30) NOT NULL DEFAULT 0,
                `CommissionPercent` DECIMAL(10,2) NOT NULL DEFAULT 0,
                `CommissionAmount` DECIMAL(65,30) NOT NULL DEFAULT 0,
                `CreatedAtUtc` DATETIME(6) NOT NULL,
                `SettledAt` DATETIME(6) NULL,
                PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "ProductAgentChats", @"
            CREATE TABLE `ProductAgentChats` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `CustomerId` INT NOT NULL,
                `AgentId` INT NOT NULL,
                `ProductId` INT NULL,
                `CreatedAtUtc` DATETIME(6) NOT NULL,
                PRIMARY KEY (`Id`)
            ) CHARACTER SET utf8mb4");

        await EnsureTableMySqlAsync(conn, "ProductAgentChatMessages", @"
            CREATE TABLE `ProductAgentChatMessages` (
                `Id` INT NOT NULL AUTO_INCREMENT,
                `ThreadId` INT NOT NULL,
                `FromAgent` TINYINT(1) NOT NULL DEFAULT 0,
                `Message` VARCHAR(2000) NOT NULL DEFAULT '',
                `CreatedAtUtc` DATETIME(6) NOT NULL,
                PRIMARY KEY (`Id`),
                KEY `IX_ProductAgentChatMessages_ThreadId` (`ThreadId`)
            ) CHARACTER SET utf8mb4");
    }

    private static async Task RenameMySqlColumnIfExistsAsync(
        System.Data.Common.DbConnection conn, string table, string oldCol, string newCol, string colDef)
    {
        try
        {
            using var check = conn.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{table}' AND COLUMN_NAME='{oldCol}'";
            var exists = Convert.ToInt32(await check.ExecuteScalarAsync());
            if (exists == 0) return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE `{table}` CHANGE `{oldCol}` `{newCol}` {colDef}";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MySqlSchemaUpgrader] Rename {table}.{oldCol}→{newCol} failed: {ex.Message}");
        }
    }

    private static async Task EnsureTableMySqlAsync(System.Data.Common.DbConnection conn, string table, string createSql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = @tbl";
        var p = cmd.CreateParameter(); p.ParameterName = "@tbl"; p.Value = table;
        cmd.Parameters.Add(p);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        if (count > 0) return;

        await using var createCmd = conn.CreateCommand();
        createCmd.CommandText = createSql;
        await createCmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureColumnMySqlAsync(System.Data.Common.DbConnection conn, string table, string column, string colDef)
    {
        
        await using (var checkTable = conn.CreateCommand())
        {
            checkTable.CommandText = @"
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = DATABASE() AND table_name = @tbl";
            var p = checkTable.CreateParameter(); p.ParameterName = "@tbl"; p.Value = table;
            checkTable.Parameters.Add(p);
            if (Convert.ToInt32(await checkTable.ExecuteScalarAsync()) == 0) return;
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = @tbl AND column_name = @col";
        var p1 = cmd.CreateParameter(); p1.ParameterName = "@tbl"; p1.Value = table; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@col"; p2.Value = column; cmd.Parameters.Add(p2);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        if (count > 0) return;

        await using var alterCmd = conn.CreateCommand();
        alterCmd.CommandText = $"ALTER TABLE `{table}` ADD COLUMN `{column}` {colDef}";
        await alterCmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureStoreSettingsTableAsync(AppDbContext db, System.Data.Common.DbConnection conn)
    {
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = DATABASE() AND table_name = 'StoreSettings'";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (count > 0) return;
        }

        bool oldExists;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = DATABASE() AND table_name = 'RestaurantSettings'";
            oldExists = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        if (oldExists)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "RENAME TABLE `RestaurantSettings` TO `StoreSettings`";
            await cmd.ExecuteNonQueryAsync();
        }

        await RenameMySqlColumnIfExistsAsync(conn, "StoreSettings", "RestaurantName", "StoreName",   "VARCHAR(200) NOT NULL DEFAULT ''");
        await RenameMySqlColumnIfExistsAsync(conn, "StoreSettings", "RestaurantLat",  "StoreLat",    "DOUBLE NOT NULL DEFAULT 0");
        await RenameMySqlColumnIfExistsAsync(conn, "StoreSettings", "RestaurantLng",  "StoreLng",    "DOUBLE NOT NULL DEFAULT 0");
        await RenameMySqlColumnIfExistsAsync(conn, "Ratings",       "RestaurantStars",   "StoreStars",   "INT NULL");
        await RenameMySqlColumnIfExistsAsync(conn, "Ratings",       "RestaurantComment", "StoreComment", "TEXT NULL");
        await EnsureColumnMySqlAsync(conn, "OrderRatings", "StoreComment",  "LONGTEXT NULL");
        await EnsureColumnMySqlAsync(conn, "OrderRatings", "DriverComment", "LONGTEXT NULL");
        if (false) { 
        }
        else
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE `StoreSettings` (
                    `Id` INT NOT NULL AUTO_INCREMENT,
                    `StoreName` VARCHAR(200) NOT NULL DEFAULT '',
                    `LogoUrl` LONGTEXT NULL,
                    `CustomerSplashUrl` LONGTEXT NULL,
                    `DriverSplashUrl` LONGTEXT NULL,
                    `SplashBackground1Url` LONGTEXT NULL,
                    `SplashBackground2Url` LONGTEXT NULL,
                    `PrimaryColorHex` VARCHAR(16) NOT NULL DEFAULT '#5C4A8E',
                    `SecondaryColorHex` VARCHAR(16) NOT NULL DEFAULT '#111827',
                    `OffersColorHex` VARCHAR(16) NOT NULL DEFAULT '#D4AF37',
                    `WelcomeText` VARCHAR(200) NOT NULL DEFAULT 'أهلاً بك',
                    `OnboardingJson` LONGTEXT NULL,
                    `HomeBannersJson` LONGTEXT NULL,
                    `WorkHours` VARCHAR(64) NOT NULL DEFAULT '',
                    `StoreLat` DOUBLE NOT NULL DEFAULT 0,
                    `StoreLng` DOUBLE NOT NULL DEFAULT 0,
                    `IsManuallyClosed` TINYINT(1) NOT NULL DEFAULT 0,
                    `ClosedMessage` VARCHAR(250) NOT NULL DEFAULT 'المتجر مغلق حالياً',
                    `ClosedScreenImageUrl` LONGTEXT NULL,
                    `MinOrderAmount` DECIMAL(65,30) NOT NULL DEFAULT 0,
                    `DeliveryFeeType` INT NOT NULL DEFAULT 0,
                    `DeliveryFeeValue` DECIMAL(65,30) NOT NULL DEFAULT 0,
                    `DeliveryFeePerKm` DECIMAL(65,30) NOT NULL DEFAULT 0,
                    `SupportPhone` VARCHAR(64) NOT NULL DEFAULT '',
                    `SupportWhatsApp` VARCHAR(64) NOT NULL DEFAULT '',
                    `FacebookUrl` VARCHAR(400) NULL,
                    `InstagramUrl` VARCHAR(400) NULL,
                    `TelegramUrl` VARCHAR(400) NULL,
                    `IsAcceptingOrders` TINYINT(1) NOT NULL DEFAULT 0,
                    `RoutingProfile` VARCHAR(16) NOT NULL DEFAULT 'driving',
                    `UpdatedAtUtc` DATETIME(6) NOT NULL DEFAULT '0001-01-01 00:00:00',
                    `DriverSpeedBikeKmH` DECIMAL(65,30) NOT NULL DEFAULT 18,
                    `DriverSpeedCarKmH` DECIMAL(65,30) NOT NULL DEFAULT 30,
                    PRIMARY KEY (`Id`)
                ) CHARACTER SET utf8mb4";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
