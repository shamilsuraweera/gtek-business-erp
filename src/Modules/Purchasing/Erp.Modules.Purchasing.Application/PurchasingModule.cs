using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Purchasing.Application;

/// <summary>GL account codes used by purchasing postings. MVP: fixed mapping (seeded by the dev seeder).</summary>
public static class PurchasingAccounts
{
    public const string Cash = "1000";
    public const string Bank = "1010";
    public const string Inventory = "1200";
    public const string Payable = "2000";
    /// <summary>Purchase price variance: the difference between the billed price and the order price (set when a bill is matched).</summary>
    public const string PurchasePriceVariance = "5100";
    /// <summary>Goods received not invoiced: accrued at receipt, cleared when the vendor bill is entered.</summary>
    public const string GoodsReceivedNotInvoiced = "2100";
}

public sealed record VendorResponse(Guid Id, string Code, string Name, string? Email, string? Phone, int PaymentTermsDays, bool IsActive);
public sealed record CreateVendorRequest(string Code, string Name, string? Email, string? Phone, int? PaymentTermsDays);
public sealed record UpdateVendorRequest(string Name, string? Email, string? Phone, int? PaymentTermsDays);

public sealed record PurchaseLineRequest(Guid ItemId, decimal Quantity, decimal? UnitCost);
public sealed record CreatePurchaseOrderRequest(Guid VendorId, DateOnly? OrderDate, string? Notes, IReadOnlyList<PurchaseLineRequest> Lines);
public sealed record PurchaseOrderLineResponse(
    Guid ItemId, string Sku, string Description, decimal Quantity, decimal UnitCost, decimal LineTotal,
    decimal QuantityReceived, decimal QuantityBilled);
public sealed record ReceiptSummary(Guid Id, string Number, DateOnly ReceiptDate, string Reference, decimal Total, string JournalNumber);
public sealed record BillSummary(Guid Id, string Number, DateOnly BillDate, decimal Total, decimal PriceVariance, BillStatus Status);
public sealed record PurchaseOrderResponse(
    Guid Id, string Number, Guid VendorId, string VendorName, DateOnly OrderDate, string? Notes, PurchaseOrderStatus Status,
    decimal Total, IReadOnlyList<PurchaseOrderLineResponse> Lines, IReadOnlyList<ReceiptSummary> Receipts, IReadOnlyList<BillSummary> Bills);

/// <param name="Lines">Quantities to receive per item; omit to receive everything outstanding.</param>
public sealed record ReceiveLineRequest(Guid ItemId, decimal Quantity);
public sealed record ReceiveGoodsRequest(DateOnly? Date, string? Reference, IReadOnlyList<ReceiveLineRequest>? Lines);

/// <param name="Lines">Quantity (default: received and not yet billed) and billed unit cost (default: the order price) per item; omit to bill everything received.</param>
public sealed record BillLineRequest(Guid ItemId, decimal? Quantity, decimal? UnitCost);
public sealed record CreateBillRequest(DateOnly? Date, string? VendorReference, IReadOnlyList<BillLineRequest>? Lines, bool OverrideVariance = false);
public sealed record BillLineResponse(
    Guid ItemId, string Sku, string Description, decimal Quantity, decimal OrderUnitCost, decimal UnitCost,
    decimal LineTotal, decimal PriceVariance, string MatchStatus);
public sealed record BillResponse(
    Guid Id, string Number, string? VendorReference, Guid PurchaseOrderId, Guid VendorId, string VendorName,
    DateOnly BillDate, DateOnly DueDate, decimal Total, decimal AmountPaid, decimal Balance, BillStatus Status,
    bool IsOverdue, string JournalNumber, decimal PriceVariance, bool VarianceOverridden, IReadOnlyList<BillLineResponse> Lines);

public sealed record PayBillRequest(decimal Amount, PaymentMethod Method, DateOnly? Date, string? Reference);
public sealed record VendorPaymentResponse(
    Guid Id, string Number, Guid BillId, string BillNumber, Guid VendorId, string VendorName,
    DateOnly PaymentDate, decimal Amount, PaymentMethod Method, string Reference, string JournalNumber);

