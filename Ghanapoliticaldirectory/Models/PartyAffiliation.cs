namespace Ghanapoliticaldirectory.Models
{
    public class PartyAffiliation:PublishableEntity
    {

        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
        public int? PartyId { get; set; }
        public PoliticalParty? Party { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public int? SourceId { get; set; }
        public Source? Source { get; set; }
        public string? Notes { get; set; }
    }
}
