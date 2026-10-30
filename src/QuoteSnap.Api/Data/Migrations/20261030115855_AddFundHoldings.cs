using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QuoteSnap.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFundHoldings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HoldingsFetchedAt",
                table: "MyFunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScanxPlanId",
                table: "MyFunds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanxScheme",
                table: "MyFunds",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FundHoldings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchemeCode = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    AssetType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    Sector = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    Shares = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundHoldings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FundHoldings_MyFunds_SchemeCode",
                        column: x => x.SchemeCode,
                        principalTable: "MyFunds",
                        principalColumn: "SchemeCode",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HoldingFetches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoldingFetches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HoldingFetchFunds",
                columns: table => new
                {
                    FetchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemeCode = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    SchemeName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScanxScheme = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    HoldingCount = table.Column<int>(type: "integer", nullable: true),
                    Month = table.Column<DateOnly>(type: "date", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoldingFetchFunds", x => new { x.FetchId, x.SchemeCode });
                    table.ForeignKey(
                        name: "FK_HoldingFetchFunds_HoldingFetches_FetchId",
                        column: x => x.FetchId,
                        principalTable: "HoldingFetches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundHoldings_Isin",
                table: "FundHoldings",
                column: "Isin");

            migrationBuilder.CreateIndex(
                name: "IX_FundHoldings_SchemeCode_Month",
                table: "FundHoldings",
                columns: new[] { "SchemeCode", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_HoldingFetches_CreatedAt",
                table: "HoldingFetches",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundHoldings");

            migrationBuilder.DropTable(
                name: "HoldingFetchFunds");

            migrationBuilder.DropTable(
                name: "HoldingFetches");

            migrationBuilder.DropColumn(
                name: "HoldingsFetchedAt",
                table: "MyFunds");

            migrationBuilder.DropColumn(
                name: "ScanxPlanId",
                table: "MyFunds");

            migrationBuilder.DropColumn(
                name: "ScanxScheme",
                table: "MyFunds");
        }
    }
}
