using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Franchise.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestaurantOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AcceptingOrders",
                table: "restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "DeliveryFeeCents",
                table: "restaurants",
                type: "bigint",
                nullable: false,
                defaultValue: 299L);

            migrationBuilder.AddColumn<double>(
                name: "DeliveryRadiusKm",
                table: "restaurants",
                type: "double precision",
                nullable: false,
                defaultValue: 5.0);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "restaurants",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "restaurants",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MinimumOrderCents",
                table: "restaurants",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "OpeningHours",
                table: "restaurants",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "restaurants",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PrepTimeMinutes",
                table: "restaurants",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<int>(
                name: "SeatingCapacity",
                table: "restaurants",
                type: "integer",
                nullable: false,
                defaultValue: 40);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "restaurants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Europe/Paris");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptingOrders",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "DeliveryFeeCents",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "DeliveryRadiusKm",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "MinimumOrderCents",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "OpeningHours",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "PrepTimeMinutes",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "SeatingCapacity",
                table: "restaurants");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "restaurants");
        }
    }
}
