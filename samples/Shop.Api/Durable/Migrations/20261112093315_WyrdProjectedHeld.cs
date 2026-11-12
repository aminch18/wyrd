using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Api.Durable.Migrations
{
    /// <inheritdoc />
    public partial class WyrdProjectedHeld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "held",
                schema: "wyrd",
                table: "projected",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "held",
                schema: "wyrd",
                table: "projected");
        }
    }
}
