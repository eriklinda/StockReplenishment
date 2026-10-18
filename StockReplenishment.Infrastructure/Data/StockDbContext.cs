using Microsoft.EntityFrameworkCore;
using StockReplenishment.Domain;

namespace StockReplenishment.Infrastructure.Data;

public class StockDbContext(DbContextOptions<StockDbContext> options) : DbContext(options)
{
    public DbSet<StockLocation> Locations => Set<StockLocation>();
    public DbSet<ReplenishmentRequest> Requests => Set<ReplenishmentRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockLocation>(location =>
        {
            location.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<ReplenishmentRequest>(request =>
        {
            request.HasOne(x => x.Location)
                .WithMany()
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Restrict);

            request.HasMany(x => x.Items)
                .WithOne()
                .HasForeignKey(x => x.ReplenishmentRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            request.HasIndex(x => new { x.Status, x.Priority, x.LocationId });
        });

        modelBuilder.Entity<RequestItem>(item =>
        {
            item.Property(x => x.ArticleNumber).HasMaxLength(50).IsRequired();
            item.Property(x => x.Description).HasMaxLength(200).IsRequired();
        });
    }
}
