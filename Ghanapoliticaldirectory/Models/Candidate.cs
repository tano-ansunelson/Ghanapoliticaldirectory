using Ghanapoliticaldirectory.Domain;

namespace Ghanapoliticaldirectory.Models
{
    public class Candidate:PublishableEntity
    {

        public int ElectionContestId { get; set; }
        public ElectionContest ElectionContest { get; set; } = null!;
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
        /// <summary>Null = stood as an independent.</summary>
        public int? PartyId { get; set; }
        public PoliticalParty? Party { get; set; }
        public int? RunningMatePersonId { get; set; }
        public Person? RunningMate { get; set; }
        public CandidateStatus Status { get; set; } = CandidateStatus.Contested;
        public int? BallotNumber { get; set; }
    }

    public class CandidateResult : Entity
    {
        public int ElectionResultId { get; set; }
        public ElectionResult ElectionResult { get; set; } = null!;
        public int CandidateId { get; set; }
        public Candidate Candidate { get; set; } = null!;
        public long? Votes { get; set; }
        /// <summary>Set only from an official declaration. The system never calculates a winner.</summary>
        public bool IsDeclaredWinner { get; set; }
    }


}
