using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StatusPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CheckerState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "checker_state",
                columns: table => new
                {
                    ComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Candidate = table.Column<int>(type: "int", nullable: false),
                    ConsecutiveObservations = table.Column<int>(type: "int", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastLatencyMs = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checker_state", x => x.ComponentId);
                    table.ForeignKey(
                        name: "FK_checker_state_components_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "components",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checker_state");
        }
    }
}
