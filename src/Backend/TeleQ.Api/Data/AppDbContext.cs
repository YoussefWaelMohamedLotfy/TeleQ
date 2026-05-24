using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data.Entities;

namespace TeleQ.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<ClerkAssignment> ClerkAssignments => Set<ClerkAssignment>();
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
            b.Property(x => x.Description).HasMaxLength(1000);
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

        modelBuilder.Entity<ClerkAssignment>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ClerkId).HasMaxLength(200).IsRequired();
            b.Property(x => x.ClerkDisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.CounterLabel).HasMaxLength(50).IsRequired();
            b.HasOne(x => x.Branch)
             .WithMany(x => x.ClerkAssignments)
             .HasForeignKey(x => x.BranchId)
             .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Service)
             .WithMany(x => x.ClerkAssignments)
             .HasForeignKey(x => x.ServiceId)
             .OnDelete(DeleteBehavior.Restrict);
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
