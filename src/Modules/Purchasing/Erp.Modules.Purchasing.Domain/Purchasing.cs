using Erp.SharedKernel;

namespace Erp.Modules.Purchasing.Domain;

public readonly record struct VendorId(Guid Value);
public readonly record struct PurchaseOrderId(Guid Value);
public enum PurchaseOrderStatus { Draft, Open, Released, PartiallyReceived, Received, Cancelled }
public sealed record Vendor(VendorId Id, CompanyId CompanyId, string Name);
public sealed record PurchaseOrderLine(string ItemCode, decimal Quantity, decimal UnitPrice);

public sealed class PurchaseOrder : AggregateRoot<PurchaseOrderId>
{
    private readonly List<PurchaseOrderLine> _lines = [];
    private PurchaseOrder(PurchaseOrderId id, CompanyId companyId, VendorId vendorId) : base(id)
    {
        CompanyId = companyId;
        VendorId = vendorId;
    }

    public CompanyId CompanyId { get; }
    public VendorId VendorId { get; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    public static PurchaseOrder Create(CompanyId companyId, VendorId vendorId) =>
        new(new PurchaseOrderId(Guid.NewGuid()), companyId, vendorId);

    public void AddLine(string itemCode, decimal quantity, decimal unitPrice)
    {
        if (Status != PurchaseOrderStatus.Draft) throw new DomainException("Only draft purchase orders can be changed.");
        if (quantity <= 0 || unitPrice < 0) throw new DomainException("Purchase order quantities and prices are invalid.");
        _lines.Add(new PurchaseOrderLine(itemCode, quantity, unitPrice));
    }

    public void Open()
    {
        if (Status != PurchaseOrderStatus.Draft || _lines.Count == 0)
            throw new DomainException("A purchase order must be a non-empty draft.");
        Status = PurchaseOrderStatus.Open;
    }

    public void Release()
    {
        if (Status != PurchaseOrderStatus.Open) throw new DomainException("Purchase order must be open.");
        Status = PurchaseOrderStatus.Released;
    }
}
