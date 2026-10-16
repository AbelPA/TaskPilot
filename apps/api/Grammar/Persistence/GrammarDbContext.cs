using Api.Grammar.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Grammar.Persistence;

public sealed class GrammarDbContext(DbContextOptions<GrammarDbContext> options) : DbContext(options)
{
    public DbSet<GrammarTopicEntity> Topics => Set<GrammarTopicEntity>();
    public DbSet<GrammarTheorySectionEntity> TheorySections => Set<GrammarTheorySectionEntity>();
    public DbSet<ExerciseEntity> Exercises => Set<ExerciseEntity>();
    public DbSet<ExerciseTopicEntity> ExerciseTopics => Set<ExerciseTopicEntity>();
    public DbSet<ExerciseBlankEntity> Blanks => Set<ExerciseBlankEntity>();
    public DbSet<ExerciseBlankAnswerEntity> BlankAnswers => Set<ExerciseBlankAnswerEntity>();
    public DbSet<ExerciseQuestionEntity> Questions => Set<ExerciseQuestionEntity>();
    public DbSet<ExerciseOptionEntity> Options => Set<ExerciseOptionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var topics = modelBuilder.Entity<GrammarTopicEntity>();
        topics.ToTable("grammar_topics", table =>
            table.HasCheckConstraint(
                "CK_grammar_topics_level",
                "\"Level\" IN ('A1', 'A2', 'B1', 'B2', 'C1', 'C2')"));
        topics.HasKey(topic => topic.Id);
        topics.Property(topic => topic.Slug).HasMaxLength(120).IsRequired();
        topics.Property(topic => topic.Title).HasMaxLength(200).IsRequired();
        topics.Property(topic => topic.Description).HasMaxLength(1_000).IsRequired();
        topics.Property(topic => topic.Level).HasMaxLength(2).IsRequired();
        topics.HasIndex(topic => topic.Slug).IsUnique();
        topics.HasIndex(topic => new { topic.IsActive, topic.DisplayOrder });

        var theory = modelBuilder.Entity<GrammarTheorySectionEntity>();
        theory.ToTable("grammar_theory_sections");
        theory.HasKey(section => section.Id);
        theory.Property(section => section.Title).HasMaxLength(200).IsRequired();
        theory.Property(section => section.ContentJson).HasColumnType("jsonb").IsRequired();
        theory.HasIndex(section => new { section.TopicId, section.DisplayOrder }).IsUnique();
        theory.HasOne(section => section.Topic)
            .WithMany(topic => topic.TheorySections)
            .HasForeignKey(section => section.TopicId)
            .OnDelete(DeleteBehavior.Restrict);

        var exercises = modelBuilder.Entity<ExerciseEntity>();
        exercises.ToTable("exercises", table =>
        {
            table.HasCheckConstraint(
                "CK_exercises_type",
                "\"ExerciseType\" IN ('fill_blank', 'multiple_choice')");
        });
        exercises.HasKey(exercise => exercise.Id);
        exercises.Property(exercise => exercise.Title).HasMaxLength(200).IsRequired();
        exercises.Property(exercise => exercise.Instruction).HasMaxLength(1_000).IsRequired();
        exercises.Property(exercise => exercise.ExerciseType).HasMaxLength(32).IsRequired();
        exercises.Property(exercise => exercise.ContentTemplate).IsRequired();
        exercises.Property(exercise => exercise.Explanation).IsRequired();
        exercises.HasIndex(exercise => new { exercise.IsActive, exercise.DisplayOrder });

        var exerciseTopics = modelBuilder.Entity<ExerciseTopicEntity>();
        exerciseTopics.ToTable("exercise_topics");
        exerciseTopics.HasKey(link => new { link.ExerciseId, link.TopicId });
        exerciseTopics.HasIndex(link => link.TopicId);
        exerciseTopics.HasOne(link => link.Exercise)
            .WithMany(exercise => exercise.ExerciseTopics)
            .HasForeignKey(link => link.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);
        exerciseTopics.HasOne(link => link.Topic)
            .WithMany(topic => topic.ExerciseTopics)
            .HasForeignKey(link => link.TopicId)
            .OnDelete(DeleteBehavior.Restrict);

