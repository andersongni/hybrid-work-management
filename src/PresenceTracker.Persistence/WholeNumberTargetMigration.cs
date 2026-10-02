using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PresenceTracker.Persistence.Migrations;

[DbContext(typeof(TrackerDbContext))]
[Migration("202610010001_WholeNumberTarget")]
public sealed class WholeNumberTargetMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE Settings SET TargetPercent = CAST(ROUND(CAST(TargetPercent AS REAL), 0) AS TEXT);");
        migrationBuilder.Sql("UPDATE MonthlySnapshots SET TargetPercent = CAST(ROUND(CAST(TargetPercent AS REAL), 0) AS TEXT);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Metas antigas fracionadas não podem ser recuperadas após o arredondamento.
    }
}
