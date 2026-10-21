using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;

namespace Ghanapoliticaldirectory.Business;

/// <summary>Applies an approved workflow step to a record: new stage plus who and when.</summary>
internal static class WorkflowStamp
{
    public static WorkflowRecord Snapshot(IPublishable r) => new(r.Stage, r.CreatedBy, r.VerifiedBy, r.ApprovedBy);

    public static void Apply(IPublishable r, WorkflowAction action, WorkflowStage newStage, string userId, DateTime now, string? notes)
    {
        r.Stage = newStage;
        switch (action)
        {
            case WorkflowAction.Verify:
                r.VerifiedBy = userId; r.VerifiedAtUtc = now;
                break;
            case WorkflowAction.Approve:
                r.ApprovedBy = userId; r.ApprovedAtUtc = now;
                break;
            case WorkflowAction.Publish:
                r.PublishedBy = userId; r.PublishedAtUtc = now;
                break;
            case WorkflowAction.ReturnToDraft:
                r.VerifiedBy = null; r.VerifiedAtUtc = null;
                r.ApprovedBy = null; r.ApprovedAtUtc = null;
                r.PublishedBy = null; r.PublishedAtUtc = null;
                break;
            case WorkflowAction.Unpublish:
                r.PublishedBy = null; r.PublishedAtUtc = null;
                break;
        }
        if (!string.IsNullOrWhiteSpace(notes)) r.ReviewNotes = notes.Trim();
    }

    public static bool NeedsReason(WorkflowAction a) => a is WorkflowAction.ReturnToDraft or WorkflowAction.Unpublish;
}
