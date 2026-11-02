using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuoteSnap.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMyFundPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "MyFunds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "MyFunds" f SET "Position" = r.n
                FROM (SELECT "SchemeCode", ROW_NUMBER() OVER (ORDER BY "AddedAt", "SchemeCode") - 1 AS n FROM "MyFunds") r
                WHERE f."SchemeCode" = r."SchemeCode";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Position",
                table: "MyFunds");
        }
    }
}