        var blanks = modelBuilder.Entity<ExerciseBlankEntity>();
        blanks.ToTable("exercise_blanks", table =>
            table.HasCheckConstraint("CK_exercise_blanks_points", "\"Points\" > 0"));
        blanks.HasKey(blank => blank.Id);
        blanks.HasAlternateKey(blank => new { blank.ExerciseId, blank.Id });
        blanks.Property(blank => blank.BlankKey).HasMaxLength(80).IsRequired();
        blanks.Property(blank => blank.InputType).HasMaxLength(16).IsRequired();
        blanks.Property(blank => blank.Points).HasPrecision(8, 2);
        blanks.HasIndex(blank => new { blank.ExerciseId, blank.BlankKey }).IsUnique();
        blanks.HasIndex(blank => new { blank.ExerciseId, blank.DisplayOrder }).IsUnique();
        blanks.ToTable(table =>
            table.HasCheckConstraint("CK_exercise_blanks_input_type", "\"InputType\" IN ('text', 'select')"));
        blanks.HasOne(blank => blank.Exercise)
            .WithMany(exercise => exercise.Blanks)
            .HasForeignKey(blank => blank.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);

        var answers = modelBuilder.Entity<ExerciseBlankAnswerEntity>();
        answers.ToTable("exercise_blank_answers", table =>
            table.HasCheckConstraint("CK_exercise_blank_answers_non_empty", "LENGTH(TRIM(\"Answer\")) > 0"));
        answers.HasKey(answer => answer.Id);
        answers.Property(answer => answer.Answer).HasMaxLength(200).IsRequired();
        answers.Property(answer => answer.Explanation).HasMaxLength(1_000);
        answers.HasOne(answer => answer.Blank)
            .WithMany(blank => blank.Answers)
            .HasForeignKey(answer => answer.BlankId)
            .OnDelete(DeleteBehavior.Restrict);
        answers.HasIndex(answer => answer.BlankId)
            .IsUnique()
            .HasFilter("\"IsPrimary\" = TRUE");

        var questions = modelBuilder.Entity<ExerciseQuestionEntity>();
        questions.ToTable("exercise_questions", table =>
            table.HasCheckConstraint("CK_exercise_questions_points", "\"Points\" > 0"));
        questions.HasKey(question => question.Id);
        questions.HasAlternateKey(question => new { question.ExerciseId, question.Id });
        questions.Property(question => question.QuestionKey).HasMaxLength(80).IsRequired();
        questions.Property(question => question.Prompt).HasMaxLength(1_000).IsRequired();
        questions.Property(question => question.Points).HasPrecision(8, 2);
        questions.HasIndex(question => new { question.ExerciseId, question.QuestionKey }).IsUnique();
        questions.HasIndex(question => new { question.ExerciseId, question.DisplayOrder }).IsUnique();
        questions.HasOne(question => question.Exercise)
            .WithMany(exercise => exercise.Questions)
            .HasForeignKey(question => question.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);

