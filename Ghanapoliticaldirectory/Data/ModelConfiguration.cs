

using Ghanapoliticaldirectory.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ghanapoliticaldirectory.Data;

/// <summary>
/// Table design. Everything is explicit so the schema does not depend on naming conventions:
///  * all foreign keys are RESTRICT (nothing cascades, because nothing is ever deleted);
///  * date rules and the presidential/parliamentary split are enforced by CHECK constraints;
///  * unique indexes stop duplicate candidates, duplicate contests and a second declared winner.
/// </summary>
internal static class ModelConfiguration
{
    public static void Apply(ModelBuilder b)
    {
        Person(b);
        Party(b);
        Geography(b);
        Elections(b);
        Sources(b);
        Government(b);
        History(b);

        // Nothing cascades. Only our own tables; Identity keeps its defaults.
        var ours = typeof(Person).Namespace;
        foreach (var fk in b.Model.GetEntityTypes()
                     .Where(t => t.ClrType.Namespace == ours)
                     .SelectMany(t => t.GetForeignKeys()))
        {
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    private static void Audited<T>(EntityTypeBuilder<T> e) where T : AuditedEntity
    {
        e.Property(x => x.CreatedBy).HasMaxLength(450);
        e.Property(x => x.UpdatedBy).HasMaxLength(450);
        e.Property(x => x.ArchivedBy).HasMaxLength(450);
        e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => x.IsArchived);
    }

    private static void Publishable<T>(EntityTypeBuilder<T> e) where T : PublishableEntity
    {
        Audited(e);
        e.Property(x => x.VerifiedBy).HasMaxLength(450);
        e.Property(x => x.ApprovedBy).HasMaxLength(450);
        e.Property(x => x.PublishedBy).HasMaxLength(450);
        e.Property(x => x.ReviewNotes).HasMaxLength(2000);
        e.HasIndex(x => x.Stage);
        e.Ignore(x => x.IsPublic);
    }

    private static void Person(ModelBuilder b)
    {
        b.Entity<Person>(e =>
        {
            e.ToTable("People");
            Publishable(e);
            e.Property(x => x.FullName).IsRequired().HasMaxLength(200);
            e.Property(x => x.NormalizedName).IsRequired().HasMaxLength(200);
            e.Property(x => x.PhotoUrl).HasMaxLength(500);
            e.Property(x => x.PhotoAltText).HasMaxLength(300);
            e.Property(x => x.PhotoContentType).HasMaxLength(100);
            e.HasIndex(x => x.NormalizedName);
        });

        b.Entity<PersonSource>(e =>
        {
            e.ToTable("PersonSources");
            e.HasKey(x => new { x.PersonId, x.SourceId });
            e.HasOne(x => x.Person).WithMany(p => p.Sources).HasForeignKey(x => x.PersonId);
            e.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId);
        });
    }

