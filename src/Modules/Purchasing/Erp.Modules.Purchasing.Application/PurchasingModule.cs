using Erp.Application.Abstractions;
using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Purchasing.Application;

public sealed record VendorResponse(Guid Id, string Code, string Name, string? Email, string? Phone, int PaymentTermsDays, bool IsActive, decimal? PriceTolerancePercent, decimal? QuantityTolerancePercent, decimal? UnderDeliveryTolerancePercent);
public sealed record CreateVendorRequest(string Code, string Name, string? Email, string? Phone, int? PaymentTermsDays, decimal? PriceTolerancePercent = null, decimal? QuantityTolerancePercent = null, decimal? UnderDeliveryTolerancePercent = null);
public sealed record UpdateVendorRequest(string Name, string? Email, string? Phone, int? PaymentTermsDays, decimal? PriceTolerancePercent = null, decimal? QuantityTolerancePercent = null, decimal? UnderDeliveryTolerancePercent = null);

/// <summary>The three tolerance fields are per-order overrides (they need the override-tolerance permission); none defers to the item, then the vendor, then the company default.</summary>
public sealed record PurchaseLineRequest(Guid ItemId, decimal Quantity, decimal? UnitCost,
    decimal? PriceTolerancePercent = null, decimal? QuantityTolerancePercent = null, decimal? UnderDeliveryTolerancePercent = null);
public sealed record OrderLineToleranceRequest(Guid ItemId, decimal? PriceTolerancePercent, decimal? QuantityTolerancePercent, decimal? UnderDeliveryTolerancePercent);
/// <summary>Replaces the overrides of the listed lines: a null value clears that override.</summary>
public sealed record UpdateOrderTolerancesRequest(IReadOnlyList<OrderLineToleranceRequest> Lines);
public sealed record CreatePurchaseOrderRequest(Guid VendorId, DateOnly? OrderDate, string? Notes, IReadOnlyList<PurchaseLineRequest> Lines);
public sealed record PurchaseOrderLineResponse(
    Guid ItemId, string Sku, string Description, decimal Quantity, decimal UnitCost, decimal LineTotal,
    decimal QuantityReceived, decimal QuantityBilled, decimal QuantityReturned, decimal QuantityDebited, decimal DebitableQuantity,
    decimal? PriceTolerancePercent = null, decimal? QuantityTolerancePercent = null, decimal? UnderDeliveryTolerancePercent = null,
    decimal? PriceToleranceOverride = null, decimal? QuantityToleranceOverride = null, decimal? UnderDeliveryToleranceOverride = null);
public sealed record ReceiptSummary(Guid Id, string Number, DateOnly ReceiptDate, string Reference, decimal Total, string JournalNumber);
public sealed record ReturnSummary(Guid Id, string Number, DateOnly ReturnDate, string Reason, string Reference, decimal Total, string JournalNumber);
public sealed record BillSummary(Guid Id, string Number, DateOnly BillDate, decimal Total, decimal PriceVariance, BillStatus Status);
public sealed record PurchaseOrderResponse(
    Guid Id, string Number, Guid VendorId, string VendorName, DateOnly OrderDate, string? Notes, PurchaseOrderStatus Status,
    decimal Total, IReadOnlyList<PurchaseOrderLineResponse> Lines, IReadOnlyList<ReceiptSummary> Receipts, IReadOnlyList<BillSummary> Bills,
    IReadOnlyList<ReturnSummary> Returns);

public sealed record ReturnLineRequest(Guid ItemId, decimal Quantity);
public sealed record ReturnGoodsRequest(DateOnly? Date, string Reason, string? Reference, IReadOnlyList<ReturnLineRequest> Lines);

/// <param name="Lines">Quantities to receive per item; omit to receive everything outstanding.</param>
public sealed record ReceiveLineRequest(Guid ItemId, decimal Quantity);
public sealed record ReceiveGoodsRequest(DateOnly? Date, string? Reference, IReadOnlyList<ReceiveLineRequest>? Lines);

