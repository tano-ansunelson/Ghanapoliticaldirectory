namespace Ghanapoliticaldirectory.Domain.Rules;

/// <summary>Baseline behaviour of each kind of office. Used to seed the Positions table and to pre-validate forms.</summary>
public static class PositionDefaults
{
    public sealed record Rule(string Title, int? MaxConcurrentHolders, bool RequiresMinistry, bool RequiresAdministration);

    public static Rule For(PositionKind kind) => kind switch
    {
        PositionKind.President => new("President", 1, false, true),
        PositionKind.VicePresident => new("Vice President", 1, false, true),
        PositionKind.MemberOfParliament => new("Member of Parliament", 1, false, false),
        PositionKind.Minister => new("Minister", 1, true, true),
        PositionKind.DeputyMinister => new("Deputy Minister", null, true, true),
        PositionKind.ChiefOfStaff => new("Chief of Staff", 1, false, true),
        PositionKind.Speaker => new("Speaker of Parliament", 1, false, false),
        _ => new("Other public office", null, false, false)
    };
}

public sealed record PositionRule(int PositionId, PositionKind Kind, string Title, int? MaxConcurrentHolders, bool RequiresMinistry, bool RequiresAdministration);

public sealed record AppointmentCandidate(
    int PersonId,
    PositionRule Position,
    int? MinistryId,
    int? AdministrationId,
    DateOnly? Start,
    DateOnly? End,
    AppointmentStatus Status);

/// <summary>Someone already holding the same office in the same scope (same position and ministry).</summary>
public sealed record ExistingHolding(int RecordId, int PersonId, DateRange Range);

public sealed record AdministrationPeriod(int Id, DateRange Range);

public static class AppointmentRules
{
    /// <param name="sameScope">Existing appointments for the same position and ministry (caller filters the scope).</param>
    /// <param name="ignoreRecordId">The record being edited, so it is not compared with itself.</param>
    public static ValidationReport Validate(
        AppointmentCandidate c,
        IReadOnlyList<ExistingHolding> sameScope,
        AdministrationPeriod? administration,
        DateOnly today,
        int? ignoreRecordId = null)
    {
        var r = new ValidationReport();
        var title = c.Position.Title;

        if (c.Start is null)
        {
            r.Error("appointment.start_required", $"{title}: enter a start date.", "Start");
            return r;
        }

        var range = new DateRange(c.Start.Value, c.End);
        if (!range.IsValid)
            r.Error("appointment.end_before_start", $"{title}: the end date cannot be before the start date.", "End");

        if (c.Position.RequiresMinistry && c.MinistryId is null)
            r.Error("appointment.ministry_required", $"{title}: select the ministry.", "MinistryId");

        if (!c.Position.RequiresMinistry && c.MinistryId is not null)
            r.Error("appointment.ministry_not_allowed", $"{title} does not belong to a ministry.", "MinistryId");

        if (c.Position.RequiresAdministration && c.AdministrationId is null)
            r.Error("appointment.administration_required", $"{title}: select the administration.", "AdministrationId");

        if (c.Status == AppointmentStatus.Ended && c.End is null)
            r.Error("appointment.ended_needs_end", $"{title}: an ended appointment needs an end date.", "End");

        if (c.Status is AppointmentStatus.Active or AppointmentStatus.Acting && c.End is not null && c.End < today)
            r.Warn("appointment.status_inconsistent", $"{title}: the end date is in the past but the status is not 'Ended'.", "Status");

        if (!r.IsValid) return r;

        // Offices need not start or end with the administration: only flag clear contradictions.
        if (administration is not null)
        {
            var adminEnd = administration.Range.End;
            if (adminEnd is not null && range.Start > adminEnd)
                r.Error("appointment.after_administration", $"{title} starts after the selected administration ended.", "Start");
            else if (range.End is not null && range.End < administration.Range.Start)
                r.Error("appointment.before_administration", $"{title} ends before the selected administration began.", "End");
            else if (range.Start < administration.Range.Start)
                r.Warn("appointment.starts_before_administration", $"{title} starts before the administration began. Check the date.", "Start");
        }

        var overlapping = sameScope
            .Where(e => e.RecordId != ignoreRecordId && e.Range.Overlaps(range))
            .ToList();

        if (c.PersonId != 0 && overlapping.Any(e => e.PersonId == c.PersonId))
            r.Error("appointment.duplicate", $"This person already holds {title} for an overlapping period.", "Start");
        else if (c.Position.MaxConcurrentHolders is int max && overlapping.Count(e => e.PersonId != c.PersonId) >= max)
            r.Error("appointment.capacity", $"{title} is already held by someone else for part of this period. End that record first, or check the dates.", "Start");

        return r;
    }
}

