namespace Ghanapoliticaldirectory.Domain.Rules;

public sealed record ResultLineInput(string Key, string Name, long? Votes, bool IsDeclaredWinner);

public sealed record ResultInput(
    ContestType ContestType,
    int Round,
    ResultBasis Basis,
    IReadOnlyList<ResultLineInput> Lines,
    long? ValidVotes,
    long? RejectedBallots,
    long? TotalVotesCast,
    long? RegisteredVoters,
    bool HasSource);

public enum ValidationMode
{
    /// <summary>Saving a draft: only clear mistakes are errors, gaps are allowed.</summary>
    Draft = 0,
    /// <summary>Marking verified: every figure and the source must be present and consistent.</summary>
    Verification = 1
}

public static class ResultMath
{
    /// <summary>Total used as the denominator: the officially reported valid votes, else the sum of every line.</summary>
    public static long? Denominator(long? validVotes, IReadOnlyList<ResultLineInput> lines)
    {
        if (validVotes is not null) return validVotes;
        return lines.Count > 0 && lines.All(l => l.Votes is not null) ? lines.Sum(l => l.Votes!.Value) : null;
    }

    public static decimal? Percentage(long? votes, long? total)
    {
        if (votes is null || total is null || total <= 0) return null;
        return Math.Round(votes.Value * 100m / total.Value, 2, MidpointRounding.AwayFromZero);
    }
}

public static class ResultRules
{
    public static ValidationReport Validate(ResultInput input, ValidationMode mode)
    {
        var report = new ValidationReport();
        var lines = input.Lines;

        if (lines.Count < 1)
            report.Error("result.min_candidates", "Add at least one candidate before entering results.", "Lines");
        else if (lines.Count == 1)
            report.Warn("result.single_candidate", "Only one candidate is listed. This is only correct for an unopposed seat.", "Lines");

        var duplicate = lines.GroupBy(l => l.Key).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            report.Error("result.duplicate_candidate", $"\"{duplicate.First().Name}\" appears more than once in this contest.", "Lines");

        foreach (var l in lines.Where(l => l.Votes is < 0))
            report.Error("result.negative_votes", $"Votes for {l.Name} cannot be negative.", "Lines");

        foreach (var (field, value, label) in new (string, long?, string)[]
        {
            ("ValidVotes", input.ValidVotes, "Valid votes"),
            ("RejectedBallots", input.RejectedBallots, "Rejected ballots"),
            ("TotalVotesCast", input.TotalVotesCast, "Total votes cast"),
            ("RegisteredVoters", input.RegisteredVoters, "Registered voters")
        })
        {
            if (value is < 0) report.Error("result.negative_total", $"{label} cannot be negative.", field);
        }

        var allVotesPresent = lines.Count > 0 && lines.All(l => l.Votes is not null);

        if (mode == ValidationMode.Verification)
        {
            foreach (var l in lines.Where(l => l.Votes is null))
                report.Error("result.missing_votes", $"Enter the votes received by {l.Name}.", "Lines");
            if (!input.HasSource)
                report.Error("result.source_required", "Record the source document or official URL before verifying a result.", "SourceTitle");
        }

        if (allVotesPresent && input.ValidVotes is not null && lines.Sum(l => l.Votes!.Value) != input.ValidVotes)
            report.Error("result.sum_mismatch", "Candidate votes do not add up to the valid votes entered.", "ValidVotes");

        if (input.ValidVotes is not null && input.RejectedBallots is not null && input.TotalVotesCast is not null
            && input.ValidVotes + input.RejectedBallots != input.TotalVotesCast)
            report.Error("result.cast_mismatch", "Valid votes plus rejected ballots do not equal total votes cast.", "TotalVotesCast");

        if (input.ValidVotes is not null && input.TotalVotesCast is not null && input.ValidVotes > input.TotalVotesCast)
            report.Error("result.valid_exceeds_cast", "Valid votes cannot be more than the total votes cast.", "ValidVotes");

        if (input.TotalVotesCast is not null && input.RegisteredVoters is not null && input.TotalVotesCast > input.RegisteredVoters)
            report.Error("result.cast_exceeds_registered", "Total votes cast cannot be more than the registered voters.", "TotalVotesCast");

        // ---- declared winner: always taken from the official declaration, never calculated
        var flagged = lines.Where(l => l.IsDeclaredWinner).ToList();
        if (flagged.Count > 1)
            report.Error("result.multiple_winners", "Only one candidate can be marked as the declared winner.", "Lines");

        if (flagged.Count >= 1 && input.Basis != ResultBasis.OfficiallyDeclared)
            report.Error("result.winner_needs_declaration",
                "A winner can only be marked when the source is an official declaration by the Electoral Commission.", "Lines");

        if (flagged.Count == 1 && allVotesPresent)
        {
            var winner = flagged[0];
            if (lines.Any(l => l.Votes > winner.Votes))
                report.Warn("result.winner_not_top", $"{winner.Name} is marked as the winner but another candidate has more votes. Check the source.", "Lines");

            var total = ResultMath.Denominator(input.ValidVotes, lines);
            if (input.ContestType == ContestType.Presidential && input.Round == 1 && total is > 0 && winner.Votes * 2 <= total)
                report.Warn("result.runoff_expected", $"{winner.Name} has no more than half of the valid votes. Confirm whether a run-off was held.", "Lines");
        }

        return report;
    }
}

/// <summary>What the public sees for a result. Only published results are visible at all.</summary>
public static class ResultLabelPolicy
{
    public static ResultLabel? PublicLabel(WorkflowStage stage, ResultBasis basis, bool corrected)
    {
        if (stage != WorkflowStage.Published) return null;
        if (corrected) return ResultLabel.Corrected;
        return basis switch
        {
            ResultBasis.OfficiallyDeclared => ResultLabel.OfficiallyDeclared,
            ResultBasis.Verified => ResultLabel.Verified,
            _ => ResultLabel.Provisional
        };
    }

    /// <summary>Percentages are shown only when the figures are not merely provisional.</summary>
    public static bool ShowPercentages(ResultLabel? label, ResultBasis basis) =>
        label is not null && label != ResultLabel.Provisional && basis != ResultBasis.Provisional;

    /// <summary>A winner is shown only for a published result whose source is an official declaration.</summary>
    public static bool ShowWinner(ResultLabel? label, ResultBasis basis, bool flagged) =>
        flagged && label is not null && basis == ResultBasis.OfficiallyDeclared;

    /// <summary>Overall label for a group of results (for example every constituency in one election): the weakest one.</summary>
    public static ResultLabel? Weakest(IEnumerable<ResultLabel> labels)
    {
        ResultLabel? weakest = null;
        var weakestRank = int.MaxValue;
        foreach (var l in labels)
        {
            var rank = Rank(l);
            if (rank < weakestRank) { weakestRank = rank; weakest = l; }
        }
        return weakest;
    }

    private static int Rank(ResultLabel l) => l switch
    {
        ResultLabel.Provisional => 1,
        ResultLabel.Verified => 2,
        _ => 3
    };
}
