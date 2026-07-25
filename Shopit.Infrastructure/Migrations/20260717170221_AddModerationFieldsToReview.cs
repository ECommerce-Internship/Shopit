using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shopit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationFieldsToReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ModeratedAt",
                table: "Reviews",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationReason",
                table: "Reviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ModerationScore",
                table: "Reviews",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Reviews",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModeratedAt",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ModerationReason",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ModerationScore",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Reviews");
        }
    }
}
