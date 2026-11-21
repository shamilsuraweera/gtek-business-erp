using Erp.Application.Abstractions;
using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Purchasing.Application;

internal static class PurchasingCompany
{
    public static CompanyId Require(ICompanyContext context) =>
        context.HasCompany ? context.CompanyId : throw new InvalidOperationException("An active company context is required.");
}

public sealed class VendorService(IPurchasingStore store, ICompanyContext companyContext, IClock clock, IAuditRecorder audit) : IVendorService
{
    public async Task<VendorResponse> CreateAsync(CreateVendorRequest request, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        if (await store.GetVendorByCodeAsync(companyId, request.Code, cancellationToken) is not null)
            throw new InvalidOperationException("A vendor with this code already exists for the company.");
        var vendor = Vendor.Create(companyId, request.Code, request.Name, request.Email, request.Phone,
            request.PaymentTermsDays ?? 30, clock.UtcNow, request.PriceTolerancePercent, request.QuantityTolerancePercent, request.UnderDeliveryTolerancePercent);
        await store.AddVendorAsync(vendor, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Purchasing", "vendor.created", "Vendor", vendor.Id.Value.ToString(), vendor.Code, true, null, cancellationToken);
        return Map(vendor);
    }

    public async Task<IReadOnlyList<VendorResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListVendorsAsync(PurchasingCompany.Require(companyContext), cancellationToken)).Select(Map).ToArray();

    public async Task<VendorResponse?> GetAsync(VendorId id, CancellationToken cancellationToken)
    {
        var vendor = await store.GetVendorAsync(PurchasingCompany.Require(companyContext), id, cancellationToken);
        return vendor is null ? null : Map(vendor);
    }

    public async Task<VendorResponse?> UpdateAsync(VendorId id, UpdateVendorRequest request, CancellationToken cancellationToken)
    {
        var vendor = await store.GetVendorAsync(PurchasingCompany.Require(companyContext), id, cancellationToken);
        if (vendor is null) return null;
        vendor.Update(request.Name, request.Email, request.Phone, request.PaymentTermsDays ?? vendor.PaymentTermsDays, request.PriceTolerancePercent, request.QuantityTolerancePercent, request.UnderDeliveryTolerancePercent);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Purchasing", "vendor.updated", "Vendor", vendor.Id.Value.ToString(), vendor.Code, true, null, cancellationToken);
        return Map(vendor);
    }

    public async Task<VendorResponse?> SetActiveAsync(VendorId id, bool active, CancellationToken cancellationToken)
    {
        var vendor = await store.GetVendorAsync(PurchasingCompany.Require(companyContext), id, cancellationToken);
        if (vendor is null) return null;
        if (active) vendor.Activate(); else vendor.Deactivate();
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Purchasing", active ? "vendor.activated" : "vendor.deactivated", "Vendor",
            vendor.Id.Value.ToString(), vendor.Code, true, null, cancellationToken);
        return Map(vendor);
    }

    private static VendorResponse Map(Vendor v) => new(v.Id.Value, v.Code, v.Name, v.Email, v.Phone, v.PaymentTermsDays, v.IsActive, v.PriceTolerancePercent, v.QuantityTolerancePercent, v.UnderDeliveryTolerancePercent);
}
