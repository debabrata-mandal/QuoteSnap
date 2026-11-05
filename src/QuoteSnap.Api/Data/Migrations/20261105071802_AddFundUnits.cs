using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuoteSnap.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFundUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Units",
                table: "MyFunds",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Units",
                table: "MyFunds");
        }
    }
}