public sealed record TermHolding(int RecordId, int PersonId, int ConstituencyId, DateRange Range);

public static class ParliamentaryTermRules
{
    /// <summary>A constituency has one MP at a time, and a person sits for one constituency at a time.</summary>
    public static ValidationReport Validate(
        int personId,
        int constituencyId,
        DateOnly? start,
        DateOnly? end,
        IReadOnlyList<TermHolding> existingInConstituency,
        IReadOnlyList<TermHolding> existingForPerson,
        int? ignoreRecordId = null)
    {
        var r = new ValidationReport();
        if (start is null)
        {
            r.Error("term.start_required", "Member of Parliament: enter a start date.", "Start");
            return r;
        }

        var range = new DateRange(start.Value, end);
        if (!range.IsValid)
        {
            r.Error("term.end_before_start", "Member of Parliament: the end date cannot be before the start date.", "End");
            return r;
        }

        if (existingForPerson.Any(t => t.RecordId != ignoreRecordId && t.ConstituencyId == constituencyId && t.Range.Overlaps(range)))
            r.Error("term.duplicate", "This person already has a parliamentary term for this constituency in an overlapping period.", "Start");
        else if (existingForPerson.Any(t => t.RecordId != ignoreRecordId && t.ConstituencyId != constituencyId && t.Range.Overlaps(range)))
            r.Error("term.two_seats", "This person already sits for another constituency in an overlapping period.", "Start");

        if (existingInConstituency.Any(t => t.RecordId != ignoreRecordId && t.PersonId != personId && t.Range.Overlaps(range)))
            r.Error("term.seat_taken", "Another Member of Parliament is already recorded for this constituency in an overlapping period.", "Start");

        return r;
    }
}

public sealed record AffiliationPeriod(int RecordId, int? PartyId, DateRange Range);

/// <summary>Result of looking up someone's party on a date: unknown, independent, or a specific party.</summary>
public readonly record struct PartyOnDate(bool IsKnown, int? PartyId)
{
    public static PartyOnDate Unknown => new(false, null);
    public bool IsIndependent => IsKnown && PartyId is null;
}

public static class PartyAffiliationRules
{
    public static ValidationReport Validate(DateRange range, IReadOnlyList<AffiliationPeriod> existing, int? ignoreRecordId = null)
    {
        var r = new ValidationReport();
        if (!range.IsValid)
        {
            r.Error("affiliation.end_before_start", "The party affiliation end date cannot be before its start date.", "End");
            return r;
        }
        if (existing.Any(a => a.RecordId != ignoreRecordId && a.Range.Overlaps(range)))
            r.Error("affiliation.overlap", "This person already has a party affiliation for an overlapping period. End the earlier one first.", "Start");
        return r;
    }

    /// <summary>
    /// The party on a given date, taken only from dated records. Today's party is never assumed for the past:
    /// with no record covering the date the answer is Unknown.
    /// </summary>
    public static PartyOnDate PartyAt(DateOnly date, IEnumerable<AffiliationPeriod> affiliations)
    {
        var match = affiliations.Where(a => a.Range.Contains(date)).OrderByDescending(a => a.Range.Start).FirstOrDefault();
        return match is null ? PartyOnDate.Unknown : new PartyOnDate(true, match.PartyId);
    }
}

public sealed record OpenAppointment(int RecordId, int PersonId, int? AdministrationId, DateOnly Start, string Title);
public sealed record PlannedClosure(int RecordId, DateOnly EndDate, string Reason);

public static class GovernmentChangePlanner
{
    /// <summary>
    /// When an administration ends, close the open appointments that belong to it, on the date the administrator
    /// states. Appointments tied to no administration (for example MPs or the Speaker) are left alone, and so are
    /// appointments of other administrations. Nothing is deleted.
    /// </summary>
    public static (IReadOnlyList<PlannedClosure> Closures, ValidationReport Report) Plan(
        int outgoingAdministrationId,
        DateOnly endDate,
        IEnumerable<OpenAppointment> openAppointments)
    {
        var report = new ValidationReport();
        var closures = new List<PlannedClosure>();

        foreach (var a in openAppointments.Where(a => a.AdministrationId == outgoingAdministrationId))
        {
            if (endDate < a.Start)
            {
                report.Error("change.end_before_start", $"{a.Title} started on {a.Start:d MMM yyyy}, after the chosen end date.", "EndDate");
                continue;
            }
            closures.Add(new PlannedClosure(a.RecordId, endDate, "Administration ended"));
        }
        return (closures, report);
    }
}
