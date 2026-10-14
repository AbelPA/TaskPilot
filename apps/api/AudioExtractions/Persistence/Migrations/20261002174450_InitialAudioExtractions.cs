using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.AudioExtractions.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudioExtractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audio_extraction_processed_events",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_extraction_processed_events", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "audio_extraction_requests",
                columns: table => new
                {
                    RequestId = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    VideoId = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    StartSeconds = table.Column<int>(type: "integer", nullable: false),
                    EndSeconds = table.Column<int>(type: "integer", nullable: false),
                    OutputFormat = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    ResultObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ResultSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ResultChecksumSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResultDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IdempotencyRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_extraction_requests", x => x.RequestId);
                    table.CheckConstraint("CK_audio_extraction_requests_idempotency_hashes", "(\"IdempotencyKeyHash\" IS NULL) = (\"IdempotencyRequestHash\" IS NULL)");
                    table.CheckConstraint("CK_audio_extraction_requests_interval", "\"StartSeconds\" >= 0 AND \"EndSeconds\" > \"StartSeconds\"");
                    table.CheckConstraint("CK_audio_extraction_requests_interval_limit", "\"EndSeconds\" - \"StartSeconds\" <= 1800 AND \"EndSeconds\" <= \"SourceDurationSeconds\"");
                    table.CheckConstraint("CK_audio_extraction_requests_output_format", "\"OutputFormat\" = 'mp3'");
                    table.CheckConstraint("CK_audio_extraction_requests_source_duration", "\"SourceDurationSeconds\" > 0 AND \"SourceDurationSeconds\" <= 21600");
                    table.CheckConstraint("CK_audio_extraction_requests_status", "\"Status\" IN ('accepted', 'completed', 'failed')");
                    table.CheckConstraint("CK_audio_extraction_requests_terminal_fields", "(\"Status\" = 'accepted' AND \"ExpiresAt\" IS NULL AND \"ResultObjectKey\" IS NULL AND \"ResultSizeBytes\" IS NULL AND \"ResultChecksumSha256\" IS NULL AND \"ResultDurationSeconds\" IS NULL AND \"FailureCode\" IS NULL AND \"FailureMessage\" IS NULL) OR (\"Status\" = 'completed' AND \"ExpiresAt\" IS NOT NULL AND \"ResultObjectKey\" IS NOT NULL AND \"ResultSizeBytes\" BETWEEN 1 AND 104857600 AND LENGTH(\"ResultChecksumSha256\") = 64 AND \"ResultDurationSeconds\" > 0 AND ABS(\"ResultDurationSeconds\" - (\"EndSeconds\" - \"StartSeconds\")) <= 2 AND \"FailureCode\" IS NULL AND \"FailureMessage\" IS NULL) OR (\"Status\" = 'failed' AND \"ExpiresAt\" IS NOT NULL AND \"FailureCode\" IS NOT NULL AND \"FailureMessage\" IS NOT NULL AND \"ResultObjectKey\" IS NULL AND \"ResultSizeBytes\" IS NULL AND \"ResultChecksumSha256\" IS NULL AND \"ResultDurationSeconds\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "audio_extraction_notification_outcomes",
                columns: table => new
                {
                    RequestId = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SafeMessage = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_extraction_notification_outcomes", x => x.RequestId);
                    table.CheckConstraint("CK_audio_extraction_notification_status", "\"Status\" IN ('completed', 'failed')");
                    table.ForeignKey(
                        name: "FK_audio_extraction_notification_outcomes_audio_extraction_req~",
                        column: x => x.RequestId,
                        principalTable: "audio_extraction_requests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audio_extraction_outbox",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<string>(type: "character varying(43)", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_extraction_outbox", x => x.EventId);
                    table.CheckConstraint("CK_audio_extraction_outbox_attempt_count", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_audio_extraction_outbox_published_at", "(\"State\" = 'published' AND \"PublishedAt\" IS NOT NULL) OR (\"State\" = 'pending' AND \"PublishedAt\" IS NULL)");
                    table.CheckConstraint("CK_audio_extraction_outbox_state", "\"State\" IN ('pending', 'published')");
                    table.ForeignKey(
                        name: "FK_audio_extraction_outbox_audio_extraction_requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "audio_extraction_requests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audio_extraction_notification_outcomes_EventId",
                table: "audio_extraction_notification_outcomes",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audio_extraction_outbox_RequestId",
                table: "audio_extraction_outbox",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_audio_extraction_outbox_state_next_attempt",
                table: "audio_extraction_outbox",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audio_extraction_requests_idempotency_key_hash",
                table: "audio_extraction_requests",
                column: "IdempotencyKeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audio_extraction_notification_outcomes");

            migrationBuilder.DropTable(
                name: "audio_extraction_outbox");

            migrationBuilder.DropTable(
                name: "audio_extraction_processed_events");

            migrationBuilder.DropTable(
                name: "audio_extraction_requests");
        }
    }
}
