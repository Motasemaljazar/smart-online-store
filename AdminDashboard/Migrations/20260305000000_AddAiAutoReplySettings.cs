using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdminDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAutoReplySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AiAutoReplyEnabled",
                table: "StoreSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "AiAutoReplySystemPrompt",
                table: "StoreSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiAutoReplyEnabled",
                table: "StoreSettings");

            migrationBuilder.DropColumn(
                name: "AiAutoReplySystemPrompt",
                table: "StoreSettings");
        }
    }
}
