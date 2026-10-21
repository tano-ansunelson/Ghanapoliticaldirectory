using Ghanapoliticaldirectory.Domain;

namespace Ghanapoliticaldirectory.Models
{
    public class Entities
    {
    }

    public abstract class Entity
    {
        public int Id { get; set; }
    }


    public abstract class AuditedEntity : Entity
    {
        public DateTime CreatedAtUtc { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }

        /// <summary>Archived records are hidden from public pages but kept forever.</summary>
        public bool IsArchived { get; set; }
        public DateTime? ArchivedAtUtc { get; set; }
        public string? ArchivedBy { get; set; }

        /// <summary>True for development sample data so it can be found and purged before launch.</summary>
        public bool IsSample { get; set; }

        public byte[]? RowVersion { get; set; }
    }


    public interface IPublishable
    {
        int Id { get; }
        WorkflowStage Stage { get; set; }
        string? CreatedBy { get; }
        string? VerifiedBy { get; set; }
        DateTime? VerifiedAtUtc { get; set; }
        string? ApprovedBy { get; set; }
        DateTime? ApprovedAtUtc { get; set; }
        string? PublishedBy { get; set; }
        DateTime? PublishedAtUtc { get; set; }
        string? ReviewNotes { get; set; }
    }


    public abstract class PublishableEntity: AuditedEntity, IPublishable
    {
        public WorkflowStage Stage { get; set; } = WorkflowStage.Draft;
        public string? VerifiedBy { get; set; }
        public DateTime? VerifiedAtUtc { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
        public string? PublishedBy { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
        public string? ReviewNotes { get; set; }

        public bool IsPublic => Stage == WorkflowStage.Published && !IsArchived;
    }
}
