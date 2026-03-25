using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminDashboard.Migrations
{

    public partial class RenameRestaurantRateToStoreRate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RestaurantRate",
                table: "OrderRatings",
                newName: "StoreRate");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StoreRate",
                table: "OrderRatings",
                newName: "RestaurantRate");
        }
    }
}
