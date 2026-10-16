using Api.Grammar.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AudioApi.Tests.Grammar;

public sealed class GrammarDbContextTests
{
    [Fact]
    public async Task EnsureCreated_seeds_cefr_topic_theory_and_both_exercise_types()
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        SQLitePCL.raw.FreezeProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GrammarDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new GrammarDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        Assert.Equal("A1", await dbContext.Topics.Select(topic => topic.Level).SingleAsync());
        Assert.Single(await dbContext.TheorySections.ToListAsync());
        Assert.Equal(2, await dbContext.Exercises.CountAsync());
        Assert.Equal(2, await dbContext.ExerciseTopics.CountAsync());
        Assert.Equal(2, await dbContext.Blanks.CountAsync());
        Assert.Equal(1, await dbContext.Questions.CountAsync());
    }
}
