using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedHistory.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteEntryType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The sixth built-in, added after the app shipped with five. Guarded like
            // RenamePillTypeToMed: if a user already created their own type matching "Note"
            // case-insensitively, that row is left alone rather than inserting a duplicate.
            // Unlike the Pill->Med rename, no existing row is renamed here, so an exact-case
            // collision ("Note" already user-created) becomes the seeded row's stand-in and
            // behaves as the built-in; a differently-cased collision ("note", "NOTE") keeps
            // its own casing and — per EntryRules' ordinal name comparison, same as any other
            // stray-cased built-in-lookalike (see EntryRulesTests) — is treated as an ordinary
            // custom type: no required note, just note+photos+time.
            migrationBuilder.Sql(
                """
                INSERT INTO "EntryTypes" ("Name", "IsActive", "IsBuiltIn")
                SELECT 'Note', true, true
                WHERE NOT EXISTS (SELECT 1 FROM "EntryTypes" WHERE lower("Name")='note');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deleting the row on rollback is safe even though Entry.Type carries no FK:
            // entries already logged as Type='Note' are untouched (the column just stops
            // being backed by a row), and EntryRules would reject creating new ones until
            // the migration is re-applied. Only removes the row this migration would have
            // inserted — a pre-existing user-created "Note" (any casing) is never deleted,
            // matching the Up guard.
            migrationBuilder.Sql(
                """
                DELETE FROM "EntryTypes" WHERE "Name"='Note' AND "IsBuiltIn";
                """);
        }
    }
}
