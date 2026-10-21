namespace Ghanapoliticaldirectory.Models
{
    public class Region:AuditedEntity
    {
        public string Name { get; set; } = "";
        public string? Code { get; set; }
        public ICollection<Constituency> Constituencies { get; set; } = new List<Constituency>();
    }
}
