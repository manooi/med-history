using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MedHistory.Migrations
{
    /// <inheritdoc />
    public partial class AddMedAllocationSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MedAllocations is dropped and rebuilt rather than altered in place. The scaffolded
            // ALTER would have back-filled MealRelation, Method and Slots with the empty string,
            // which is not a member of any of those enums — every existing row would then throw
            // on read. There is no deployed data to protect (the checklist has never shipped),
            // and inventing a schedule for a row we know nothing about would be worse than
            // starting the table empty.
            migrationBuilder.DropTable(
                name: "MedAllocations");

            migrationBuilder.CreateTable(
                name: "MedAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Slots = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MealRelation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedAllocations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedAllocations_Day",
                table: "MedAllocations",
                column: "Day");

            // Entries are only added to: both columns are nullable, so every existing entry —
            // and every photo hanging off one — is untouched and simply reads as no tick.
            // ChecklistAllocationId carries no foreign key on purpose; see Entry.cs.
            migrationBuilder.AddColumn<int>(
                name: "ChecklistAllocationId",
                table: "Entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChecklistSlot",
                table: "Entries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChecklistAllocationId",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "ChecklistSlot",
                table: "Entries");

            migrationBuilder.DropTable(
                name: "MedAllocations");

            migrationBuilder.CreateTable(
                name: "MedAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequiredCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedAllocations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedAllocations_Day",
                table: "MedAllocations",
                column: "Day");
        }
    }
}
