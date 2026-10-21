

using Ghanapoliticaldirectory.Business;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ghanapoliticaldirectory.Data;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One database for the public directory and the administrators who maintain it.
/// Rules enforced here, whatever the caller does:
///  * records are never deleted (deleting an audited record throws; archive it instead);
///  * audit log entries and revisions are append-only;
///  * every audited record is stamped with who created or changed it and when.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ICurrentUser? _currentUser;
    private readonly IClock _clock;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null, IClock? clock = null)
        : base(options)
    {
        _currentUser = currentUser;
        _clock = clock ?? new SystemClock();
    }

    public DbSet<Person> People => Set<Person>();
    public DbSet<PoliticalParty> PoliticalParties => Set<PoliticalParty>();
    public DbSet<PartyAffiliation> PartyAffiliations => Set<PartyAffiliation>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Constituency> Constituencies => Set<Constituency>();
    public DbSet<ElectionCycle> ElectionCycles => Set<ElectionCycle>();
    public DbSet<ElectionContest> ElectionContests => Set<ElectionContest>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<ElectionResult> ElectionResults => Set<ElectionResult>();
    public DbSet<CandidateResult> CandidateResults => Set<CandidateResult>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<ResultSource> ResultSources => Set<ResultSource>();
    public DbSet<PersonSource> PersonSources => Set<PersonSource>();
    public DbSet<Administration> Administrations => Set<Administration>();
    public DbSet<Ministry> Ministries => Set<Ministry>();
    public DbSet<MinistryNameEra> MinistryNameEras => Set<MinistryNameEra>();
    public DbSet<MinistryLineage> MinistryLineages => Set<MinistryLineage>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ParliamentaryTerm> ParliamentaryTerms => Set<ParliamentaryTerm>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RecordRevision> RecordRevisions => Set<RecordRevision>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Identity tables
        ModelConfiguration.Apply(builder);

        // SQLite (used by the automated tests) cannot generate SQL Server style row versions, so concurrency
        // tokens are switched off there. SQL Server keeps them.
        if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            foreach (var type in builder.Model.GetEntityTypes())
            {
                var rowVersion = type.FindProperty(nameof(AuditedEntity.RowVersion));
                if (rowVersion is null) continue;
                rowVersion.IsConcurrencyToken = false;
                rowVersion.ValueGenerated = ValueGenerated.Never;
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Stamp()
    {
        var now = _clock.UtcNow;
        var who = _currentUser?.UserId ?? "system";

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            switch (entry.Entity)
            {
                case AuditedEntity audited:
                    if (entry.State == EntityState.Deleted)
                        throw new InvalidOperationException(
                            $"{audited.GetType().Name} #{audited.Id} cannot be deleted. Records are kept for the historical archive: archive it instead.");
                    if (entry.State == EntityState.Added)
                    {
                        if (audited.CreatedAtUtc == default) audited.CreatedAtUtc = now;
                        audited.CreatedBy ??= who;
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        audited.UpdatedAtUtc = now;
                        audited.UpdatedBy = who;
                    }
                    break;

                case AuditLog or RecordRevision:
                    if (entry.State is EntityState.Modified or EntityState.Deleted)
                        throw new InvalidOperationException("Audit log entries and revisions are append-only.");
                    break;
            }
        }
    }
}
