namespace Ghanapoliticaldirectory.Domain
{

    /// <summary>Injected so that business rules and tests never read the system clock directly.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
        DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }


    public enum IssueSeverity { Warning = 0, Error = 1 }

    /// <summary>One finding from a business rule. Errors block a save; warnings are shown but do not.</summary>
    public sealed record ValidationIssue(string Code, string Message, IssueSeverity Severity = IssueSeverity.Error, string? Field = null);

    public sealed class ValidationReport
    {
        private readonly List<ValidationIssue> _issues = new();

        public IReadOnlyList<ValidationIssue> Issues => _issues;
        public IEnumerable<ValidationIssue> Errors => _issues.Where(i => i.Severity == IssueSeverity.Error);
        public IEnumerable<ValidationIssue> Warnings => _issues.Where(i => i.Severity == IssueSeverity.Warning);
        public bool IsValid => !Errors.Any();

        public ValidationReport Error(string code, string message, string? field = null)
        {
            _issues.Add(new ValidationIssue(code, message, IssueSeverity.Error, field));
            return this;
        }

        public ValidationReport Warn(string code, string message, string? field = null)
        {
            _issues.Add(new ValidationIssue(code, message, IssueSeverity.Warning, field));
            return this;
        }

        public ValidationReport AddRange(ValidationReport other)
        {
            _issues.AddRange(other._issues);
            return this;
        }

        public bool Has(string code) => _issues.Any(i => i.Code == code);
    }

    /// <summary>
    /// A period with an optional open end (still ongoing).
    /// For overlap tests the end date is treated as the hand-over day: a record that ends on 7 January
    /// and another that starts on 7 January do NOT overlap, because real appointments are often handed
    /// over on a single date. Two records that start on the same day always overlap.
    /// </summary>
    public readonly record struct DateRange(DateOnly Start, DateOnly? End)
    {
        public bool IsValid => End is null || End >= Start;

        public bool Contains(DateOnly date) => date >= Start && (End is null || date <= End);

        public bool Overlaps(DateRange other)
        {
            if (Start == other.Start) return true;
            return Start < (other.End ?? DateOnly.MaxValue) && other.Start < (End ?? DateOnly.MaxValue);
        }

        public override string ToString() => $"{Start:yyyy-MM-dd} to {(End is null ? "present" : End.Value.ToString("yyyy-MM-dd"))}";
    }
    public class Common
    {
    }
}
