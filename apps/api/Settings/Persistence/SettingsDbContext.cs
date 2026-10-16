using Microsoft.EntityFrameworkCore;

namespace Api.Settings.Persistence;

public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : DbContext(options)
{
    public DbSet<AppSettingEntity> Settings => Set<AppSettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSettingEntity>(entity =>
        {
            entity.ToTable("app_settings");
            entity.HasKey(setting => setting.Key);
            entity.Property(setting => setting.Key).HasMaxLength(100);
            entity.Property(setting => setting.ProtectedValue).HasMaxLength(2_048).IsRequired();
        });
    }
}
