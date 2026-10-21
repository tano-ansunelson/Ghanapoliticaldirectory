using Ghanapoliticaldirectory.Domain;

namespace Ghanapoliticaldirectory.Models
{
    public class ParliamentaryTerm:PublishableEntity
    {
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
        public int ConstituencyId { get; set; }
        public Constituency Constituency { get; set; } = null!;
        public int? ElectionCycleId { get; set; }
        public ElectionCycle? ElectionCycle { get; set; }
        /// <summary>The candidacy that won the seat, when recorded.</summary>
        public int? CandidateId { get; set; }
        public Candidate? Candidate { get; set; }
        public TermKind Kind { get; set; } = TermKind.General;
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public int? SourceId { get; set; }
        public Source? Source { get; set; }
        public string? Notes { get; set; }
    }
}
