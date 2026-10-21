namespace Ghanapoliticaldirectory.Models
{
    public class PoliticalParty:AuditedEntity
    {

        public string Name { get; set; } = "";
        public string Abbreviation { get; set; } = "";
        public string? Description { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? LogoUrl { get; set; }
        public string? LogoAltText { get; set; }
        public DateOnly? FoundedOn { get; set; }
        public DateOnly? DissolvedOn { get; set; }
    }
}
