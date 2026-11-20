using Erp.SharedKernel;

namespace Erp.Modules.Purchasing.Domain;

public readonly record struct VendorId(Guid Value);
public readonly record struct PurchaseOrderId(Guid Value);
public readonly record struct VendorBillId(Guid Value);
public readonly record struct VendorPaymentId(Guid Value);

public enum PurchaseOrderStatus { Draft, Open, Released, Received, Billed, Cancelled }
public enum BillStatus { Open, Paid }
public enum PaymentMethod { Cash, Bank }

public sealed class Vendor : AggregateRoot<VendorId>
{
    private Vendor()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Vendor(VendorId id, CompanyId companyId, string code, string name, string? email, string? phone,
        int paymentTermsDays, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Code = NormalizeCode(code);
        Name = NormalizeName(name);
        Email = Clean(email);
        Phone = Clean(phone);
        PaymentTermsDays = ValidateTerms(paymentTermsDays);
        IsActive = true;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public int PaymentTermsDays { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Vendor Create(CompanyId companyId, string code, string name, string? email, string? phone,
        int paymentTermsDays, DateTimeOffset createdAt) =>
        new(new VendorId(Guid.NewGuid()), companyId, code, name, email, phone, paymentTermsDays, createdAt);

    public static string NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code) ? throw new DomainException("Vendor code is required.") : code.Trim().ToUpperInvariant();

    public void Update(string name, string? email, string? phone, int paymentTermsDays)
    {
        Name = NormalizeName(name);
        Email = Clean(email);
        Phone = Clean(phone);
        PaymentTermsDays = ValidateTerms(paymentTermsDays);
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    private static string NormalizeName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new DomainException("Vendor name is required.") : name.Trim();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ValidateTerms(int days) =>
        days is < 0 or > 365 ? throw new DomainException("Payment terms must be between 0 and 365 days.") : days;
}

public sealed record PurchaseOrderLine
{
    public PurchaseOrderLine(Guid itemId, string sku, string description, decimal quantity, decimal unitCost)
    {
        if (quantity <= 0) throw new DomainException("Line quantity must be positive.");
        if (unitCost < 0) throw new DomainException("Line unit cost cannot be negative.");
        ItemId = itemId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitCost = unitCost;
    }

    public Guid ItemId { get; }
    public string Sku { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public decimal UnitCost { get; }
    public decimal LineTotal => decimal.Round(Quantity * UnitCost, 2);
}

/// <summary>
/// Draft → Open (confirmed) → Released (sent to vendor) → Received (goods in, GRNI accrued) → Billed.
/// MVP: the whole order is received and billed at once.
/// </summary>
public sealed class PurchaseOrder : AggregateRoot<PurchaseOrderId>
{
    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder()
    {
        Number = string.Empty;
    }

    private PurchaseOrder(PurchaseOrderId id, CompanyId companyId, string number, VendorId vendorId,
        DateOnly orderDate, string? notes, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        VendorId = vendorId;
        OrderDate = orderDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public VendorId VendorId { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public string? Notes { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public DateOnly? ReceivedDate { get; private set; }
    public string? ReceiptJournalNumber { get; private set; }
    public VendorBillId? BillId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(x => x.LineTotal);

    public static PurchaseOrder Create(CompanyId companyId, string number, VendorId vendorId,
        DateOnly orderDate, string? notes, DateTimeOffset createdAt) =>
        new(new PurchaseOrderId(Guid.NewGuid()), companyId, number, vendorId, orderDate, notes, createdAt);

    public void AddLine(Guid itemId, string sku, string description, decimal quantity, decimal unitCost)
    {
        if (Status != PurchaseOrderStatus.Draft) throw new DomainException("Only draft purchase orders can be changed.");
        _lines.Add(new PurchaseOrderLine(itemId, sku, description, quantity, unitCost));
    }

    public void Open()
    {
        EnsureStatus(PurchaseOrderStatus.Draft);
        if (_lines.Count == 0) throw new DomainException("A purchase order must contain at least one line.");
        Status = PurchaseOrderStatus.Open;
    }

    public void Release()
    {
        EnsureStatus(PurchaseOrderStatus.Open);
        Status = PurchaseOrderStatus.Released;
    }

    public void MarkReceived(DateOnly receivedDate, string journalNumber)
    {
        EnsureStatus(PurchaseOrderStatus.Released);
        ReceivedDate = receivedDate;
        ReceiptJournalNumber = journalNumber;
        Status = PurchaseOrderStatus.Received;
    }

    public void MarkBilled(VendorBillId billId)
    {
        EnsureStatus(PurchaseOrderStatus.Received);
        BillId = billId;
        Status = PurchaseOrderStatus.Billed;
    }

    public void Cancel()
    {
        if (Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Billed or PurchaseOrderStatus.Cancelled)
            throw new DomainException("This purchase order cannot be cancelled.");
        Status = PurchaseOrderStatus.Cancelled;
    }

    private void EnsureStatus(PurchaseOrderStatus expected)
    {
        if (Status != expected) throw new DomainException($"Purchase order must be {expected}.");
    }
}

public sealed record VendorBillLine
{
    public VendorBillLine(Guid itemId, string sku, string description, decimal quantity, decimal unitCost)
    {
        ItemId = itemId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitCost = unitCost;
    }

    public Guid ItemId { get; }
    public string Sku { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public decimal UnitCost { get; }
    public decimal LineTotal => decimal.Round(Quantity * UnitCost, 2);
}

public sealed class VendorBill : AggregateRoot<VendorBillId>
{
    private readonly List<VendorBillLine> _lines = [];

    private VendorBill()
    {
        Number = string.Empty;
        JournalNumber = string.Empty;
    }

    private VendorBill(VendorBillId id, CompanyId companyId, string number, string? vendorReference, PurchaseOrderId orderId,
        VendorId vendorId, DateOnly billDate, DateOnly dueDate, Guid journalId, string journalNumber, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        VendorReference = string.IsNullOrWhiteSpace(vendorReference) ? null : vendorReference.Trim();
        PurchaseOrderId = orderId;
        VendorId = vendorId;
        BillDate = billDate;
        DueDate = dueDate;
        JournalId = journalId;
        JournalNumber = journalNumber;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public string? VendorReference { get; private set; }
    public PurchaseOrderId PurchaseOrderId { get; private set; }
    public VendorId VendorId { get; private set; }
    public DateOnly BillDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public Guid JournalId { get; private set; }
    public string JournalNumber { get; private set; }
    public decimal AmountPaid { get; private set; }
    public BillStatus Status { get; private set; } = BillStatus.Open;
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<VendorBillLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(x => x.LineTotal);
    public decimal Balance => Total - AmountPaid;

    public static VendorBill Create(CompanyId companyId, string number, string? vendorReference, PurchaseOrder order,
        DateOnly billDate, int paymentTermsDays, Guid journalId, string journalNumber, DateTimeOffset createdAt)
    {
        var bill = new VendorBill(new VendorBillId(Guid.NewGuid()), companyId, number, vendorReference, order.Id, order.VendorId,
            billDate, billDate.AddDays(paymentTermsDays), journalId, journalNumber, createdAt);
        bill._lines.AddRange(order.Lines.Select(l => new VendorBillLine(l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitCost)));
        if (bill._lines.Count == 0) throw new DomainException("A bill must contain at least one line.");
        return bill;
    }

    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Payment amount must be positive.");
        if (amount > Balance) throw new DomainException($"Payment exceeds the bill balance of {Balance:N2}.");
        AmountPaid += amount;
        if (Balance == 0) Status = BillStatus.Paid;
    }
}

public sealed class VendorPayment : AggregateRoot<VendorPaymentId>
{
    private VendorPayment()
    {
        Number = string.Empty;
        Reference = string.Empty;
        JournalNumber = string.Empty;
    }

    private VendorPayment(VendorPaymentId id, CompanyId companyId, string number, VendorBillId billId, VendorId vendorId,
        DateOnly paymentDate, decimal amount, PaymentMethod method, string? reference, string journalNumber,
        DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        BillId = billId;
        VendorId = vendorId;
        PaymentDate = paymentDate;
        Amount = amount;
        Method = method;
        Reference = reference?.Trim() ?? string.Empty;
        JournalNumber = journalNumber;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public VendorBillId BillId { get; private set; }
    public VendorId VendorId { get; private set; }
    public DateOnly PaymentDate { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string Reference { get; private set; }
    public string JournalNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static VendorPayment Create(CompanyId companyId, string number, VendorBillId billId, VendorId vendorId,
        DateOnly paymentDate, decimal amount, PaymentMethod method, string? reference, string journalNumber,
        DateTimeOffset createdAt)
    {
        if (amount <= 0) throw new DomainException("Payment amount must be positive.");
        return new VendorPayment(new VendorPaymentId(Guid.NewGuid()), companyId, number, billId, vendorId, paymentDate,
            amount, method, reference, journalNumber, createdAt);
    }
}
