using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuoteSnap.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMutualFundNavs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MutualFundNavs",
                columns: table => new
                {
                    SchemeCode = table.Column<int>(type: "integer", nullable: false),
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    IsinReinvestment = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    SchemeName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FundHouse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Category = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Plan = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Option = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Nav = table.Column<decimal>(type: "numeric", nullable: false),
                    NavDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LoadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MutualFundNavs", x => x.SchemeCode);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MutualFundNavs_Isin",
                table: "MutualFundNavs",
                column: "Isin");

            migrationBuilder.CreateIndex(
                name: "IX_MutualFundNavs_IsinReinvestment",
                table: "MutualFundNavs",
                column: "IsinReinvestment");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MutualFundNavs");
        }
    }
}
