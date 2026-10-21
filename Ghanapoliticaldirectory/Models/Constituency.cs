namespace Ghanapoliticaldirectory.Models
{
    public class Constituency:AuditedEntity
    {
        public int RegionId { get; set; }
        public Region Region { get; set; } = null!;
        public string Name { get; set; } = "";
        /// <summary>Constituencies are created and split over time; these bound the years it existed.</summary>
        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }
    }
}
