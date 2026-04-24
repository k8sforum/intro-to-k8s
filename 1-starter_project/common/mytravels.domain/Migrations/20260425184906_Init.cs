using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace mytravels.domain.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "lookups");

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

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PointOfInterests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PointOfInterestKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Container = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    GeneratedBlobName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FormattedAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ImageResized = table.Column<bool>(type: "boolean", nullable: false),
                    PointOfInterestTypeId = table.Column<int>(type: "integer", nullable: false),
                    PointOfInterestStatusId = table.Column<int>(type: "integer", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointOfInterests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PointOfInterests_PointOfInterestStatuses_PointOfInterestSta~",
                        column: x => x.PointOfInterestStatusId,
                        principalSchema: "lookups",
                        principalTable: "PointOfInterestStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PointOfInterests_PointOfInterestTypes_PointOfInterestTypeId",
                        column: x => x.PointOfInterestTypeId,
                        principalSchema: "lookups",
                        principalTable: "PointOfInterestTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PointOfInterestAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QueueName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Payload = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Sucessful = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PointOfInterestId = table.Column<int>(type: "integer", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointOfInterestAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PointOfInterestAuditLogs_PointOfInterests_PointOfInterestId",
                        column: x => x.PointOfInterestId,
                        principalTable: "PointOfInterests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PointOfInterestTagAssociations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PointOfInterestId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointOfInterestTagAssociations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PointOfInterestTagAssociations_PointOfInterests_PointOfInte~",
                        column: x => x.PointOfInterestId,
                        principalTable: "PointOfInterests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PointOfInterestTagAssociations_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PointOfInterestAuditLogs_PointOfInterestId",
                table: "PointOfInterestAuditLogs",
                column: "PointOfInterestId");

            migrationBuilder.CreateIndex(
                name: "IX_PointOfInterests_PointOfInterestStatusId",
                table: "PointOfInterests",
                column: "PointOfInterestStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_PointOfInterests_PointOfInterestTypeId",
                table: "PointOfInterests",
                column: "PointOfInterestTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PointOfInterestTagAssociations_PointOfInterestId",
                table: "PointOfInterestTagAssociations",
                column: "PointOfInterestId");

            migrationBuilder.CreateIndex(
                name: "IX_PointOfInterestTagAssociations_TagId",
                table: "PointOfInterestTagAssociations",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PointOfInterestAuditLogs");

            migrationBuilder.DropTable(
                name: "PointOfInterestTagAssociations");

            migrationBuilder.DropTable(
                name: "PointOfInterests");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "PointOfInterestStatuses",
                schema: "lookups");

            migrationBuilder.DropTable(
                name: "PointOfInterestTypes",
                schema: "lookups");
        }
    }
}