        var options = modelBuilder.Entity<ExerciseOptionEntity>();
        options.ToTable("exercise_options", table =>
        {
            table.HasCheckConstraint(
                "CK_exercise_options_single_parent",
                "(\"QuestionId\" IS NOT NULL AND \"BlankId\" IS NULL) OR (\"QuestionId\" IS NULL AND \"BlankId\" IS NOT NULL)");
        });
        options.HasKey(option => option.Id);
        options.Property(option => option.OptionKey).HasMaxLength(40).IsRequired();
        options.Property(option => option.OptionText).HasMaxLength(200).IsRequired();
        options.Property(option => option.Explanation).HasMaxLength(1_000);
        options.HasOne(option => option.Exercise)
            .WithMany(exercise => exercise.Options)
            .HasForeignKey(option => option.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);
        options.HasOne(option => option.Question)
            .WithMany()
            .HasForeignKey(option => new { option.ExerciseId, option.QuestionId })
            .HasPrincipalKey(question => new { question.ExerciseId, Id = question.Id })
            .OnDelete(DeleteBehavior.Restrict);
        options.HasOne(option => option.Blank)
            .WithMany()
            .HasForeignKey(option => new { option.ExerciseId, option.BlankId })
            .HasPrincipalKey(blank => new { blank.ExerciseId, Id = blank.Id })
            .OnDelete(DeleteBehavior.Restrict);
        options.HasIndex(option => new { option.ExerciseId, option.QuestionId, option.OptionKey })
            .IsUnique()
            .HasFilter("\"QuestionId\" IS NOT NULL");
        options.HasIndex(option => new { option.ExerciseId, option.BlankId, option.OptionKey })
            .IsUnique()
            .HasFilter("\"BlankId\" IS NOT NULL");
        options.HasIndex(option => new { option.ExerciseId, option.QuestionId, option.DisplayOrder })
            .IsUnique()
            .HasFilter("\"QuestionId\" IS NOT NULL");
        options.HasIndex(option => new { option.ExerciseId, option.BlankId, option.DisplayOrder })
            .IsUnique()
            .HasFilter("\"BlankId\" IS NOT NULL");

