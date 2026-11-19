using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuoteSnap.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlackLastMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastMessageChannel",
                table: "SlackSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastMessageTs",
                table: "SlackSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastMessageChannel",
                table: "SlackSettings");

            migrationBuilder.DropColumn(
                name: "LastMessageTs",
                table: "SlackSettings");
        }
    }
}
