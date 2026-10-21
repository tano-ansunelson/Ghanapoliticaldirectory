namespace Ghanapoliticaldirectory.Models
{
    public class Person: PublishableEntity
    {
        public string FullName { get; set; } = "";
        /// <summary>Lower-case, accent-free, honorific-free form used only for duplicate detection and search.</summary>
        public string NormalizedName { get; set; } = "";
        public string? Biography { get; set; }
        public string? AdditionalInfo { get; set; }

        public string? PhotoUrl { get; set; }
        public string? PhotoAltText { get; set; }
        public string? PhotoContentType { get; set; }
        public long? PhotoSizeBytes { get; set; }

        public ICollection<PartyAffiliation> Affiliations { get; set; } = new List<PartyAffiliation>();
        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
        public ICollection<ParliamentaryTerm> ParliamentaryTerms { get; set; } = new List<ParliamentaryTerm>();
        public ICollection<Candidate> Candidacies { get; set; } = new List<Candidate>();
        public ICollection<PersonSource> Sources { get; set; } = new List<PersonSource>();

    }
}
