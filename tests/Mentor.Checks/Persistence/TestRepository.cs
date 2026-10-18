using Mentor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Mentor.Checks.Persistence;

internal static class TestRepository
{
    public static EfMentorRepository Create(string path, string initialPrompt, TimeProvider clock)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new DbContextOptionsBuilder<MentorDbContext>()
            .UseSqlite(MentorDbContext.ConnectionString(path)).Options;
        return new EfMentorRepository(new PooledDbContextFactory<MentorDbContext>(options), initialPrompt, clock);
    }
}