/// <param name="Lines">Quantity (default: received and not yet billed) and billed unit cost (default: the order price) per item; omit to bill everything received.</param>
public sealed record BillLineRequest(Guid ItemId, decimal? Quantity, decimal? UnitCost);
public sealed record CreateBillRequest(DateOnly? Date, string? VendorReference, IReadOnlyList<BillLineRequest>? Lines, bool OverrideVariance = false);
public sealed record BillLineResponse(
    Guid ItemId, string Sku, string Description, decimal Quantity, decimal OrderUnitCost, decimal UnitCost,
    decimal LineTotal, decimal PriceVariance, string MatchStatus, decimal QuantityDebited,
    string? TaxCode, decimal TaxRate, decimal TaxAmount, decimal TolerancePercent);
public sealed record BillResponse(
    Guid Id, string Number, string? VendorReference, Guid PurchaseOrderId, Guid VendorId, string VendorName,
    DateOnly BillDate, DateOnly DueDate, decimal Subtotal, decimal TaxTotal, decimal Total, decimal AmountPaid, decimal AmountDebited, decimal Balance, BillStatus Status,
    bool IsOverdue, string JournalNumber, decimal PriceVariance, bool VarianceOverridden, IReadOnlyList<BillLineResponse> Lines);

public sealed record DebitNoteLineRequest(Guid ItemId, decimal Quantity);
public sealed record CreateDebitNoteRequest(DateOnly? Date, string Reason, IReadOnlyList<DebitNoteLineRequest> Lines);
public sealed record DebitNoteLineResponse(
    Guid ItemId, string Sku, string Description, decimal Quantity, decimal OrderUnitCost, decimal UnitCost, decimal LineTotal, decimal PriceVariance,
    decimal TaxRate, decimal TaxAmount);
public sealed record VendorRefundRequest(decimal Amount, PaymentMethod Method, DateOnly? Date, string? Reference);
public sealed record VendorRefundResponse(
    Guid Id, string Number, Guid DebitNoteId, string DebitNoteNumber, Guid VendorId, string VendorName,
    DateOnly RefundDate, decimal Amount, PaymentMethod Method, string Reference, string JournalNumber);
