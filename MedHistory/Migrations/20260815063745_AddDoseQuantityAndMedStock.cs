using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MedHistory.Migrations
{
    /// <inheritdoc />
    public partial class AddDoseQuantityAndMedStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Purely additive: two new columns and a new table. Nothing existing is dropped or
            // rewritten, so entries, photos, types and allocations already in the database come
            // through untouched — unlike AddMedAllocationSlots, which could rebuild its table
            // because there was no deployed data behind it.

            // One unit per slot, which is what every allocation planned before quantities
            // existed meant. The store default carries that onto them.
            migrationBuilder.AddColumn<decimal>(
                name: "DoseQuantity",
                table: "MedAllocations",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            // Nullable with no default, deliberately: an entry logged before quantities existed
            // recorded one unit, and null is how the app already says exactly that. Back-filling
            // it with 1 would claim the user had chosen an amount they were never asked for.
            migrationBuilder.AddColumn<decimal>(
                name: "DoseQuantity",
                table: "Entries",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MedStocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalCount = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedStocks", x => x.Id);
                });

            // Case-insensitive uniqueness: "panadol" must not become a second row beside
            // "Panadol" and split one medication's count in two. Written as SQL because EF's
            // model builder cannot express an index over an expression, so this index is not in
            // the model snapshot — later migrations neither see nor drop it. Same arrangement
            // as IX_EntryTypes_Name_Lower.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_MedStocks_Name_Lower" ON "MedStocks" (lower("Name"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedStocks");

            migrationBuilder.DropColumn(
                name: "DoseQuantity",
                table: "MedAllocations");

            migrationBuilder.DropColumn(
                name: "DoseQuantity",
                table: "Entries");
        }
    }
}