    private static void Party(ModelBuilder b)
    {
        b.Entity<PoliticalParty>(e =>
        {
            e.ToTable("PoliticalParties");
            Audited(e);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Abbreviation).IsRequired().HasMaxLength(20);
            e.Property(x => x.WebsiteUrl).HasMaxLength(500);
            e.Property(x => x.LogoUrl).HasMaxLength(500);
            e.Property(x => x.LogoAltText).HasMaxLength(300);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.Abbreviation).IsUnique();
        });

        b.Entity<PartyAffiliation>(e =>
        {
            e.ToTable("PartyAffiliations", t => t.HasCheckConstraint("CK_PartyAffiliations_Dates", "[EndDate] IS NULL OR [EndDate] >= [StartDate]"));
            Publishable(e);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne(x => x.Person).WithMany(p => p.Affiliations).HasForeignKey(x => x.PersonId);
            e.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).IsRequired(false);
            e.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId).IsRequired(false);
            e.HasIndex(x => new { x.PersonId, x.StartDate });
        });
    }

    private static void Geography(ModelBuilder b)
    {
        b.Entity<Region>(e =>
        {
            e.ToTable("Regions");
            Audited(e);
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Code).HasMaxLength(10);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Constituency>(e =>
        {
            e.ToTable("Constituencies");
            Audited(e);
            e.Property(x => x.Name).IsRequired().HasMaxLength(150);
            e.HasOne(x => x.Region).WithMany(r => r.Constituencies).HasForeignKey(x => x.RegionId);
            e.HasIndex(x => new { x.RegionId, x.Name }).IsUnique();
        });
    }

    private static void Elections(ModelBuilder b)
    {
        b.Entity<ElectionCycle>(e =>
        {
            e.ToTable("ElectionCycles");
            Publishable(e);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => new { x.Year, x.Name }).IsUnique();
        });

        b.Entity<ElectionContest>(e =>
        {
            e.ToTable("ElectionContests", t =>
            {
                // A presidential contest has no constituency; a parliamentary contest must have one.
                t.HasCheckConstraint("CK_ElectionContests_Type",
                    "([ContestType] = 0 AND [ConstituencyId] IS NULL) OR ([ContestType] = 1 AND [ConstituencyId] IS NOT NULL)");
                t.HasCheckConstraint("CK_ElectionContests_Round", "[Round] >= 1");
            });
            Audited(e);
            e.HasOne(x => x.ElectionCycle).WithMany().HasForeignKey(x => x.ElectionCycleId);
            e.HasOne(x => x.Constituency).WithMany().HasForeignKey(x => x.ConstituencyId).IsRequired(false);
            // One contest per cycle, type, constituency and round. SQL Server treats the NULL constituency of a
            // presidential contest as a single value, so there can only be one presidential contest per round.
            e.HasIndex(x => new { x.ElectionCycleId, x.ContestType, x.ConstituencyId, x.Round }).IsUnique();
        });

        b.Entity<Candidate>(e =>
        {
            e.ToTable("Candidates");
            Publishable(e);
            e.HasOne(x => x.ElectionContest).WithMany(c => c.Candidates).HasForeignKey(x => x.ElectionContestId);
            e.HasOne(x => x.Person).WithMany(p => p.Candidacies).HasForeignKey(x => x.PersonId);
            e.HasOne(x => x.RunningMate).WithMany().HasForeignKey(x => x.RunningMatePersonId).IsRequired(false);
            e.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).IsRequired(false);
            e.HasIndex(x => new { x.ElectionContestId, x.PersonId }).IsUnique();
            e.HasIndex(x => x.PartyId);
        });

        b.Entity<ElectionResult>(e =>
        {
            e.ToTable("ElectionResults", t => t.HasCheckConstraint("CK_ElectionResults_NonNegative",
                "([ValidVotes] IS NULL OR [ValidVotes] >= 0) AND ([RejectedBallots] IS NULL OR [RejectedBallots] >= 0) " +
                "AND ([TotalVotesCast] IS NULL OR [TotalVotesCast] >= 0) AND ([RegisteredVoters] IS NULL OR [RegisteredVoters] >= 0)"));
            Publishable(e);
            e.Property(x => x.CorrectionNote).HasMaxLength(2000);
            e.Property(x => x.VerificationNotes).HasMaxLength(4000);
            e.HasOne(x => x.ElectionContest).WithOne(c => c.Result).HasForeignKey<ElectionResult>(x => x.ElectionContestId);
            e.HasIndex(x => x.ElectionContestId).IsUnique();
        });

        b.Entity<CandidateResult>(e =>
        {
            e.ToTable("CandidateResults", t => t.HasCheckConstraint("CK_CandidateResults_Votes", "[Votes] IS NULL OR [Votes] >= 0"));
            e.HasOne(x => x.ElectionResult).WithMany(r => r.Lines).HasForeignKey(x => x.ElectionResultId);
            e.HasOne(x => x.Candidate).WithMany().HasForeignKey(x => x.CandidateId);
            e.HasIndex(x => new { x.ElectionResultId, x.CandidateId }).IsUnique();
            // At most one declared winner per result, enforced by the database as well as the service.
            e.HasIndex(x => x.ElectionResultId).IsUnique().HasFilter("[IsDeclaredWinner] = 1").HasDatabaseName("UX_CandidateResults_OneWinner");
        });

        b.Entity<ResultSource>(e =>
        {
            e.ToTable("ResultSources");
            e.HasKey(x => new { x.ElectionResultId, x.SourceId });
            e.HasOne(x => x.ElectionResult).WithMany(r => r.Sources).HasForeignKey(x => x.ElectionResultId);
            e.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId);
        });
    }

    private static void Sources(ModelBuilder b)
    {
        b.Entity<Source>(e =>
        {
            e.ToTable("Sources");
            Audited(e);
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.Url).HasMaxLength(1000);
            e.Property(x => x.Notes).HasMaxLength(2000);
        });
    }

    private static void Government(ModelBuilder b)
    {
        b.Entity<Administration>(e =>
        {
            e.ToTable("Administrations", t => t.HasCheckConstraint("CK_Administrations_Dates", "[EndDate] IS NULL OR [EndDate] >= [StartDate]"));
            Publishable(e);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasOne(x => x.ElectionCycle).WithMany().HasForeignKey(x => x.ElectionCycleId).IsRequired(false);
            e.HasIndex(x => x.StartDate);
        });

        b.Entity<Ministry>(e =>
        {
            e.ToTable("Ministries");
            Audited(e);
            e.Property(x => x.Name).IsRequired().HasMaxLength(250);
            e.Property(x => x.WebsiteUrl).HasMaxLength(500);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<MinistryNameEra>(e =>
        {
            e.ToTable("MinistryNameEras", t => t.HasCheckConstraint("CK_MinistryNameEras_Dates", "[ValidTo] IS NULL OR [ValidTo] >= [ValidFrom]"));
            Audited(e);
            e.Property(x => x.Name).IsRequired().HasMaxLength(250);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.Ministry).WithMany(m => m.Names).HasForeignKey(x => x.MinistryId);
            e.HasIndex(x => new { x.MinistryId, x.ValidFrom });
        });

        b.Entity<MinistryLineage>(e =>
        {
            e.ToTable("MinistryLineages");
            Audited(e);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.FromMinistry).WithMany().HasForeignKey(x => x.FromMinistryId);
            e.HasOne(x => x.ToMinistry).WithMany().HasForeignKey(x => x.ToMinistryId);
        });

        b.Entity<Position>(e =>
        {
            e.ToTable("Positions");
            Audited(e);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => new { x.Kind, x.Title }).IsUnique();
        });

        b.Entity<Appointment>(e =>
        {
            e.ToTable("Appointments", t => t.HasCheckConstraint("CK_Appointments_Dates", "[EndDate] IS NULL OR [EndDate] >= [StartDate]"));
            Publishable(e);
            e.Property(x => x.EndReason).HasMaxLength(500);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne(x => x.Person).WithMany(p => p.Appointments).HasForeignKey(x => x.PersonId);
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId);
            e.HasOne(x => x.Administration).WithMany().HasForeignKey(x => x.AdministrationId).IsRequired(false);
            e.HasOne(x => x.Ministry).WithMany().HasForeignKey(x => x.MinistryId).IsRequired(false);
            e.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId).IsRequired(false);
            e.HasIndex(x => new { x.PersonId, x.StartDate });
            e.HasIndex(x => new { x.PositionId, x.MinistryId, x.StartDate });
            e.HasIndex(x => x.AdministrationId);
        });

        b.Entity<ParliamentaryTerm>(e =>
        {
            e.ToTable("ParliamentaryTerms", t => t.HasCheckConstraint("CK_ParliamentaryTerms_Dates", "[EndDate] IS NULL OR [EndDate] >= [StartDate]"));
            Publishable(e);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasOne(x => x.Person).WithMany(p => p.ParliamentaryTerms).HasForeignKey(x => x.PersonId);
            e.HasOne(x => x.Constituency).WithMany().HasForeignKey(x => x.ConstituencyId);
            e.HasOne(x => x.ElectionCycle).WithMany().HasForeignKey(x => x.ElectionCycleId).IsRequired(false);
            e.HasOne(x => x.Candidate).WithMany().HasForeignKey(x => x.CandidateId).IsRequired(false);
            e.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId).IsRequired(false);
            e.HasIndex(x => new { x.ConstituencyId, x.StartDate });
            e.HasIndex(x => x.PersonId);
        });
    }

    private static void History(ModelBuilder b)
    {
        b.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.UserName).IsRequired().HasMaxLength(256);
            e.Property(x => x.Action).IsRequired().HasMaxLength(100);
            e.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
            e.Property(x => x.Summary).IsRequired().HasMaxLength(1000);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasIndex(x => x.AtUtc);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        b.Entity<RecordRevision>(e =>
        {
            e.ToTable("RecordRevisions");
            e.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
            e.Property(x => x.Reason).IsRequired().HasMaxLength(1000);
            e.Property(x => x.ChangedBy).IsRequired().HasMaxLength(450);
            e.HasIndex(x => new { x.EntityType, x.EntityId, x.RevisionNumber }).IsUnique();
        });
    }
}
