using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuoteSnap.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FyersSession",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    FyersId = table.Column<string>(type: "text", nullable: false),
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedSecretId = table.Column<string>(type: "text", nullable: false),
                    ProtectedPin = table.Column<string>(type: "text", nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "text", nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "text", nullable: false),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RefreshTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FyersSession", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuoteRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalSymbols = table.Column<int>(type: "integer", nullable: false),
                    SucceededCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuoteResults",
                columns: table => new
                {
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ltp = table.Column<decimal>(type: "numeric", nullable: true),
                    Change = table.Column<decimal>(type: "numeric", nullable: true),
                    ChangePercent = table.Column<decimal>(type: "numeric", nullable: true),
                    Open = table.Column<decimal>(type: "numeric", nullable: true),
                    High = table.Column<decimal>(type: "numeric", nullable: true),
                    Low = table.Column<decimal>(type: "numeric", nullable: true),
                    PreviousClose = table.Column<decimal>(type: "numeric", nullable: true),
                    Volume = table.Column<long>(type: "bigint", nullable: true),
                    ExchangeTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteResults", x => new { x.RequestId, x.Symbol });
                    table.ForeignKey(
                        name: "FK_QuoteResults_QuoteRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "QuoteRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRequests_CreatedAt",
                table: "QuoteRequests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRequests_Status",
                table: "QuoteRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FyersSession");

            migrationBuilder.DropTable(
                name: "QuoteResults");

            migrationBuilder.DropTable(
                name: "QuoteRequests");
        }
    }
}
