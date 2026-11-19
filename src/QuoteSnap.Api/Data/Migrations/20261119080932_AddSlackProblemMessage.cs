using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuoteSnap.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlackProblemMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProblemChannel",
                table: "SlackSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProblemKey",
                table: "SlackSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProblemTs",
                table: "SlackSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProblemChannel",
                table: "SlackSettings");

            migrationBuilder.DropColumn(
                name: "ProblemKey",
                table: "SlackSettings");

            migrationBuilder.DropColumn(
                name: "ProblemTs",
                table: "SlackSettings");
        }
    }
}
