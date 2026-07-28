using Microsoft.EntityFrameworkCore;

namespace MekomonPublisher.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<PublishHistoryEntry> PublishHistory => Set<PublishHistoryEntry>();
}
