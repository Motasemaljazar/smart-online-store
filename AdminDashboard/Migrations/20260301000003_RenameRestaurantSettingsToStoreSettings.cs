using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminDashboard.Migrations
{

    public partial class RenameRestaurantSettingsToStoreSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql(@"
                SET @tbl_old = (
                    SELECT COUNT(*) FROM information_schema.tables
                    WHERE table_schema = DATABASE() AND table_name = 'RestaurantSettings'
                );
                SET @tbl_new = (
                    SELECT COUNT(*) FROM information_schema.tables
                    WHERE table_schema = DATABASE() AND table_name = 'StoreSettings'
                );

                -- Only rename if old exists and new doesn't
                SET @do_rename = IF(@tbl_old > 0 AND @tbl_new = 0, 1, 0);
            ");

            migrationBuilder.Sql(@"
                DROP PROCEDURE IF EXISTS _MigrateRestaurantSettings;
                CREATE PROCEDURE _MigrateRestaurantSettings()
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = DATABASE() AND table_name = 'RestaurantSettings'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = DATABASE() AND table_name = 'StoreSettings'
                    ) THEN
                        RENAME TABLE RestaurantSettings TO StoreSettings;
                    END IF;

                    -- Create from scratch if neither exists
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = DATABASE() AND table_name = 'StoreSettings'
                    ) THEN
                        CREATE TABLE StoreSettings (
                            Id INT NOT NULL AUTO_INCREMENT,
                            RestaurantName VARCHAR(200) NOT NULL DEFAULT '',
                            LogoUrl LONGTEXT NULL,
                            CustomerSplashUrl LONGTEXT NULL,
                            DriverSplashUrl LONGTEXT NULL,
                            SplashBackground1Url LONGTEXT NULL,
                            SplashBackground2Url LONGTEXT NULL,
                            PrimaryColorHex VARCHAR(16) NOT NULL DEFAULT '#D32F2F',
                            SecondaryColorHex VARCHAR(16) NOT NULL DEFAULT '#111827',
                            OffersColorHex VARCHAR(16) NOT NULL DEFAULT '#E11D48',
                            WelcomeText VARCHAR(200) NOT NULL DEFAULT '',
                            OnboardingJson LONGTEXT NULL,
                            HomeBannersJson LONGTEXT NULL,
                            WorkHours VARCHAR(64) NOT NULL DEFAULT '',
                            RestaurantLat DOUBLE NOT NULL DEFAULT 0,
                            RestaurantLng DOUBLE NOT NULL DEFAULT 0,
                            IsManuallyClosed TINYINT(1) NOT NULL DEFAULT 0,
                            ClosedMessage VARCHAR(250) NOT NULL DEFAULT '',
                            ClosedScreenImageUrl LONGTEXT NULL,
                            MinOrderAmount DECIMAL(65,30) NOT NULL DEFAULT 0,
                            DeliveryFeeType INT NOT NULL DEFAULT 0,
                            DeliveryFeeValue DECIMAL(65,30) NOT NULL DEFAULT 0,
                            DeliveryFeePerKm DECIMAL(65,30) NOT NULL DEFAULT 0,
                            SupportPhone VARCHAR(64) NOT NULL DEFAULT '',
                            SupportWhatsApp VARCHAR(64) NOT NULL DEFAULT '',
                            FacebookUrl VARCHAR(400) NULL,
                            InstagramUrl VARCHAR(400) NULL,
                            TelegramUrl VARCHAR(400) NULL,
                            IsAcceptingOrders TINYINT(1) NOT NULL DEFAULT 0,
                            RoutingProfile VARCHAR(16) NOT NULL DEFAULT 'driving',
                            UpdatedAtUtc DATETIME(6) NOT NULL DEFAULT '0001-01-01 00:00:00',
                            DriverSpeedBikeKmH DECIMAL(65,30) NOT NULL DEFAULT 18,
                            DriverSpeedCarKmH DECIMAL(65,30) NOT NULL DEFAULT 30,
                            PRIMARY KEY (Id)
                        ) CHARACTER SET utf8mb4;
                    END IF;
                END;
                CALL _MigrateRestaurantSettings();
                DROP PROCEDURE IF EXISTS _MigrateRestaurantSettings;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP PROCEDURE IF EXISTS _RevertStoreSettings;
                CREATE PROCEDURE _RevertStoreSettings()
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = DATABASE() AND table_name = 'StoreSettings'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = DATABASE() AND table_name = 'RestaurantSettings'
                    ) THEN
                        RENAME TABLE StoreSettings TO RestaurantSettings;
                    END IF;
                END;
                CALL _RevertStoreSettings();
                DROP PROCEDURE IF EXISTS _RevertStoreSettings;
            ");
        }
    }
}