public sealed record DebitNoteResponse(
    Guid Id, string Number, Guid PurchaseOrderId, Guid BillId, string BillNumber, Guid VendorId, string VendorName, DateOnly DebitDate,
    string Reason, decimal Subtotal, decimal TaxTotal, decimal Total, decimal AmountApplied, decimal AmountRefunded, decimal RefundDue, decimal PriceVariance,
    string JournalNumber, IReadOnlyList<DebitNoteLineResponse> Lines, IReadOnlyList<VendorRefundResponse> Refunds);

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
    /// <summary>Orders placed with the vendor and not yet fully received (released or part-received), with their lines, read without tracking.</summary>
    Task<IReadOnlyList<PurchaseOrder>> ListOrdersAwaitingGoodsAsync(CompanyId companyId, CancellationToken cancellationToken);

    Task AddReceiptAsync(GoodsReceipt receipt, CancellationToken cancellationToken);
    Task<IReadOnlyList<GoodsReceipt>> ListReceiptsForOrderAsync(CompanyId companyId, PurchaseOrderId orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorBill>> ListBillsForOrderAsync(CompanyId companyId, PurchaseOrderId orderId, CancellationToken cancellationToken);
    Task<bool> BillExistsForVendorReferenceAsync(CompanyId companyId, VendorId vendorId, string vendorReference, CancellationToken cancellationToken);

    Task AddReturnAsync(VendorReturn vendorReturn, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorReturn>> ListReturnsForOrderAsync(CompanyId companyId, PurchaseOrderId orderId, CancellationToken cancellationToken);

    Task AddBillAsync(VendorBill bill, CancellationToken cancellationToken);
    /// <summary>The order a bill belongs to, read without tracking (needed to take the right locks before loading the bill).</summary>
    Task<PurchaseOrderId?> GetBillOrderIdAsync(CompanyId companyId, VendorBillId billId, CancellationToken cancellationToken);

    Task AddDebitNoteAsync(DebitNote note, CancellationToken cancellationToken);
    Task<IReadOnlyList<DebitNote>> ListDebitNotesAsync(CompanyId companyId, int take, CancellationToken cancellationToken);
    Task<DebitNote?> GetDebitNoteAsync(CompanyId companyId, DebitNoteId id, CancellationToken cancellationToken);
    Task<IReadOnlyList<DebitNote>> ListDebitNotesForBillAsync(CompanyId companyId, VendorBillId billId, CancellationToken cancellationToken);
    Task AddVendorRefundAsync(VendorRefund refund, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorRefund>> ListVendorRefundsAsync(CompanyId companyId, int take, CancellationToken cancellationToken);
    /// <summary>Open bills whose due date is before <paramref name="today"/>, oldest due first.</summary>
    Task<IReadOnlyList<VendorBill>> ListOverdueBillsAsync(CompanyId companyId, DateOnly today, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorBill>> ListBillsAsync(CompanyId companyId, int take, CancellationToken cancellationToken);
    Task<VendorBill?> GetBillAsync(CompanyId companyId, VendorBillId id, CancellationToken cancellationToken);

    Task AddPaymentAsync(VendorPayment payment, CancellationToken cancellationToken);
    Task<IReadOnlyList<VendorPayment>> ListPaymentsAsync(CompanyId companyId, int take, CancellationToken cancellationToken);

    Task AddToleranceRequestAsync(ToleranceRequest request, CancellationToken cancellationToken);
    Task<ToleranceRequest?> GetToleranceRequestAsync(CompanyId companyId, ToleranceRequestId id, CancellationToken cancellationToken);
    /// <summary>The order a request belongs to, read without tracking (to take the order lock before loading the request).</summary>
    Task<PurchaseOrderId?> GetToleranceRequestOrderIdAsync(CompanyId companyId, ToleranceRequestId id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ToleranceRequest>> ListToleranceRequestsAsync(CompanyId companyId, ToleranceRequestStatus? status, PurchaseOrderId? orderId, int take, CancellationToken cancellationToken);
    Task<bool> HasPendingToleranceRequestAsync(CompanyId companyId, PurchaseOrderId orderId, CancellationToken cancellationToken);

    Task<T> InLockedTransactionAsync<T>(CompanyId companyId, string lockKey, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    /// <summary>Holds several locks at once (e.g. an order and one of its bills). Keys are taken in sorted order to avoid deadlocks.</summary>
    Task<T> InLockedTransactionAsync<T>(CompanyId companyId, IReadOnlyCollection<string> lockKeys, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
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
    /// <param name="canOverrideTolerances">Whether the caller may set per-order tolerance overrides on the lines.</param>
    Task<PurchaseOrderResponse> CreateAsync(CreatePurchaseOrderRequest request, bool canOverrideTolerances, CancellationToken cancellationToken);
    Task<PurchaseOrderResponse?> SetTolerancesAsync(PurchaseOrderId id, UpdateOrderTolerancesRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseOrderResponse>> ListAsync(int take, CancellationToken cancellationToken);
    Task<PurchaseOrderResponse?> GetAsync(PurchaseOrderId id, CancellationToken cancellationToken);
    Task<PurchaseOrderResponse?> TransitionAsync(PurchaseOrderId id, PurchaseOrderAction action, CancellationToken cancellationToken);
    Task<PurchaseOrderResponse?> ReceiveAsync(PurchaseOrderId id, ReceiveGoodsRequest request, CancellationToken cancellationToken);
    Task<PurchaseOrderResponse?> ReturnAsync(PurchaseOrderId id, ReturnGoodsRequest request, CancellationToken cancellationToken);
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

public interface IDebitNoteService
{
    Task<DebitNoteResponse?> CreateAsync(VendorBillId billId, CreateDebitNoteRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<DebitNoteResponse>> ListAsync(int take, CancellationToken cancellationToken);
    Task<DebitNoteResponse?> GetAsync(DebitNoteId id, CancellationToken cancellationToken);
    Task<VendorRefundResponse?> RefundAsync(DebitNoteId id, VendorRefundRequest request, CancellationToken cancellationToken);
}

public static class PurchasingModule
{
    public static IServiceCollection AddPurchasingModule(this IServiceCollection services)
    {
        services.AddScoped<IVendorService, VendorService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IBillService, BillService>();
        services.AddScoped<IToleranceRequestService, ToleranceRequestService>();
        services.AddScoped<IDebitNoteService, DebitNoteService>();
        services.AddScoped<IReminderJob, OverdueBillReminder>();
        services.AddScoped<IOnOrderQuantities, OnOrderQuantities>();
        return services;
    }
}
