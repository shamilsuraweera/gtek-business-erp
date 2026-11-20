using Erp.Application.Abstractions;
using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Platform.Application;

public sealed class AccountingPeriodService(
    IAccountingPeriodStore store,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IClock clock,
    IAuditTrail audit) : IAccountingPeriodService
{
    public async Task<AccountingPeriodResponse> CreateAsync(
        CreateAccountingPeriodRequest request,
        CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        if (await store.GetByCodeAsync(companyId, request.Code, cancellationToken) is not null)
            throw new InvalidOperationException("An accounting period with this code already exists for the company.");
        if (await store.OverlapsAsync(
                companyId,
                request.StartDate,
                request.EndDate,
                null,
                cancellationToken))
            throw new InvalidOperationException("The accounting period overlaps an existing period for the company.");

        var period = AccountingPeriod.Create(
            companyId,
            request.Code,
            request.Name,
            request.StartDate,
            request.EndDate,
            clock.UtcNow,
            currentUser.UserId);
        await store.AddAsync(period, cancellationToken);
        await audit.RecordAsync(
            "CompanyManagement",
            "accounting-period.created",
            "AccountingPeriod",
            period.Id.Value.ToString(),
            period.Code,
            AuditOutcome.Succeeded,
            new Dictionary<string, object?>
            {
                ["StartDate"] = period.StartDate,
                ["EndDate"] = period.EndDate
            },
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(period);
    }

    public async Task<IReadOnlyList<AccountingPeriodResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(RequireCompany(), cancellationToken)).Select(Map).ToArray();

    public async Task<AccountingPeriodResponse?> GetAsync(
        AccountingPeriodId id,
        CancellationToken cancellationToken) =>
        MapNullable(await store.GetAsync(RequireCompany(), id, cancellationToken));

    public async Task<AccountingPeriodResponse?> RenameAsync(
        AccountingPeriodId id,
        RenameAccountingPeriodRequest request,
        CancellationToken cancellationToken)
    {
        var period = await store.GetAsync(RequireCompany(), id, cancellationToken);
        if (period is null)
            return null;

        period.Rename(request.Name, currentUser.UserId, clock.UtcNow);
        await audit.RecordAsync(
            "CompanyManagement",
            "accounting-period.renamed",
            "AccountingPeriod",
            period.Id.Value.ToString(),
            period.Code,
            AuditOutcome.Succeeded,
            new Dictionary<string, object?> { ["Name"] = period.Name },
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(period);
    }

    public async Task<AccountingPeriodResponse?> ChangeStatusAsync(
        AccountingPeriodId id,
        AccountingPeriodStatus status,
        CancellationToken cancellationToken)
    {
        var period = await store.GetAsync(RequireCompany(), id, cancellationToken);
        if (period is null)
            return null;

        switch (status)
        {
            case AccountingPeriodStatus.Open:
                period.Reopen(currentUser.UserId, clock.UtcNow);
                break;
            case AccountingPeriodStatus.Closed:
                period.Close(currentUser.UserId, clock.UtcNow);
                break;
            case AccountingPeriodStatus.Locked:
                period.Lock(currentUser.UserId, clock.UtcNow);
                break;
            default:
                throw new DomainException("Unknown accounting period status.");
        }

        await audit.RecordAsync(
            "CompanyManagement",
            $"accounting-period.{status.ToString().ToLowerInvariant()}",
            "AccountingPeriod",
            period.Id.Value.ToString(),
            period.Code,
            AuditOutcome.Succeeded,
            null,
            cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return Map(period);
    }

    private CompanyId RequireCompany() =>
        companyContext.HasCompany
            ? companyContext.CompanyId
            : throw new InvalidOperationException("An active company context is required.");

    private static AccountingPeriodResponse? MapNullable(AccountingPeriod? period) =>
        period is null
            ? null
            : new(
                period.Id.Value,
                period.CompanyId.Value,
                period.Code,
                period.Name,
                period.StartDate,
                period.EndDate,
                period.Status,
                period.CreatedAt,
                period.ModifiedAt);

    private static AccountingPeriodResponse Map(AccountingPeriod period) => MapNullable(period)!;
}
