using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IndirimTakip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NutritionLabelImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NutritionLabelImageUrl",
                table: "Products",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NutritionLabelReadUrl",
                table: "Products",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NutritionLabelStatus",
                table: "Products",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NutritionLabelImageUrl",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "NutritionLabelReadUrl",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "NutritionLabelStatus",
                table: "Products");
        }
    }
}
