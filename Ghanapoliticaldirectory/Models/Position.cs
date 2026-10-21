using Ghanapoliticaldirectory.Domain;

namespace Ghanapoliticaldirectory.Models
{
    public class Position:AuditedEntity
    {
        public string Title { get; set; } = "";
        public PositionKind Kind { get; set; }
        public string? Description { get; set; }
        /// <summary>How many people may hold it at once in the same scope (position + ministry). Null = unlimited.</summary>
        public int? MaxConcurrentHolders { get; set; }
        public bool RequiresMinistry { get; set; }
        public bool RequiresAdministration { get; set; }
    }
}
