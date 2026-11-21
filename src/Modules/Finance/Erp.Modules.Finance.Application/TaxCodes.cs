using Erp.Application.Abstractions;
using Erp.Modules.Finance.Contracts;
using Erp.Modules.Finance.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Finance.Application;

public sealed record TaxCodeResponse(Guid Id, string Code, string Name, decimal RatePercent, bool IsActive);
public sealed record CreateTaxCodeRequest(string Code, string Name, decimal RatePercent);
public sealed record UpdateTaxCodeRequest(string Name, decimal RatePercent);

public interface ITaxCodeStore
{
    Task<IReadOnlyList<TaxCode>> ListAsync(CompanyId companyId, CancellationToken cancellationToken);
    Task<TaxCode?> GetAsync(CompanyId companyId, TaxCodeId id, CancellationToken cancellationToken);
    Task<TaxCode?> GetByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken);
    Task AddAsync(TaxCode taxCode, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ITaxCodeService
{
    Task<TaxCodeResponse> CreateAsync(CreateTaxCodeRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<TaxCodeResponse>> ListAsync(CancellationToken cancellationToken);
    Task<TaxCodeResponse?> UpdateAsync(TaxCodeId id, UpdateTaxCodeRequest request, CancellationToken cancellationToken);
    Task<TaxCodeResponse?> SetActiveAsync(TaxCodeId id, bool active, CancellationToken cancellationToken);
}

/// <summary>Tax codes. Also the cross-module <see cref="ITaxCatalog"/>: Sales and Purchasing read rates through it.</summary>
public sealed class TaxCodeService(ITaxCodeStore store, ICompanyContext companyContext, IClock clock, IAuditRecorder audit)
    : ITaxCodeService, ITaxCatalog
{
    public async Task<TaxCodeResponse> CreateAsync(CreateTaxCodeRequest request, CancellationToken cancellationToken)
    {
        var companyId = RequireCompany();
        if (await store.GetByCodeAsync(companyId, request.Code, cancellationToken) is not null)
            throw new InvalidOperationException("A tax code with this code already exists for the company.");
        var taxCode = TaxCode.Create(companyId, request.Code, request.Name, request.RatePercent, clock.UtcNow);
        await store.AddAsync(taxCode, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Finance", "tax-code.created", "TaxCode", taxCode.Id.Value.ToString(), taxCode.Code, true,
            new Dictionary<string, object?> { ["RatePercent"] = taxCode.RatePercent }, cancellationToken);
        return Map(taxCode);
    }

    public async Task<IReadOnlyList<TaxCodeResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(RequireCompany(), cancellationToken)).Select(Map).ToArray();

    public async Task<TaxCodeResponse?> UpdateAsync(TaxCodeId id, UpdateTaxCodeRequest request, CancellationToken cancellationToken)
    {
        var taxCode = await store.GetAsync(RequireCompany(), id, cancellationToken);
        if (taxCode is null) return null;
        var previous = taxCode.RatePercent;
        taxCode.Update(request.Name, request.RatePercent);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Finance", "tax-code.updated", "TaxCode", taxCode.Id.Value.ToString(), taxCode.Code, true,
            new Dictionary<string, object?> { ["RatePercent"] = taxCode.RatePercent, ["PreviousRatePercent"] = previous }, cancellationToken);
        return Map(taxCode);
    }

    public async Task<TaxCodeResponse?> SetActiveAsync(TaxCodeId id, bool active, CancellationToken cancellationToken)
    {
        var taxCode = await store.GetAsync(RequireCompany(), id, cancellationToken);
        if (taxCode is null) return null;
        if (active) taxCode.Activate(); else taxCode.Deactivate();
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Finance", active ? "tax-code.activated" : "tax-code.deactivated", "TaxCode",
            taxCode.Id.Value.ToString(), taxCode.Code, true, null, cancellationToken);
        return Map(taxCode);
    }

    public async Task<IReadOnlyDictionary<string, TaxCodeInfo>> GetAsync(IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        var wanted = codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(TaxCode.NormalizeCode).ToHashSet();
        var result = new Dictionary<string, TaxCodeInfo>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return result;
        foreach (var t in await store.ListAsync(RequireCompany(), cancellationToken))
            if (wanted.Contains(t.Code)) result[t.Code] = new TaxCodeInfo(t.Code, t.Name, t.RatePercent, t.IsActive);
        return result;
    }

    private CompanyId RequireCompany() =>
        companyContext.HasCompany ? companyContext.CompanyId : throw new InvalidOperationException("An active company context is required.");

    private static TaxCodeResponse Map(TaxCode t) => new(t.Id.Value, t.Code, t.Name, t.RatePercent, t.IsActive);
}
