using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedHistory.Migrations
{
    /// <inheritdoc />
    public partial class AddStockLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Purely additive, like AddDoseQuantityAndMedStock before it: two nullable columns
            // and two UPDATEs that only ever write those columns. Nothing existing is dropped or
            // rewritten, which matters because there is a deployed database with real history
            // behind this.
            //
            // Both columns are plain integers with no foreign key, deliberately — see the
            // comments on Entry.MedStockId and MedAllocation.MedStockId. A stock row may be
            // removed while doses that came out of it remain, and those doses must survive.

            migrationBuilder.AddColumn<int>(
                name: "MedStockId",
                table: "MedAllocations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MedStockId",
                table: "Entries",
                type: "integer",
                nullable: true);

            // Both back-fills match on lower(btrim(name)), which is exactly the rule the app
            // matches medication names by — ChecklistRules.NamesMatch, trimmed and
            // case-insensitive. Anything matching nothing is left null, which the app reads as
            // "not stocked" and never as an error.

            // The plan links to the stock it names. This is what the app would have resolved had
            // the allocation been written today.
            migrationBuilder.Sql(
                """
                UPDATE "MedAllocations" a
                SET "MedStockId" = s."Id"
                FROM "MedStocks" s
                WHERE lower(btrim(a."Name")) = lower(btrim(s."Name"));
                """);

            // Every Pill entry with a name that matches something stocked, ticked or typed in
            // alike — not only the ones a checklist tick created.
            //
            // That looks broader than it needs to be, and it is on purpose: before this column
            // existed, consumption was counted by name over exactly this set of entries, so
            // stamping the id onto all of them freezes today's count as it currently reads. Any
            // narrower back-fill would drop doses out of their stock the moment this deploys,
            // which is the very jump this change exists to prevent. Hand-typed doses gain a link
            // they would not get today; that only makes them survive a later rename too, which is
            // no worse for the user than the alternative.
            migrationBuilder.Sql(
                """
                UPDATE "Entries" e
                SET "MedStockId" = s."Id"
                FROM "MedStocks" s
                WHERE e."Type" = 'Pill'
                  AND e."PillName" IS NOT NULL
                  AND lower(btrim(e."PillName")) = lower(btrim(s."Name"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the columns discards the links, which is the whole of what was added —
            // the previous schema counted consumption by name and would go on doing so.
            migrationBuilder.DropColumn(
                name: "MedStockId",
                table: "MedAllocations");

            migrationBuilder.DropColumn(
                name: "MedStockId",
                table: "Entries");
        }
    }
}
