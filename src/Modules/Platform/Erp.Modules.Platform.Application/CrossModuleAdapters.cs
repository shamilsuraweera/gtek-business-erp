using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Platform.Application;

/// <summary>Implements the posting-period contract on top of Platform accounting periods.</summary>
public sealed class PostingPeriodGuard(IAccountingPeriodStore store, ICompanyContext companyContext) : IPostingPeriodGuard
{
    public async Task EnsurePostingAllowedAsync(DateOnly postingDate, CancellationToken cancellationToken)
    {
        if (!companyContext.HasCompany)
            throw new InvalidOperationException("An active company context is required.");
        var periods = await store.ListAsync(companyContext.CompanyId, cancellationToken);
        var period = periods.FirstOrDefault(x => x.Contains(postingDate))
            ?? throw new DomainException($"No accounting period covers {postingDate:yyyy-MM-dd}.");
        if (period.Status != AccountingPeriodStatus.Open)
            throw new DomainException($"Accounting period '{period.Code}' is {period.Status.ToString().ToLowerInvariant()}; posting is not allowed.");
    }
}

/// <summary>Issues document numbers from Platform number sequences, creating a default sequence on first use.</summary>
public sealed class DocumentNumberGenerator(INumberSequenceService sequences) : IDocumentNumberGenerator
{
    public async Task<string> NextAsync(string sequenceCode, string defaultPrefix, CancellationToken cancellationToken)
    {
        if (await sequences.GetByCodeAsync(sequenceCode, cancellationToken) is null)
        {
            try
            {
                await sequences.CreateAsync(
                    new CreateNumberSequenceRequest(sequenceCode, $"{sequenceCode} numbers", defaultPrefix, null, 1, 5, 1),
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Created concurrently by another request; reserve from the existing sequence.
            }
        }

        return await sequences.GetNextAsync(sequenceCode, cancellationToken);
    }
}

/// <summary>Implements the user directory contract on top of Platform users.</summary>
public sealed class UserDirectory(IUserStore users) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in userIds.Distinct())
            if (await users.GetByIdAsync(new UserId(id), cancellationToken) is { } user) names[id] = user.UserName;
        return names;
    }
}

/// <summary>Bridges business-module audit calls to the Platform append-only audit trail.</summary>
public sealed class AuditRecorder(IAuditTrail audit) : IAuditRecorder
{
    public async Task RecordAsync(
        string category, string action, string entityType, string? entityId, string? entityDisplay,
        bool succeeded, IReadOnlyDictionary<string, object?>? metadata, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(
            category, action, entityType, entityId, entityDisplay,
            succeeded ? AuditOutcome.Succeeded : AuditOutcome.Failed, metadata, cancellationToken);
        await audit.SaveAsync(cancellationToken);
    }
}
