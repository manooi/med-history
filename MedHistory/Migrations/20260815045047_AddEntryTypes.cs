using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MedHistory.Migrations
{
    /// <inheritdoc />
    public partial class AddEntryTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntryTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryTypes", x => x.Id);
                });

            // Case-insensitive uniqueness: "cough" must not join the built-in "Cough".
            // Written as SQL because EF's model builder cannot express an index over an
            // expression, so this index is not in the model snapshot — later migrations
            // neither see nor drop it.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_EntryTypes_Name_Lower" ON "EntryTypes" (lower("Name"));
                """);

            // The five types the app shipped with, matching the values already stored in
            // Entries."Type" — that column keeps its data untouched, it merely stopped
            // being an enum in C#. Ids are left to the identity column rather than being
            // listed: assigning them explicitly would leave the sequence at 1 and make
            // the first type added from /types collide with a seeded row.
            migrationBuilder.Sql(
                """
                INSERT INTO "EntryTypes" ("Name", "IsActive", "IsBuiltIn")
                VALUES ('Symptom', true, true),
                       ('Bleeding', true, true),
                       ('Pill', true, true),
                       ('Cough', true, true),
                       ('Meal', true, true);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntryTypes");
        }
    }
}
