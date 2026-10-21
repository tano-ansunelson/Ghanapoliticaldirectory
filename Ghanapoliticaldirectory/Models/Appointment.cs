using Ghanapoliticaldirectory.Domain;

namespace Ghanapoliticaldirectory.Models
{
    public class Appointment:PublishableEntity
    {
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
        public int PositionId { get; set; }
        public Position Position { get; set; } = null!;
        public int? AdministrationId { get; set; }
        public Administration? Administration { get; set; }
        /// <summary>Null for offices that do not belong to a ministry.</summary>
        public int? MinistryId { get; set; }
        public Ministry? Ministry { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public AppointmentStatus Status { get; set; } = AppointmentStatus.Active;
        public string? EndReason { get; set; }
        public int? SourceId { get; set; }
        public Source? Source { get; set; }
        /// <summary>Chosen by an administrator to appear on the homepage's current-government section.</summary>
        public bool IsFeatured { get; set; }
        public string? Notes { get; set; }
    }
}
