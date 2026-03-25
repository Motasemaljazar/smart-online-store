using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminDashboard.Migrations
{
    
    public partial class RenameRestaurantColumnsToStore : Migration
    {
        
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            
            migrationBuilder.RenameColumn(table: "StoreSettings", name: "RestaurantName", newName: "StoreName");
            migrationBuilder.RenameColumn(table: "StoreSettings", name: "RestaurantLat",  newName: "StoreLat");
            migrationBuilder.RenameColumn(table: "StoreSettings", name: "RestaurantLng",  newName: "StoreLng");

            migrationBuilder.Sql(@"
                ALTER TABLE ""Ratings"" RENAME COLUMN ""RestaurantStars"" TO ""StoreStars"";
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE ""Ratings"" RENAME COLUMN ""RestaurantComment"" TO ""StoreComment"";
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(table: "StoreSettings", name: "StoreName", newName: "RestaurantName");
            migrationBuilder.RenameColumn(table: "StoreSettings", name: "StoreLat",  newName: "RestaurantLat");
            migrationBuilder.RenameColumn(table: "StoreSettings", name: "StoreLng",  newName: "RestaurantLng");

            migrationBuilder.Sql(@"ALTER TABLE ""Ratings"" RENAME COLUMN ""StoreStars""   TO ""RestaurantStars"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Ratings"" RENAME COLUMN ""StoreComment"" TO ""RestaurantComment"";");
        }
    }
}
