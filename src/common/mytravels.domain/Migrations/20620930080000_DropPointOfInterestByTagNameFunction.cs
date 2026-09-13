using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mytravels.domain.Migrations
{
    /// <summary>
    /// Drops spGetPointOfInterestByTagName now that SOLR serves the tag filter. The .sql file is gone, so a
    /// fresh database never creates the function; this removes it from databases that already have it.
    /// Deliberately dated after AddCorrelationIdToPointOfInterestFunctions, which replays every .sql file.
    /// </summary>
    public partial class DropPointOfInterestByTagNameFunction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.spGetPointOfInterestByTagName(character varying);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException();
        }
    }
}
