using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StatusPage.Infrastructure.Migrations
{
    /// <summary>
    /// Rules the database enforces itself. Each one is also checked in C#: the domain check
    /// gives a caller a good error, and the constraint makes the rule true of the data even
    /// when a future code path, a migration script or a hand-typed UPDATE forgets to ask.
    /// </summary>
    public partial class Constraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Exactly one row per component may claim to be "the state right now". SQL Server
            // spells this as a filtered unique index; two open intervals would make every
            // current-state read ambiguous, and a checker that crashed mid-write is exactly
            // how you end up with them.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_component_intervals_one_open
                    ON component_intervals (ComponentId)
                    WHERE EndedAt IS NULL;
                """);

            // An interval that ends before it starts contributes a negative duration to every
            // sum it appears in, so the uptime figure comes out wrong rather than absent.
            migrationBuilder.Sql("""
                ALTER TABLE component_intervals
                    ADD CONSTRAINT ck_component_intervals_ends_after_start
                    CHECK (EndedAt IS NULL OR EndedAt >= StartedAt);
                """);

            // The domain refuses these at construction. The column refuses them too, because
            // the domain guard only runs on the path that goes through the domain.
            migrationBuilder.Sql("""
                ALTER TABLE components
                    ADD CONSTRAINT ck_components_thresholds_at_least_one
                    CHECK (FailuresToOpen >= 1 AND SuccessesToClose >= 1);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE components
                    ADD CONSTRAINT ck_components_latency_budget_not_negative
                    CHECK (DegradedAboveMs >= 0);
                """);

            // Postgres would write this as EXCLUDE USING gist (component_id WITH =,
            // tstzrange(started_at, ended_at) WITH &&). SQL Server has no exclusion
            // constraint, so the same rule is a trigger — and a trigger is only a rule if
            // something proves it fires, which is what the tests beside this migration do.
            //
            // Ranges are half-open: [StartedAt, EndedAt). Two intervals laid end to end share
            // the boundary instant and are not an overlap, which is why the comparisons are
            // strict on one side and not the other. An open interval runs to the end of time,
            // and COALESCE gives it that explicitly rather than relying on NULL semantics.
            migrationBuilder.Sql("""
                CREATE TRIGGER tr_component_intervals_no_overlap
                    ON component_intervals
                    AFTER INSERT, UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF EXISTS (
                        SELECT 1
                        FROM inserted AS i
                        JOIN component_intervals AS existing
                            ON existing.ComponentId = i.ComponentId
                           AND existing.Id <> i.Id
                        WHERE i.StartedAt < COALESCE(existing.EndedAt, '9999-12-31 23:59:59 +00:00')
                          AND existing.StartedAt < COALESCE(i.EndedAt, '9999-12-31 23:59:59 +00:00')
                    )
                    BEGIN
                        ROLLBACK TRANSACTION;
                        THROW 50001,
                            'A component''s intervals may not overlap.', 1;
                    END
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_component_intervals_no_overlap;");
            migrationBuilder.Sql("ALTER TABLE components DROP CONSTRAINT ck_components_latency_budget_not_negative;");
            migrationBuilder.Sql("ALTER TABLE components DROP CONSTRAINT ck_components_thresholds_at_least_one;");
            migrationBuilder.Sql("ALTER TABLE component_intervals DROP CONSTRAINT ck_component_intervals_ends_after_start;");
            migrationBuilder.Sql("DROP INDEX ux_component_intervals_one_open ON component_intervals;");
        }
    }
}
