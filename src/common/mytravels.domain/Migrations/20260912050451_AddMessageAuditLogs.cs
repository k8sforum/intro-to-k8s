using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace mytravels.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PointOfInterestAuditLogs");

            migrationBuilder.CreateTable(
                name: "MessageAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExchangeName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PointOfInterestId = table.Column<int>(type: "integer", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAuditLogs_CorrelationId",
                table: "MessageAuditLogs",
                column: "CorrelationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageAuditLogs");

            migrationBuilder.CreateTable(
                name: "PointOfInterestAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PointOfInterestId = table.Column<int>(type: "integer", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Payload = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    QueueName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sucessful = table.Column<bool>(type: "boolean", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_PointOfInterestAuditLogs_PointOfInterestId",
                table: "PointOfInterestAuditLogs",
                column: "PointOfInterestId");
        }
    }
}
