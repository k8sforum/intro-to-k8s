using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace mytravels.domain.Migrations
{
    /// <inheritdoc />
    public partial class RemovePointOfInterestLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PointOfInterests_PointOfInterestStatuses_PointOfInterestSta~",
                table: "PointOfInterests");

            migrationBuilder.DropForeignKey(
                name: "FK_PointOfInterests_PointOfInterestTypes_PointOfInterestTypeId",
                table: "PointOfInterests");

            migrationBuilder.DropTable(
                name: "PointOfInterestStatuses",
                schema: "lookups");

            migrationBuilder.DropTable(
                name: "PointOfInterestTypes",
                schema: "lookups");

            migrationBuilder.DropIndex(
                name: "IX_PointOfInterests_PointOfInterestStatusId",
                table: "PointOfInterests");

            migrationBuilder.DropIndex(
                name: "IX_PointOfInterests_PointOfInterestTypeId",
                table: "PointOfInterests");

            migrationBuilder.DropColumn(
                name: "PointOfInterestStatusId",
                table: "PointOfInterests");

            migrationBuilder.DropColumn(
                name: "PointOfInterestTypeId",
                table: "PointOfInterests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "lookups");

            migrationBuilder.AddColumn<int>(
                name: "PointOfInterestStatusId",
                table: "PointOfInterests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PointOfInterestTypeId",
                table: "PointOfInterests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PointOfInterestStatuses",
                schema: "lookups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PrimaryColor = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SecondaryColor = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointOfInterestStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PointOfInterestTypes",
                schema: "lookups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PrimaryColor = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SecondaryColor = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointOfInterestTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PointOfInterests_PointOfInterestStatusId",
                table: "PointOfInterests",
                column: "PointOfInterestStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_PointOfInterests_PointOfInterestTypeId",
                table: "PointOfInterests",
                column: "PointOfInterestTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_PointOfInterests_PointOfInterestStatuses_PointOfInterestSta~",
                table: "PointOfInterests",
                column: "PointOfInterestStatusId",
                principalSchema: "lookups",
                principalTable: "PointOfInterestStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PointOfInterests_PointOfInterestTypes_PointOfInterestTypeId",
                table: "PointOfInterests",
                column: "PointOfInterestTypeId",
                principalSchema: "lookups",
                principalTable: "PointOfInterestTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
