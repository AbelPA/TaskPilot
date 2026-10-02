using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.AudioExtractions.Persistence.Migrations;

public partial class AddOutboxTraceContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TraceParent",
            table: "audio_extraction_outbox",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TraceState",
            table: "audio_extraction_outbox",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TraceState", table: "audio_extraction_outbox");
        migrationBuilder.DropColumn(name: "TraceParent", table: "audio_extraction_outbox");
    }
}