        SeedContent(modelBuilder);
    }

    private static void SeedContent(ModelBuilder modelBuilder)
    {
        var topicId = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e01");
        var theoryId = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e02");
        var blanksExerciseId = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e03");
        var choiceExerciseId = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e04");
        var firstBlankId = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e05");
        var secondBlankId = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e06");
        var questionId = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e07");
        var createdAt = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

        modelBuilder.Entity<GrammarTopicEntity>().HasData(new GrammarTopicEntity
        {
            Id = topicId,
            Slug = "present-simple-to-be",
            Title = "Present Simple: verb to be",
            Description = "Uso de am, is e are no presente simples.",
            Level = "A1",
            DisplayOrder = 1,
            IsActive = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });
        modelBuilder.Entity<GrammarTheorySectionEntity>().HasData(new GrammarTheorySectionEntity
        {
            Id = theoryId,
            TopicId = topicId,
            Title = "Formas de am, is e are",
            DisplayOrder = 1,
            ContentJson = """[{"type":"paragraph","text":"O verbo to be significa ser ou estar. No presente simples, sua forma depende do sujeito."},{"type":"table","headers":["Pronome","Verbo","Exemplo"],"rows":[["I","am","I am a student."],["You","are","You are a student."],["He / She / It","is","She is a doctor."],["We / They","are","We are friends."]]},{"type":"list","ordered":false,"items":["Use am com I.","Use is com he, she e it.","Use are com you, we e they."]}]""",
        });
        modelBuilder.Entity<ExerciseEntity>().HasData(
            new ExerciseEntity
            {
                Id = blanksExerciseId,
                Title = "Complete com am, is ou are",
                Instruction = "Escolha a forma correta do verbo to be.",
                ExerciseType = "fill_blank",
                ContentTemplate = "{{blank:1}} you a teacher?\n\nB: Yes, I {{blank:2}}.",
                Explanation = "A forma do verbo to be concorda com o sujeito.",
                CaseSensitive = false,
                DisplayOrder = 1,
                IsActive = true,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            },
            new ExerciseEntity
            {
                Id = choiceExerciseId,
                Title = "Escolha a forma correta",
                Instruction = "Selecione a forma correta para completar a frase.",
                ExerciseType = "multiple_choice",
                ContentTemplate = "She ___ a doctor.",
                Explanation = "Use is com os pronomes he, she e it.",
                CaseSensitive = false,
                DisplayOrder = 2,
                IsActive = true,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            });
        modelBuilder.Entity<ExerciseTopicEntity>().HasData(
            new ExerciseTopicEntity { ExerciseId = blanksExerciseId, TopicId = topicId },
            new ExerciseTopicEntity { ExerciseId = choiceExerciseId, TopicId = topicId });
        modelBuilder.Entity<ExerciseBlankEntity>().HasData(
            new ExerciseBlankEntity
            {
                Id = firstBlankId,
                ExerciseId = blanksExerciseId,
                BlankKey = "1",
                DisplayOrder = 1,
                InputType = "select",
                Points = 1,
            },
            new ExerciseBlankEntity
            {
                Id = secondBlankId,
                ExerciseId = blanksExerciseId,
                BlankKey = "2",
                DisplayOrder = 2,
                InputType = "select",
                Points = 1,
            });
        modelBuilder.Entity<ExerciseBlankAnswerEntity>().HasData(
            new ExerciseBlankAnswerEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e08"),
                BlankId = firstBlankId,
                Answer = "Are",
                IsPrimary = true,
                Explanation = "Use are with the pronoun you.",
            },
            new ExerciseBlankAnswerEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e09"),
                BlankId = secondBlankId,
                Answer = "am",
                IsPrimary = true,
                Explanation = "Use am with the pronoun I.",
            });
        modelBuilder.Entity<ExerciseQuestionEntity>().HasData(new ExerciseQuestionEntity
        {
            Id = questionId,
            ExerciseId = choiceExerciseId,
            QuestionKey = "1",
            Prompt = "She ___ a doctor.",
            DisplayOrder = 1,
            Points = 1,
        });

        var choiceOptions = modelBuilder.Entity<ExerciseOptionEntity>();
        choiceOptions.HasData(
            new ExerciseOptionEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e10"),
                ExerciseId = blanksExerciseId,
                BlankId = firstBlankId,
                OptionKey = "am",
                OptionText = "am",
                DisplayOrder = 1,
            },
            new ExerciseOptionEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e11"),
                ExerciseId = blanksExerciseId,
                BlankId = firstBlankId,
                OptionKey = "is",
                OptionText = "is",
                DisplayOrder = 2,
            },
            new ExerciseOptionEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e12"),
                ExerciseId = blanksExerciseId,
                BlankId = firstBlankId,
                OptionKey = "are",
                OptionText = "are",
                IsCorrect = true,
                DisplayOrder = 3,
            },
            new ExerciseOptionEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e13"),
                ExerciseId = blanksExerciseId,
                BlankId = secondBlankId,
                OptionKey = "am",
                OptionText = "am",
                IsCorrect = true,
                DisplayOrder = 1,
            },
            new ExerciseOptionEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e14"),
                ExerciseId = blanksExerciseId,
                BlankId = secondBlankId,
                OptionKey = "is",
                OptionText = "is",
                DisplayOrder = 2,
            },
            new ExerciseOptionEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e15"),
                ExerciseId = blanksExerciseId,
                BlankId = secondBlankId,
                OptionKey = "are",
                OptionText = "are",
                DisplayOrder = 3,
            },
            new ExerciseOptionEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e16"),
                ExerciseId = choiceExerciseId,
                QuestionId = questionId,
                OptionKey = "A",
                OptionText = "am",
                DisplayOrder = 1,
            },
            new ExerciseOptionEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e17"),
                ExerciseId = choiceExerciseId,
                QuestionId = questionId,
                OptionKey = "B",
                OptionText = "is",
                IsCorrect = true,
                Explanation = "Use is with she.",
                DisplayOrder = 2,
            },
            new ExerciseOptionEntity
            {
                Id = Guid.Parse("b9a9e289-6e16-4f17-a144-81b12f2c5e18"),
                ExerciseId = choiceExerciseId,
                QuestionId = questionId,
                OptionKey = "C",
                OptionText = "are",
                DisplayOrder = 3,
            });
    }
}
