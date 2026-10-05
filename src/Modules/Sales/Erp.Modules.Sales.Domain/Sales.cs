using Erp.SharedKernel;

namespace Erp.Modules.Sales.Domain;

public readonly record struct CustomerId(Guid Value);
public readonly record struct SalesOrderId(Guid Value);

public enum SalesOrderStatus { Draft, Open, Released, PartiallyPosted, Posted, Cancelled }

public sealed record Customer(CustomerId Id, CompanyId CompanyId, string Name);

public sealed record SalesOrderLine(string ItemCode, decimal Quantity, decimal UnitPrice);

public sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<SalesOrderLine> _lines = [];
    private SalesOrder(SalesOrderId id, CompanyId companyId, CustomerId customerId) : base(id)
    {
        CompanyId = companyId;
        CustomerId = customerId;
    }

    public CompanyId CompanyId { get; }
    public CustomerId CustomerId { get; }
    public SalesOrderStatus Status { get; private set; } = SalesOrderStatus.Draft;
    public IReadOnlyCollection<SalesOrderLine> Lines => _lines.AsReadOnly();

    public static SalesOrder Create(CompanyId companyId, CustomerId customerId) =>
        new(new SalesOrderId(Guid.NewGuid()), companyId, customerId);

    public void AddLine(string itemCode, decimal quantity, decimal unitPrice)
    {
        if (Status != SalesOrderStatus.Draft) throw new DomainException("Only draft sales orders can be changed.");
        if (quantity <= 0 || unitPrice < 0) throw new DomainException("Sales order quantities and prices are invalid.");
        _lines.Add(new SalesOrderLine(itemCode, quantity, unitPrice));
    }

    public void Open()
    {
        EnsureStatus(SalesOrderStatus.Draft);
        if (_lines.Count == 0) throw new DomainException("A sales order must contain at least one line.");
        Status = SalesOrderStatus.Open;
    }

    public void Release()
    {
        EnsureStatus(SalesOrderStatus.Open);
        Status = SalesOrderStatus.Released;
    }

    public void Cancel()
    {
        if (Status is SalesOrderStatus.Posted or SalesOrderStatus.Cancelled)
            throw new DomainException("This sales order cannot be cancelled.");
        Status = SalesOrderStatus.Cancelled;
    }

    private void EnsureStatus(SalesOrderStatus expected)
    {
        if (Status != expected) throw new DomainException($"Sales order must be {expected}.");
    }
}
