using Bogus;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data.Entities;

namespace TeleQ.Api.Data;

/// <summary>
/// Provides deterministic seed data for local development using the Bogus library.
/// Seeds are idempotent — data is only inserted when the Branches table is empty.
/// </summary>
public static class AppDbSeeder
{
    private const int RandomSeed = 42;

    /// <summary>Synchronous seed callback compatible with EF Core's <c>UseSeeding</c>.</summary>
    public static void Seed(DbContext context, bool _)
    {
        if (context.Set<Branch>().Any()) return;
        SeedCore(context);
        context.SaveChanges();
    }

    /// <summary>Async seed callback compatible with EF Core's <c>UseAsyncSeeding</c>.</summary>
    public static async Task SeedAsync(DbContext context, bool _, CancellationToken ct)
    {
        if (await context.Set<Branch>().AnyAsync(ct)) return;
        SeedCore(context);
        await context.SaveChangesAsync(ct);
    }

    private static void SeedCore(DbContext context)
    {
        // Pin the global Bogus randomizer seed so data is reproducible across runs.
        Randomizer.Seed = new Random(RandomSeed);
        var faker = new Faker("en");
        var now = DateTimeOffset.UtcNow;

        // ── Branches ──────────────────────────────────────────────────────────────
        var branchNames = new[] { "Downtown Branch", "Westside Branch", "Airport Branch" };

        var branches = branchNames
            .Select(name => new Branch
            {
                Id = Guid.CreateVersion7(),
                Name = name,
                Address = $"{faker.Address.StreetAddress()}, {faker.Address.City()}",
                PhoneNumber = faker.Phone.PhoneNumber("+1-###-###-####"),
                IsActive = true,
                CreatedAt = now,
            })
            .ToList();

        // ── Services ──────────────────────────────────────────────────────────────
        // Three focused financial services per branch, each with a realistic description and duration.
        (Guid BranchId, string Name, string Description, int Duration)[] serviceData =
        [
            (branches[0].Id, "Account Opening",    "Open a new personal or corporate bank account.",              10),
            (branches[0].Id, "Loan Consultation",   "Expert advice on personal and business loan products.",        15),
            (branches[0].Id, "Card Services",        "Issue, replace, or block your debit and credit cards.",       10),

            (branches[1].Id, "Money Transfer",       "Send and receive local and international wire transfers.",     8),
            (branches[1].Id, "Foreign Exchange",     "Buy or sell foreign currencies at competitive rates.",         10),
            (branches[1].Id, "Customer Support",     "Resolve account queries and receive personalised assistance.", 12),

            (branches[2].Id, "Insurance Services",   "Explore life, health, and asset insurance products.",         20),
            (branches[2].Id, "Business Banking",     "Dedicated services for small and medium enterprises.",         15),
            (branches[2].Id, "Investment Advisory",  "Grow your wealth with tailored investment strategies.",        20),
        ];

        var services = serviceData
            .Select(d => new Service
            {
                Id = Guid.CreateVersion7(),
                BranchId = d.BranchId,
                Name = d.Name,
                Description = d.Description,
                EstimatedDurationMinutes = d.Duration,
                IsActive = true,
                CreatedAt = now,
            })
            .ToList();

        // ── Time Slots ────────────────────────────────────────────────────────────
        // Ten recurring weekly slots per service — two per weekday (morning + afternoon).
        (DayOfWeek Day, TimeOnly Start, TimeOnly End, int Capacity)[] slotSchedule =
        [
            (DayOfWeek.Monday,    new TimeOnly(9,  0), new TimeOnly(10, 0), 5),
            (DayOfWeek.Monday,    new TimeOnly(13, 0), new TimeOnly(14, 0), 4),
            (DayOfWeek.Tuesday,   new TimeOnly(9,  0), new TimeOnly(10, 0), 5),
            (DayOfWeek.Tuesday,   new TimeOnly(14, 0), new TimeOnly(15, 0), 4),
            (DayOfWeek.Wednesday, new TimeOnly(9,  0), new TimeOnly(10, 0), 5),
            (DayOfWeek.Wednesday, new TimeOnly(13, 0), new TimeOnly(14, 0), 4),
            (DayOfWeek.Thursday,  new TimeOnly(9,  0), new TimeOnly(10, 0), 5),
            (DayOfWeek.Thursday,  new TimeOnly(14, 0), new TimeOnly(15, 0), 4),
            (DayOfWeek.Friday,    new TimeOnly(9,  0), new TimeOnly(10, 0), 5),
            (DayOfWeek.Friday,    new TimeOnly(11, 0), new TimeOnly(12, 0), 3),
        ];

        var timeSlots = services
            .SelectMany(svc => slotSchedule.Select(s => new TimeSlot
            {
                Id = Guid.CreateVersion7(),
                ServiceId = svc.Id,
                BranchId = svc.BranchId,
                StartTime = s.Start,
                EndTime = s.End,
                Capacity = s.Capacity,
                BookedCount = 0,
                IsRecurring = true,
                DayOfWeek = s.Day,
                IsActive = true,
                CreatedAt = now,
            }))
            .ToList();

        // ── Clerk Assignments ─────────────────────────────────────────────────────
        // Two clerks per service — Counter 1 and Counter 2 — with fake Keycloak subject IDs.
        var clerkAssignments = services
            .SelectMany((svc, i) =>
            {
                // Use per-service seeded faker so each service gets distinct clerk names.
                var localFaker = new Faker("en") { Random = new Randomizer(RandomSeed + i + 1) };

                return new[]
                {
                    new ClerkAssignment
                    {
                        Id = Guid.CreateVersion7(),
                        ClerkId = Guid.CreateVersion7().ToString(),
                        ClerkDisplayName = localFaker.Name.FullName(),
                        CounterLabel = "Counter 1",
                        BranchId = svc.BranchId,
                        ServiceId = svc.Id,
                        IsActive = true,
                        AssignedAt = now,
                    },
                    new ClerkAssignment
                    {
                        Id = Guid.CreateVersion7(),
                        ClerkId = Guid.CreateVersion7().ToString(),
                        ClerkDisplayName = localFaker.Name.FullName(),
                        CounterLabel = "Counter 2",
                        BranchId = svc.BranchId,
                        ServiceId = svc.Id,
                        IsActive = true,
                        AssignedAt = now,
                    },
                };
            })
            .ToList();

        context.Set<Branch>().AddRange(branches);
        context.Set<Service>().AddRange(services);
        context.Set<TimeSlot>().AddRange(timeSlots);
        context.Set<ClerkAssignment>().AddRange(clerkAssignments);
    }
}
