using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace api.Grammar.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialGrammar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instruction = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ExerciseType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentTemplate = table.Column<string>(type: "text", nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: false),
                    CaseSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercises", x => x.Id);
                    table.CheckConstraint("CK_exercises_type", "\"ExerciseType\" IN ('fill_blank', 'multiple_choice')");
                });

            migrationBuilder.CreateTable(
                name: "grammar_topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Level = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grammar_topics", x => x.Id);
                    table.CheckConstraint("CK_grammar_topics_level", "\"Level\" IN ('A1', 'A2', 'B1', 'B2', 'C1', 'C2')");
                });

            migrationBuilder.CreateTable(
                name: "exercise_blanks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlankKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    InputType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Points = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_blanks", x => x.Id);
                    table.UniqueConstraint("AK_exercise_blanks_ExerciseId_Id", x => new { x.ExerciseId, x.Id });
                    table.CheckConstraint("CK_exercise_blanks_input_type", "\"InputType\" IN ('text', 'select')");
                    table.CheckConstraint("CK_exercise_blanks_points", "\"Points\" > 0");
                    table.ForeignKey(
                        name: "FK_exercise_blanks_exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exercise_questions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Prompt = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_questions", x => x.Id);
                    table.UniqueConstraint("AK_exercise_questions_ExerciseId_Id", x => new { x.ExerciseId, x.Id });
                    table.CheckConstraint("CK_exercise_questions_points", "\"Points\" > 0");
                    table.ForeignKey(
                        name: "FK_exercise_questions_exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exercise_topics",
                columns: table => new
                {
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_topics", x => new { x.ExerciseId, x.TopicId });
                    table.ForeignKey(
                        name: "FK_exercise_topics_exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exercise_topics_grammar_topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "grammar_topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grammar_theory_sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grammar_theory_sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_grammar_theory_sections_grammar_topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "grammar_topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exercise_blank_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BlankId = table.Column<Guid>(type: "uuid", nullable: false),
                    Answer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_blank_answers", x => x.Id);
                    table.CheckConstraint("CK_exercise_blank_answers_non_empty", "LENGTH(TRIM(\"Answer\")) > 0");
                    table.ForeignKey(
                        name: "FK_exercise_blank_answers_exercise_blanks_BlankId",
                        column: x => x.BlankId,
                        principalTable: "exercise_blanks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exercise_options",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    BlankId = table.Column<Guid>(type: "uuid", nullable: true),
                    OptionKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OptionText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_options", x => x.Id);
                    table.CheckConstraint("CK_exercise_options_single_parent", "(\"QuestionId\" IS NOT NULL AND \"BlankId\" IS NULL) OR (\"QuestionId\" IS NULL AND \"BlankId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_exercise_options_exercise_blanks_ExerciseId_BlankId",
                        columns: x => new { x.ExerciseId, x.BlankId },
                        principalTable: "exercise_blanks",
                        principalColumns: new[] { "ExerciseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exercise_options_exercise_questions_ExerciseId_QuestionId",
                        columns: x => new { x.ExerciseId, x.QuestionId },
                        principalTable: "exercise_questions",
                        principalColumns: new[] { "ExerciseId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exercise_options_exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "exercises",
                columns: new[] { "Id", "CaseSensitive", "ContentTemplate", "CreatedAt", "DisplayOrder", "ExerciseType", "Explanation", "Instruction", "IsActive", "Title", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e03"), false, "{{blank:1}} you a teacher?\n\nB: Yes, I {{blank:2}}.", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, "fill_blank", "A forma do verbo to be concorda com o sujeito.", "Escolha a forma correta do verbo to be.", true, "Complete com am, is ou are", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e04"), false, "She ___ a doctor.", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 2, "multiple_choice", "Use is com os pronomes he, she e it.", "Selecione a forma correta para completar a frase.", true, "Escolha a forma correta", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "grammar_topics",
                columns: new[] { "Id", "CreatedAt", "Description", "DisplayOrder", "IsActive", "Level", "Slug", "Title", "UpdatedAt" },
                values: new object[] { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e01"), new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Uso de am, is e are no presente simples.", 1, true, "A1", "present-simple-to-be", "Present Simple: verb to be", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.InsertData(
                table: "exercise_blanks",
                columns: new[] { "Id", "BlankKey", "DisplayOrder", "ExerciseId", "InputType", "Points" },
                values: new object[,]
                {
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e05"), "1", 1, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e03"), "select", 1m },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e06"), "2", 2, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e03"), "select", 1m }
                });

            migrationBuilder.InsertData(
                table: "exercise_questions",
                columns: new[] { "Id", "DisplayOrder", "ExerciseId", "Points", "Prompt", "QuestionKey" },
                values: new object[] { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e07"), 1, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e04"), 1m, "She ___ a doctor.", "1" });

            migrationBuilder.InsertData(
                table: "exercise_topics",
                columns: new[] { "ExerciseId", "TopicId" },
                values: new object[,]
                {
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e03"), new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e01") },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e04"), new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e01") }
                });

            migrationBuilder.InsertData(
                table: "grammar_theory_sections",
                columns: new[] { "Id", "ContentJson", "DisplayOrder", "Title", "TopicId" },
                values: new object[] { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e02"), "[{\"type\":\"paragraph\",\"text\":\"O verbo to be significa ser ou estar. No presente simples, sua forma depende do sujeito.\"},{\"type\":\"table\",\"headers\":[\"Pronome\",\"Verbo\",\"Exemplo\"],\"rows\":[[\"I\",\"am\",\"I am a student.\"],[\"You\",\"are\",\"You are a student.\"],[\"He / She / It\",\"is\",\"She is a doctor.\"],[\"We / They\",\"are\",\"We are friends.\"]]},{\"type\":\"list\",\"ordered\":false,\"items\":[\"Use am com I.\",\"Use is com he, she e it.\",\"Use are com you, we e they.\"]}]", 1, "Formas de am, is e are", new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e01") });

            migrationBuilder.InsertData(
                table: "exercise_blank_answers",
                columns: new[] { "Id", "Answer", "BlankId", "Explanation", "IsPrimary" },
                values: new object[,]
                {
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e08"), "Are", new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e05"), "Use are with the pronoun you.", true },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e09"), "am", new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e06"), "Use am with the pronoun I.", true }
                });

            migrationBuilder.InsertData(
                table: "exercise_options",
                columns: new[] { "Id", "BlankId", "DisplayOrder", "ExerciseId", "Explanation", "IsCorrect", "OptionKey", "OptionText", "QuestionId" },
                values: new object[,]
                {
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e10"), new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e05"), 1, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e03"), null, false, "am", "am", null },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e11"), new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e05"), 2, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e03"), null, false, "is", "is", null },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e12"), new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e05"), 3, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e03"), null, true, "are", "are", null },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e13"), new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e06"), 1, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e03"), null, true, "am", "am", null },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e14"), new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e06"), 2, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e03"), null, false, "is", "is", null },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e15"), new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e06"), 3, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e03"), null, false, "are", "are", null },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e16"), null, 1, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e04"), null, false, "A", "am", new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e07") },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e17"), null, 2, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e04"), "Use is with she.", true, "B", "is", new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e07") },
                    { new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e18"), null, 3, new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e04"), null, false, "C", "are", new Guid("b9a9e289-6e16-4f17-a144-81b12f2c5e07") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_blank_answers_BlankId",
                table: "exercise_blank_answers",
                column: "BlankId",
                unique: true,
                filter: "\"IsPrimary\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_blanks_ExerciseId_BlankKey",
                table: "exercise_blanks",
                columns: new[] { "ExerciseId", "BlankKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercise_blanks_ExerciseId_DisplayOrder",
                table: "exercise_blanks",
                columns: new[] { "ExerciseId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercise_options_ExerciseId_BlankId_DisplayOrder",
                table: "exercise_options",
                columns: new[] { "ExerciseId", "BlankId", "DisplayOrder" },
                unique: true,
                filter: "\"BlankId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_options_ExerciseId_BlankId_OptionKey",
                table: "exercise_options",
                columns: new[] { "ExerciseId", "BlankId", "OptionKey" },
                unique: true,
                filter: "\"BlankId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_options_ExerciseId_QuestionId_DisplayOrder",
                table: "exercise_options",
                columns: new[] { "ExerciseId", "QuestionId", "DisplayOrder" },
                unique: true,
                filter: "\"QuestionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_options_ExerciseId_QuestionId_OptionKey",
                table: "exercise_options",
                columns: new[] { "ExerciseId", "QuestionId", "OptionKey" },
                unique: true,
                filter: "\"QuestionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_questions_ExerciseId_DisplayOrder",
                table: "exercise_questions",
                columns: new[] { "ExerciseId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercise_questions_ExerciseId_QuestionKey",
                table: "exercise_questions",
                columns: new[] { "ExerciseId", "QuestionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercise_topics_TopicId",
                table: "exercise_topics",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_exercises_IsActive_DisplayOrder",
                table: "exercises",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_grammar_theory_sections_TopicId_DisplayOrder",
                table: "grammar_theory_sections",
                columns: new[] { "TopicId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_grammar_topics_IsActive_DisplayOrder",
                table: "grammar_topics",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_grammar_topics_Slug",
                table: "grammar_topics",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_blank_answers");

            migrationBuilder.DropTable(
                name: "exercise_options");

            migrationBuilder.DropTable(
                name: "exercise_topics");

            migrationBuilder.DropTable(
                name: "grammar_theory_sections");

            migrationBuilder.DropTable(
                name: "exercise_blanks");

            migrationBuilder.DropTable(
                name: "exercise_questions");

            migrationBuilder.DropTable(
                name: "grammar_topics");

            migrationBuilder.DropTable(
                name: "exercises");
        }
    }
}
