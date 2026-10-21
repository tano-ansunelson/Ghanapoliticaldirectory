using Ghanapoliticaldirectory.Domain;

namespace Ghanapoliticaldirectory.Models
{
    public class Ministry:AuditedEntity
    {

        /// <summary>The current display name. Earlier names live in <see cref="Names"/> and are never overwritten.</summary>
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? WebsiteUrl { get; set; }
        public ICollection<MinistryNameEra> Names { get; set; } = new List<MinistryNameEra>();
    }


    public class MinistryNameEra : AuditedEntity
    {
        public int MinistryId { get; set; }
        public Ministry Ministry { get; set; } = null!;
        public string Name { get; set; } = "";
        public DateOnly ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>Records that one ministry was renamed, merged into, or split from another.</summary>
    public class MinistryLineage : AuditedEntity
    {
        public int FromMinistryId { get; set; }
        public Ministry FromMinistry { get; set; } = null!;
        public int ToMinistryId { get; set; }
        public Ministry ToMinistry { get; set; } = null!;
        public MinistryLineageKind Kind { get; set; }
        public DateOnly EffectiveOn { get; set; }
        public string? Note { get; set; }
    }

}

