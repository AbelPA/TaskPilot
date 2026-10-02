using Api.AudioExtractions.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.AudioExtractions.Persistence;

public sealed class AudioExtractionDbContext(DbContextOptions<AudioExtractionDbContext> options)
    : DbContext(options)
{
    public DbSet<AudioExtractionRequestEntity> Requests => Set<AudioExtractionRequestEntity>();
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();
    public DbSet<ProcessedEventEntity> ProcessedEvents => Set<ProcessedEventEntity>();
    public DbSet<NotificationOutcomeEntity> NotificationOutcomes => Set<NotificationOutcomeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var requests = modelBuilder.Entity<AudioExtractionRequestEntity>();
        requests.ToTable("audio_extraction_requests");
        requests.HasKey(request => request.RequestId);
        requests.Property(request => request.RequestId).HasMaxLength(43);
        requests.Property(request => request.VideoId).HasMaxLength(11).IsRequired();
        requests.Property(request => request.OutputFormat).HasMaxLength(8).IsRequired();
        requests.Property(request => request.Status).HasMaxLength(16).IsRequired();
        requests.Property(request => request.ResultObjectKey).HasMaxLength(512);
        requests.Property(request => request.ResultChecksumSha256).HasMaxLength(64);
        requests.Property(request => request.FailureCode).HasMaxLength(64);
        requests.Property(request => request.FailureMessage).HasMaxLength(512);
        requests.Property(request => request.IdempotencyKeyHash).HasMaxLength(64);
        requests.Property(request => request.IdempotencyRequestHash).HasMaxLength(64);
        requests.HasIndex(request => request.IdempotencyKeyHash)
            .IsUnique()
            .HasDatabaseName("IX_audio_extraction_requests_idempotency_key_hash");
        requests.ToTable(table =>
        {
            table.HasCheckConstraint("CK_audio_extraction_requests_interval", "\"StartSeconds\" >= 0 AND \"EndSeconds\" > \"StartSeconds\"");
            table.HasCheckConstraint("CK_audio_extraction_requests_interval_limit", "\"EndSeconds\" - \"StartSeconds\" <= 1800 AND \"EndSeconds\" <= \"SourceDurationSeconds\"");
            table.HasCheckConstraint("CK_audio_extraction_requests_status", "\"Status\" IN ('accepted', 'completed', 'failed')");
            table.HasCheckConstraint("CK_audio_extraction_requests_source_duration", "\"SourceDurationSeconds\" > 0 AND \"SourceDurationSeconds\" <= 21600");
            table.HasCheckConstraint("CK_audio_extraction_requests_output_format", "\"OutputFormat\" = 'mp3'");
            table.HasCheckConstraint("CK_audio_extraction_requests_idempotency_hashes", "(\"IdempotencyKeyHash\" IS NULL) = (\"IdempotencyRequestHash\" IS NULL)");
            table.HasCheckConstraint(
                "CK_audio_extraction_requests_terminal_fields",
                "(\"Status\" = 'accepted' AND \"ExpiresAt\" IS NULL AND \"ResultObjectKey\" IS NULL AND \"ResultSizeBytes\" IS NULL AND \"ResultChecksumSha256\" IS NULL AND \"ResultDurationSeconds\" IS NULL AND \"FailureCode\" IS NULL AND \"FailureMessage\" IS NULL) OR " +
                "(\"Status\" = 'completed' AND \"ExpiresAt\" IS NOT NULL AND \"ResultObjectKey\" IS NOT NULL AND \"ResultSizeBytes\" BETWEEN 1 AND 104857600 AND LENGTH(\"ResultChecksumSha256\") = 64 AND \"ResultDurationSeconds\" > 0 AND ABS(\"ResultDurationSeconds\" - (\"EndSeconds\" - \"StartSeconds\")) <= 2 AND \"FailureCode\" IS NULL AND \"FailureMessage\" IS NULL) OR " +
                "(\"Status\" = 'failed' AND \"ExpiresAt\" IS NOT NULL AND \"FailureCode\" IS NOT NULL AND \"FailureMessage\" IS NOT NULL AND \"ResultObjectKey\" IS NULL AND \"ResultSizeBytes\" IS NULL AND \"ResultChecksumSha256\" IS NULL AND \"ResultDurationSeconds\" IS NULL)");
        });

        var outbox = modelBuilder.Entity<OutboxMessageEntity>();
        outbox.ToTable("audio_extraction_outbox");
        outbox.HasKey(message => message.EventId);
        outbox.Property(message => message.EventType).HasMaxLength(80).IsRequired();
        outbox.Property(message => message.PayloadJson).HasColumnType("jsonb").IsRequired();
        outbox.Property(message => message.State).HasMaxLength(16).IsRequired();
        outbox.HasIndex(message => new { message.State, message.NextAttemptAt })
            .HasDatabaseName("IX_audio_extraction_outbox_state_next_attempt");
        outbox.HasOne(message => message.Request)
            .WithMany(request => request.OutboxMessages)
            .HasForeignKey(message => message.RequestId)
            .OnDelete(DeleteBehavior.Restrict);
        outbox.ToTable(table =>
        {
            table.HasCheckConstraint("CK_audio_extraction_outbox_state", "\"State\" IN ('pending', 'published')");
            table.HasCheckConstraint("CK_audio_extraction_outbox_attempt_count", "\"AttemptCount\" >= 0");
            table.HasCheckConstraint(
                "CK_audio_extraction_outbox_published_at",
                "(\"State\" = 'published' AND \"PublishedAt\" IS NOT NULL) OR (\"State\" = 'pending' AND \"PublishedAt\" IS NULL)");
        });

        var processedEvents = modelBuilder.Entity<ProcessedEventEntity>();
        processedEvents.ToTable("audio_extraction_processed_events");
        processedEvents.HasKey(message => message.EventId);
        processedEvents.Property(message => message.EventType).HasMaxLength(80).IsRequired();

        var notifications = modelBuilder.Entity<NotificationOutcomeEntity>();
        notifications.ToTable("audio_extraction_notification_outcomes");
        notifications.HasKey(outcome => outcome.RequestId);
        notifications.Property(outcome => outcome.RequestId).HasMaxLength(43);
        notifications.Property(outcome => outcome.Status).HasMaxLength(16).IsRequired();
        notifications.Property(outcome => outcome.SafeMessage).HasMaxLength(240).IsRequired();
        notifications.HasIndex(outcome => outcome.EventId).IsUnique();
        notifications.HasOne(outcome => outcome.Request)
            .WithOne(request => request.NotificationOutcome)
            .HasForeignKey<NotificationOutcomeEntity>(outcome => outcome.RequestId)
            .OnDelete(DeleteBehavior.Restrict);
        notifications.ToTable(table =>
            table.HasCheckConstraint(
                "CK_audio_extraction_notification_status",
                "\"Status\" IN ('completed', 'failed')"));
    }
}
