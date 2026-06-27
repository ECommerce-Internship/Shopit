using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shopit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiVendorMarketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_SKU",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_StoreId",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId_SKU",
                table: "Products",
                columns: new[] { "StoreId", "SKU" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_StoreId_SKU",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SKU",
                table: "Products",
                column: "SKU",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId",
                table: "Products",
                column: "StoreId");
        }
    }
}
