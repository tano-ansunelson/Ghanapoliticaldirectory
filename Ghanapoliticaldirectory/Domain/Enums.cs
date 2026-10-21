namespace Ghanapoliticaldirectory.Domain
{

    /// <summary>
    /// Where a record is in the review pipeline. Public pages only ever read <see cref="Published"/> records.
    /// Data entry, verification, approval and publication are separate steps (separate permissions, and
    /// by default separate people).
    /// </summary>
    public enum WorkflowStage
    {
        Draft = 0,
        AwaitingVerification = 1,
        Verified = 2,
        Approved = 3,
        Published = 4
    }

    public enum WorkflowAction
    {
        Submit = 0,
        Verify = 1,
        Approve = 2,
        Publish = 3,
        ReturnToDraft = 4,
        Unpublish = 5
    }


    public enum ContestType { Presidential = 0, Parliamentary = 1 }

    /// <summary>
    /// What the source document says about the figures at the time they were entered.
    /// This is NOT the review stage: it is the official standing of the numbers.
    /// </summary>
    public enum ResultBasis
    {
        /// <summary>Early or provisional figures announced by the Electoral Commission.</summary>
        Provisional = 0,
        /// <summary>Figures checked against the official publication; formal declaration not recorded.</summary>
        Verified = 1,
        /// <summary>Formally declared by the Electoral Commission.</summary>
        OfficiallyDeclared = 2
    }

    /// <summary>The label shown to the public next to a published result.</summary>
    public enum ResultLabel
    {
        Provisional = 0,
        Verified = 1,
        OfficiallyDeclared = 2,
        Corrected = 3
    }
    public class Enums
    {
    }


    public enum CandidateStatus { Nominated = 0, Contested = 1, Withdrawn = 2, Disqualified = 3 }

    public enum PositionKind
    {
        President = 0,
        VicePresident = 1,
        MemberOfParliament = 2,
        Minister = 3,
        DeputyMinister = 4,
        ChiefOfStaff = 5,
        Speaker = 6,
        Other = 7
    }

    public enum AppointmentStatus { Active = 0, Acting = 1, Ended = 2, Unconfirmed = 3 }

    public enum TermKind { General = 0, ByElection = 1 }

    public enum SourceType { ElectoralCommission = 0, Parliament = 1, Government = 2, Ministry = 3, Other = 4 }

    public enum SourceVerification { Unverified = 0, Verified = 1, Disputed = 2 }

    public enum MinistryLineageKind { Renamed = 0, Merged = 1, Split = 2, Reorganised = 3 }

}
