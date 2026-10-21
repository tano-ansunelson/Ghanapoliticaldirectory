using Ghanapoliticaldirectory.Domain;

namespace Ghanapoliticaldirectory.Models
{
    public class ElectionCycle:PublishableEntity
    {
        public int Year { get; set; }
        public string Name { get; set; } = "";
        public DateOnly? ElectionDate { get; set; }
        /// <summary>False while the date is only planned. Never present a planned date as a confirmed fact.</summary>
        public bool IsDateConfirmed { get; set; }
        public string? Notes { get; set; }
    }

    public class ElectionContest : AuditedEntity
    {
        public int ElectionCycleId { get; set; }
        public ElectionCycle ElectionCycle { get; set; } = null!;
        public ContestType ContestType { get; set; }
        /// <summary>Null for presidential contests, required for parliamentary ones.</summary>
        public int? ConstituencyId { get; set; }
        public Constituency? Constituency { get; set; }
        /// <summary>1 for the first round; 2 for a run-off.</summary>
        public int Round { get; set; } = 1;

        public ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();
        public ElectionResult? Result { get; set; }
    }

    public class ElectionResult : PublishableEntity
    {
        public int ElectionContestId { get; set; }
        public ElectionContest ElectionContest { get; set; } = null!;
        public ResultBasis Basis { get; set; } = ResultBasis.Provisional;
        public long? ValidVotes { get; set; }
        public long? RejectedBallots { get; set; }
        public long? TotalVotesCast { get; set; }
        public long? RegisteredVoters { get; set; }
        public DateOnly? DeclaredOn { get; set; }
        public bool HasBeenCorrected { get; set; }
        public string? CorrectionNote { get; set; }
        public string? VerificationNotes { get; set; }

        public ICollection<CandidateResult> Lines { get; set; } = new List<CandidateResult>();
        public ICollection<ResultSource> Sources { get; set; } = new List<ResultSource>();
    }
}


