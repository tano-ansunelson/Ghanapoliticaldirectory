namespace Ghanapoliticaldirectory.Models
{
    public class Administration:PublishableEntity
    {

        public string Name { get; set; } = "";
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        /// <summary>The election that produced it. Optional, so transitional governments can be recorded.</summary>
        public int? ElectionCycleId { get; set; }
        public ElectionCycle? ElectionCycle { get; set; }
        public string? Description { get; set; }
        /// <summary>False for planned or draft administrations, which are never shown as historical fact.</summary>
        public bool IsConfirmed { get; set; }
    }
}
