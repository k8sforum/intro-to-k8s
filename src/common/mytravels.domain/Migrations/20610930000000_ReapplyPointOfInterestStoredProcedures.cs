using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace mytravels.domain.Migrations
{
    /// <inheritdoc />
    public partial class ReapplyPointOfInterestStoredProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // spGetPointOfInterest and spGetPointOfInterestByTagName ranked rows after
            // joining tags, which collapsed every point down to a single tag. Databases
            // that already ran UpdateStoredProcedure need the corrected scripts replayed.
            string baseDir = AppContext.BaseDirectory;
            var scriptsDir = Path.Combine(baseDir, "Features");
            string[] files = Directory.GetFiles(scriptsDir, "*.sql", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                string script = File.ReadAllText(file);
                migrationBuilder.Sql(script);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException();
        }
    }
}
