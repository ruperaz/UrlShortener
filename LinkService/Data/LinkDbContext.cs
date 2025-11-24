using LinkService.Models;
using Microsoft.EntityFrameworkCore;

namespace LinkService.Data;

public class LinkDbContext : DbContext
{
    public LinkDbContext(DbContextOptions<LinkDbContext> options) : base(options)
    {
    }

    public DbSet<ShortLink> ShortLinks => Set<ShortLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ShortLink>()
            .HasIndex(x => x.ShortCode)
            .IsUnique();
    }
}