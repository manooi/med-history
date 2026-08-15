using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedHistory.Migrations
{
    /// <inheritdoc />
    public partial class RenamePillTypeToMed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every logged dose moves to the new type name. Entry.Type carries no foreign
            // key, so this is the only place that needs to know the value changed.
            migrationBuilder.Sql(
                """
                UPDATE "Entries" SET "Type"='Med' WHERE "Type"='Pill';
                """);

            // Rename the seeded built-in row itself. Guarded by the case-insensitive unique
            // index on EntryTypes.Name (IX_EntryTypes_Name_Lower, added in AddEntryTypes and
            // never part of the EF model): if a user had already added their own type named
            // "Med" (any casing), renaming "Pill" to "Med" would collide. In that single-user
            // edge case the seeded row deliberately keeps its old name "Pill" rather than
            // erroring — the app still treats BuiltInEntryTypes.Med ("Med") as the special
            // type, so the entries renamed above display correctly either way; only the
            // /types page listing would show the built-in row still labelled "Pill".
            migrationBuilder.Sql(
                """
                UPDATE "EntryTypes" SET "Name"='Med'
                WHERE lower("Name")='pill'
                  AND NOT EXISTS (SELECT 1 FROM "EntryTypes" WHERE lower("Name")='med');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "EntryTypes" SET "Name"='Pill'
                WHERE lower("Name")='med'
                  AND NOT EXISTS (SELECT 1 FROM "EntryTypes" WHERE lower("Name")='pill');
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Entries" SET "Type"='Pill' WHERE "Type"='Med';
                """);
        }
    }
}
