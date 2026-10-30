using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuoteSnap.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMyFunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MyFunds",
                columns: table => new
                {
                    SchemeCode = table.Column<int>(type: "integer", nullable: false),
                    SchemeName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FundHouse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Option = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Category = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MyFunds", x => x.SchemeCode);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MyFunds");
        }
    }
}
