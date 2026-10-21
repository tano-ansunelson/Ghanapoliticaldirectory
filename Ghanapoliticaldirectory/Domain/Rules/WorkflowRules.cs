//using Ghanapoliticaldirectory.Service;

namespace Ghanapoliticaldirectory.Domain.Rules;

public sealed record WorkflowRecord(WorkflowStage Stage, string? EnteredBy, string? VerifiedBy, string? ApprovedBy);

public sealed record WorkflowDecision(bool Allowed, WorkflowStage? NewStage, string? Reason)
{
    public static WorkflowDecision Allow(WorkflowStage next) => new(true, next, null);
    public static WorkflowDecision Deny(string reason) => new(false, null, reason);
}

/// <summary>
/// The review pipeline: Draft → (Awaiting verification) → Verified → Approved → Published.
/// Each step needs its own permission and, unless switched off for development, a different person
/// from the earlier steps, so one user cannot enter, verify and publish their own data.
/// </summary>
public static class WorkflowRules
{
    public static string RequiredPermission(WorkflowAction action) => action switch
    {
        WorkflowAction.Submit => Permissions.RecordsEnter,
        WorkflowAction.Verify => Permissions.RecordsVerify,
        WorkflowAction.Approve => Permissions.RecordsApprove,
        WorkflowAction.Publish => Permissions.RecordsPublish,
        WorkflowAction.ReturnToDraft => Permissions.RecordsVerify,
        WorkflowAction.Unpublish => Permissions.RecordsPublish,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static WorkflowDecision Evaluate(
        WorkflowRecord record,
        WorkflowAction action,
        string actorId,
        IReadOnlyCollection<string> actorPermissions,
        bool requireDistinctUsers = true)
    {
        if (string.IsNullOrWhiteSpace(actorId))
            return WorkflowDecision.Deny("You must be signed in.");

        if (!actorPermissions.Contains(RequiredPermission(action)))
            return WorkflowDecision.Deny($"You do not have permission to {Describe(action)} records.");

        var stage = record.Stage;
        bool Same(string? other) => other is not null && string.Equals(other, actorId, StringComparison.Ordinal);

        switch (action)
        {
            case WorkflowAction.Submit:
                return stage == WorkflowStage.Draft
                    ? WorkflowDecision.Allow(WorkflowStage.AwaitingVerification)
                    : WorkflowDecision.Deny($"Only a draft can be submitted (this record is {Label(stage)}).");

            case WorkflowAction.Verify:
                if (stage is not (WorkflowStage.Draft or WorkflowStage.AwaitingVerification))
                    return WorkflowDecision.Deny($"Only a draft or a record awaiting verification can be verified (this record is {Label(stage)}).");
                if (requireDistinctUsers && Same(record.EnteredBy))
                    return WorkflowDecision.Deny("The person who entered a record cannot verify it. Ask a different verifier.");
                return WorkflowDecision.Allow(WorkflowStage.Verified);

            case WorkflowAction.Approve:
                if (stage != WorkflowStage.Verified)
                    return WorkflowDecision.Deny($"Only a verified record can be approved (this record is {Label(stage)}).");
                if (requireDistinctUsers && (Same(record.EnteredBy) || Same(record.VerifiedBy)))
                    return WorkflowDecision.Deny("The person who entered or verified a record cannot approve it.");
                return WorkflowDecision.Allow(WorkflowStage.Approved);

            case WorkflowAction.Publish:
                if (stage != WorkflowStage.Approved)
                    return WorkflowDecision.Deny($"Only an approved record can be published (this record is {Label(stage)}).");
                if (requireDistinctUsers && Same(record.EnteredBy))
                    return WorkflowDecision.Deny("The person who entered a record cannot publish it.");
                return WorkflowDecision.Allow(WorkflowStage.Published);

            case WorkflowAction.ReturnToDraft:
                return stage is WorkflowStage.AwaitingVerification or WorkflowStage.Verified or WorkflowStage.Approved
                    ? WorkflowDecision.Allow(WorkflowStage.Draft)
                    : WorkflowDecision.Deny($"A record that is {Label(stage)} cannot be returned to draft.");

            case WorkflowAction.Unpublish:
                return stage == WorkflowStage.Published
                    ? WorkflowDecision.Allow(WorkflowStage.Approved)
                    : WorkflowDecision.Deny("Only a published record can be withdrawn from public view.");

            default:
                return WorkflowDecision.Deny("Unknown action.");
        }
    }

    public static string Label(WorkflowStage stage) => stage switch
    {
        WorkflowStage.Draft => "a draft",
        WorkflowStage.AwaitingVerification => "awaiting verification",
        WorkflowStage.Verified => "verified",
        WorkflowStage.Approved => "approved",
        WorkflowStage.Published => "published",
        _ => stage.ToString()
    };

    private static string Describe(WorkflowAction action) => action switch
    {
        WorkflowAction.Submit => "submit",
        WorkflowAction.Verify => "verify",
        WorkflowAction.Approve => "approve",
        WorkflowAction.Publish => "publish",
        WorkflowAction.ReturnToDraft => "return to draft",
        WorkflowAction.Unpublish => "unpublish",
        _ => action.ToString().ToLowerInvariant()
    };
}
