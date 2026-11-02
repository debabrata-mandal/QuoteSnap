using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuoteSnap.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteRequestForHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ForHoldings",
                table: "QuoteRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRequests_ForHoldings_CreatedAt",
                table: "QuoteRequests",
                columns: new[] { "ForHoldings", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuoteRequests_ForHoldings_CreatedAt",
                table: "QuoteRequests");

            migrationBuilder.DropColumn(
                name: "ForHoldings",
                table: "QuoteRequests");
        }
    }
}
