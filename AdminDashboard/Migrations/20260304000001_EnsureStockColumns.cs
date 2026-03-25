using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminDashboard.Migrations
{
    /// <inheritdoc />
    public partial class EnsureStockColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // StockQuantity and TrackStock already exist from AddStoreFeatures migration
            // This migration adds an index to improve query performance for out-of-stock filtering
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_Products_TrackStock_StockQuantity 
                ON Products (TrackStock, StockQuantity) 
                WHERE TrackStock = 1;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS IX_Products_TrackStock_StockQuantity ON Products;");
        }
    }
}
