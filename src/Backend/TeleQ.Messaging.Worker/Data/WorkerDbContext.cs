using Microsoft.EntityFrameworkCore;
using TeleQ.Messaging.Worker.Data.Entities;

namespace TeleQ.Messaging.Worker.Data;

/// <summary>
/// Read/write EF Core context for the Worker service.
/// Maps to the same Postgres tables owned and migrated by TeleQ.Api.
/// Does NOT run migrations — TeleQ.Api owns the schema.
/// </summary>
public sealed class WorkerDbContext(DbContextOptions<WorkerDbContext> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<TelegramCustomer> TelegramCustomers => Set<TelegramCustomer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Branch>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Address).HasMaxLength(500).IsRequired();
            b.Property(x => x.PhoneNumber).HasMaxLength(30);
        });

        modelBuilder.Entity<Service>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.HasOne(x => x.Branch)
             .WithMany(x => x.Services)
             .HasForeignKey(x => x.BranchId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TimeSlot>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.Service)
             .WithMany(x => x.TimeSlots)
             .HasForeignKey(x => x.ServiceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TelegramCustomer>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.PhoneNumber).HasMaxLength(30).IsRequired();
            b.HasIndex(x => x.PhoneNumber).IsUnique();
            b.HasIndex(x => x.TelegramChatId).IsUnique();
        });
    }
}