public enum PurchaseOrderAction { Open, Release, Cancel, CloseShort }

public interface IPurchasingStore
{
    Task AddVendorAsync(Vendor vendor, CancellationToken cancellationToken);
    Task<IReadOnlyList<Vendor>> ListVendorsAsync(CompanyId companyId, CancellationToken cancellationToken);
    Task<Vendor?> GetVendorAsync(CompanyId companyId, VendorId id, CancellationToken cancellationToken);
    Task<Vendor?> GetVendorByCodeAsync(CompanyId companyId, string code, CancellationToken cancellationToken);

    Task AddOrderAsync(PurchaseOrder order, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseOrder>> ListOrdersAsync(CompanyId companyId, int take, CancellationToken cancellationToken);
    Task<PurchaseOrder?> GetOrderAsync(CompanyId companyId, PurchaseOrderId id, CancellationToken cancellationToken);

    Task AddReceiptAsync(GoodsReceipt receipt, CancellationToken cancellationToken);
    Task<IReadOnlyList<GoodsReceipt>> ListReceiptsForOrderAsync(CompanyId companyId, PurchaseOrderId orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorBill>> ListBillsForOrderAsync(CompanyId companyId, PurchaseOrderId orderId, CancellationToken cancellationToken);
    Task<bool> BillExistsForVendorReferenceAsync(CompanyId companyId, VendorId vendorId, string vendorReference, CancellationToken cancellationToken);

    Task AddBillAsync(VendorBill bill, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorBill>> ListBillsAsync(CompanyId companyId, int take, CancellationToken cancellationToken);
    Task<VendorBill?> GetBillAsync(CompanyId companyId, VendorBillId id, CancellationToken cancellationToken);

    Task AddPaymentAsync(VendorPayment payment, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorPayment>> ListPaymentsAsync(CompanyId companyId, int take, CancellationToken cancellationToken);

    Task<T> InLockedTransactionAsync<T>(CompanyId companyId, string lockKey, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IVendorService
{
    Task<VendorResponse> CreateAsync(CreateVendorRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorResponse>> ListAsync(CancellationToken cancellationToken);
    Task<VendorResponse?> GetAsync(VendorId id, CancellationToken cancellationToken);
    Task<VendorResponse?> UpdateAsync(VendorId id, UpdateVendorRequest request, CancellationToken cancellationToken);
    Task<VendorResponse?> SetActiveAsync(VendorId id, bool active, CancellationToken cancellationToken);
}

public interface IPurchaseOrderService
{
    Task<PurchaseOrderResponse> CreateAsync(CreatePurchaseOrderRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseOrderResponse>> ListAsync(int take, CancellationToken cancellationToken);
    Task<PurchaseOrderResponse?> GetAsync(PurchaseOrderId id, CancellationToken cancellationToken);
    Task<PurchaseOrderResponse?> TransitionAsync(PurchaseOrderId id, PurchaseOrderAction action, CancellationToken cancellationToken);
    Task<PurchaseOrderResponse?> ReceiveAsync(PurchaseOrderId id, ReceiveGoodsRequest request, CancellationToken cancellationToken);
}

public interface IBillService
{
    /// <param name="canOverrideVariance">Whether the caller may approve a price variance beyond tolerance.</param>
    Task<BillResponse?> CreateFromOrderAsync(PurchaseOrderId orderId, CreateBillRequest request, bool canOverrideVariance, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillResponse>> ListAsync(int take, CancellationToken cancellationToken);
    Task<BillResponse?> GetAsync(VendorBillId id, CancellationToken cancellationToken);
    Task<VendorPaymentResponse?> PayAsync(VendorBillId id, PayBillRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorPaymentResponse>> ListPaymentsAsync(int take, CancellationToken cancellationToken);
}

public static class PurchasingModule
{
    public static IServiceCollection AddPurchasingModule(this IServiceCollection services)
    {
        services.AddScoped<IVendorService, VendorService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IBillService, BillService>();
        return services;
    }
}
