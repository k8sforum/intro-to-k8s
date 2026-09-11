using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mytravels.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddPointOfInterestCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "PointOfInterests",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "PointOfInterests");
        }
    }
}
