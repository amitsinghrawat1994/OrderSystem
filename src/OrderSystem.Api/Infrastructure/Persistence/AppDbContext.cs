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
                });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ProcessedOnUtc);
        });
    }
}
