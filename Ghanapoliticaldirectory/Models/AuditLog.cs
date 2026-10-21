namespace Ghanapoliticaldirectory.Models
{
    public class AuditLog:Entity
    {
        public DateTime AtUtc { get; set; }
        public string? UserId { get; set; }
        public string UserName { get; set; } = "";
        public string Action { get; set; } = "";
        public string EntityType { get; set; } = "";
        public int? EntityId { get; set; }
        public string Summary { get; set; } = "";
        public string? DetailsJson { get; set; }
        public string? IpAddress { get; set; }
    }

    public class RecordRevision : Entity
    {
        public string EntityType { get; set; } = "";
        public int EntityId { get; set; }
        public int RevisionNumber { get; set; }
        public string SnapshotJson { get; set; } = "";
        public string Reason { get; set; } = "";
        public string ChangedBy { get; set; } = "";
        public DateTime ChangedAtUtc { get; set; }
    }
}
