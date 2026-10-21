using Ghanapoliticaldirectory.Domain;

namespace Ghanapoliticaldirectory.Models
{
    public class Source:AuditedEntity
    {

        public string Title { get; set; } = "";
        public string? Url { get; set; }
        public SourceType SourceType { get; set; } = SourceType.Other;
        public DateOnly? PublishedOn { get; set; }
        public DateOnly? LastCheckedOn { get; set; }
        public SourceVerification VerificationStatus { get; set; } = SourceVerification.Unverified;
        public string? Notes { get; set; }
    }


    public class ResultSource
    {
        public int ElectionResultId { get; set; }
        public ElectionResult ElectionResult { get; set; } = null!;
        public int SourceId { get; set; }
        public Source Source { get; set; } = null!;
    }

    public class PersonSource
    {
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
        public int SourceId { get; set; }
        public Source Source { get; set; } = null!;
    }


}
