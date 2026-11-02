using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Platform.Application;

public sealed class NumberSequenceService(
    INumberSequenceStore store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditTrail audit) : INumberSequenceService
{
    public async Task<NumberSequenceResponse> CreateAsync(CreateNumberSequenceRequest request, CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        if (await store.GetByCodeAsync(companyId, request.Code, cancellationToken) is not null)
            throw new InvalidOperationException("A number sequence with this code already exists for the company.");

        var sequence = NumberSequence.Create(companyId, request.Code, request.Name, request.Prefix, request.Suffix,
            request.NextValue, request.Padding, request.Increment, clock.UtcNow, currentUser.UserId);
        await store.AddAsync(sequence, cancellationToken);
        await audit.RecordAsync("CompanyManagement", "number-sequence.created", "NumberSequence",
            sequence.Id.Value.ToString(), sequence.Code, AuditOutcome.Succeeded,
            Metadata(sequence), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(sequence);
    }

    public async Task<IReadOnlyList<NumberSequenceResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(RequireCompany(), cancellationToken)).Select(Map).ToArray();

    public async Task<NumberSequenceResponse?> GetAsync(NumberSequenceId id, CancellationToken cancellationToken) =>
        MapNullable(await store.GetAsync(RequireCompany(), id, cancellationToken));

    public async Task<NumberSequenceResponse?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        MapNullable(await store.GetByCodeAsync(RequireCompany(), code, cancellationToken));

    public async Task<NumberSequenceResponse?> RenameAsync(NumberSequenceId id, RenameNumberSequenceRequest request, CancellationToken cancellationToken)
    {
        var sequence = await store.GetAsync(RequireCompany(), id, cancellationToken);
        if (sequence is null) return null;
        var previous = sequence.Name;
        sequence.Rename(request.Name, currentUser.UserId, clock.UtcNow);
        await audit.RecordAsync("CompanyManagement", "number-sequence.renamed", "NumberSequence",
            sequence.Id.Value.ToString(), sequence.Code, AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["PreviousName"] = previous, ["NewName"] = sequence.Name }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(sequence);
    }

    public async Task<NumberSequenceResponse?> ConfigureAsync(NumberSequenceId id, ConfigureNumberSequenceRequest request, CancellationToken cancellationToken)
    {
        var sequence = await store.GetAsync(RequireCompany(), id, cancellationToken);
        if (sequence is null) return null;
        var metadata = new Dictionary<string, object?> { ["Prefix"] = request.Prefix, ["Suffix"] = request.Suffix, ["Padding"] = request.Padding, ["Increment"] = request.Increment };
        sequence.ChangeConfiguration(request.Prefix, request.Suffix, request.Padding, request.Increment, currentUser.UserId, clock.UtcNow);
        await audit.RecordAsync("CompanyManagement", "number-sequence.configuration-changed", "NumberSequence",
            sequence.Id.Value.ToString(), sequence.Code, AuditOutcome.Succeeded, metadata, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(sequence);
    }

    public async Task<NumberSequenceResponse?> SetStatusAsync(NumberSequenceId id, bool active, CancellationToken cancellationToken)
    {
        var sequence = await store.GetAsync(RequireCompany(), id, cancellationToken);
        if (sequence is null) return null;
        if (active) sequence.Activate(currentUser.UserId, clock.UtcNow); else sequence.Deactivate(currentUser.UserId, clock.UtcNow);
        await audit.RecordAsync("CompanyManagement", active ? "number-sequence.activated" : "number-sequence.deactivated",
            "NumberSequence", sequence.Id.Value.ToString(), sequence.Code, AuditOutcome.Succeeded, null, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(sequence);
    }

    public Task<string> GetNextAsync(string code, CancellationToken cancellationToken) =>
        store.ReserveNextAsync(RequireCompany(), code, cancellationToken);

    private CompanyId RequireCompany() =>
        companyContext.HasCompany ? companyContext.CompanyId : throw new InvalidOperationException("An active company context is required.");

    private static IReadOnlyDictionary<string, object?> Metadata(NumberSequence sequence) =>
        new Dictionary<string, object?> { ["SequenceCode"] = sequence.Code, ["Prefix"] = sequence.Prefix, ["Suffix"] = sequence.Suffix, ["Padding"] = sequence.Padding, ["Increment"] = sequence.Increment };

    private static NumberSequenceResponse? MapNullable(NumberSequence? sequence) => sequence is null ? null :
        new(sequence.Id.Value, sequence.CompanyId.Value, sequence.Code, sequence.Name, sequence.Prefix, sequence.Suffix,
            sequence.NextValue, sequence.Padding, sequence.Increment, sequence.Status, sequence.CreatedAt, sequence.ModifiedAt);
    private static NumberSequenceResponse Map(NumberSequence sequence) => MapNullable(sequence)!;
}
