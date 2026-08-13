using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StatusPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IncidentsAndMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Impact = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OpenedAutomatically = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_windows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_windows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "incident_components",
                columns: table => new
                {
                    AffectedComponentsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_components", x => new { x.AffectedComponentsId, x.IncidentsId });
                    table.ForeignKey(
                        name: "FK_incident_components_components_AffectedComponentsId",
                        column: x => x.AffectedComponentsId,
                        principalTable: "components",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_incident_components_incidents_IncidentsId",
                        column: x => x.IncidentsId,
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incident_updates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedByOperatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedByDisplayName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_updates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_incident_updates_incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_components",
                columns: table => new
                {
                    AffectedComponentsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaintenanceWindowsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_components", x => new { x.AffectedComponentsId, x.MaintenanceWindowsId });
                    table.ForeignKey(
                        name: "FK_maintenance_components_components_AffectedComponentsId",
                        column: x => x.AffectedComponentsId,
                        principalTable: "components",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_components_maintenance_windows_MaintenanceWindowsId",
                        column: x => x.MaintenanceWindowsId,
                        principalTable: "maintenance_windows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incident_components_IncidentsId",
                table: "incident_components",
                column: "IncidentsId");

            migrationBuilder.CreateIndex(
                name: "ix_incident_updates_incident_posted",
                table: "incident_updates",
                columns: new[] { "IncidentId", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_incidents_started",
                table: "incidents",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_components_MaintenanceWindowsId",
                table: "maintenance_components",
                column: "MaintenanceWindowsId");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_windows_span",
                table: "maintenance_windows",
                columns: new[] { "StartsAt", "EndsAt" });

            // Resolved and ResolvedAt say the same thing, so they must never disagree.
            // Status 3 is Resolved. Without this, a query filtering on the enum and one
            // filtering on the date return different incidents, and both look correct.
            migrationBuilder.Sql("""
                ALTER TABLE incidents
                    ADD CONSTRAINT ck_incidents_resolved_has_date
                    CHECK ((Status = 3 AND ResolvedAt IS NOT NULL)
                        OR (Status <> 3 AND ResolvedAt IS NULL));
                """);

            // A window ending before it starts would subtract a negative duration from the
            // denominator, which inflates availability rather than failing.
            migrationBuilder.Sql("""
                ALTER TABLE maintenance_windows
                    ADD CONSTRAINT ck_maintenance_windows_ends_after_start
                    CHECK (EndsAt > StartsAt);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE maintenance_windows DROP CONSTRAINT ck_maintenance_windows_ends_after_start;");
            migrationBuilder.Sql("ALTER TABLE incidents DROP CONSTRAINT ck_incidents_resolved_has_date;");

            migrationBuilder.DropTable(
                name: "incident_components");

            migrationBuilder.DropTable(
                name: "incident_updates");

            migrationBuilder.DropTable(
                name: "maintenance_components");

            migrationBuilder.DropTable(
                name: "incidents");

            migrationBuilder.DropTable(
                name: "maintenance_windows");
        }
    }
}
