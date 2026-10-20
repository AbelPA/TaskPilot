using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Grammar.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningStatisticsReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "grammar_review_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SelectionSeed = table.Column<int>(type: "integer", nullable: false),
                    RequestedExerciseCount = table.Column<int>(type: "integer", nullable: false),
                    AvailableExerciseCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grammar_review_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "grammar_review_exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectionCategory = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PriorityScore = table.Column<decimal>(type: "numeric(6,5)", precision: 6, scale: 5, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    TitleSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TopicTitleSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grammar_review_exercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_grammar_review_exercises_grammar_review_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "grammar_review_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grammar_submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseTitleSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicTitleSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReviewSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    ExerciseExplanation = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grammar_submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_grammar_submissions_grammar_review_sessions_ReviewSessionId",
                        column: x => x.ReviewSessionId,
                        principalTable: "grammar_review_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grammar_evaluated_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SubmittedAnswer = table.Column<string>(type: "text", nullable: false),
                    CorrectAnswer = table.Column<string>(type: "text", nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: false),
                    Points = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    MaxPoints = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grammar_evaluated_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_grammar_evaluated_answers_grammar_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "grammar_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_grammar_evaluated_answers_SubmissionId",
                table: "grammar_evaluated_answers",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_grammar_evaluated_answers_SubmissionId_ItemType_ItemKey",
                table: "grammar_evaluated_answers",
                columns: new[] { "SubmissionId", "ItemType", "ItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grammar_review_exercises_SessionId_DisplayOrder",
                table: "grammar_review_exercises",
                columns: new[] { "SessionId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grammar_review_exercises_SessionId_ExerciseId",
                table: "grammar_review_exercises",
                columns: new[] { "SessionId", "ExerciseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grammar_review_sessions_CreatedAt",
                table: "grammar_review_sessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_grammar_submissions_ExerciseId_TopicId_AttemptNumber",
                table: "grammar_submissions",
                columns: new[] { "ExerciseId", "TopicId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grammar_submissions_ReviewSessionId",
                table: "grammar_submissions",
                column: "ReviewSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_grammar_submissions_TopicId_EvaluatedAt",
                table: "grammar_submissions",
                columns: new[] { "TopicId", "EvaluatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grammar_evaluated_answers");

            migrationBuilder.DropTable(
                name: "grammar_review_exercises");

            migrationBuilder.DropTable(
                name: "grammar_submissions");

            migrationBuilder.DropTable(
                name: "grammar_review_sessions");
        }
    }
}
