using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.AudioExtractions.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxTraceContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "audio_extraction_outbox",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceState",
                table: "audio_extraction_outbox",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "audio_extraction_outbox");

            migrationBuilder.DropColumn(
                name: "TraceState",
                table: "audio_extraction_outbox");
        }
    }
}
