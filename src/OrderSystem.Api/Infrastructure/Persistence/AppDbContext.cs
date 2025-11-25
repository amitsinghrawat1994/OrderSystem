using System;
using Microsoft.EntityFrameworkCore;
using OrderSystem.Shared.Contracts;

namespace OrderSystem.Api.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // This represents your actual Business Data
    // (In a real app, you'd have a full Order entity here)
    public DbSet<OrderCreatedEvent> Orders { get; set; }

    // This represents the Queue inside your SQL DB
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderCreatedEvent>(b =>
        {
            b.HasKey(o => o.OrderId);
            // Use explicit precision for decimal columns to avoid EF Core warnings
            b.Property(o => o.TotalAmount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ProcessedOnUtc);
        });
    }
}
