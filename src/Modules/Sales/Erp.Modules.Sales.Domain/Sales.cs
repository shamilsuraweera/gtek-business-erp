using Erp.SharedKernel;

namespace Erp.Modules.Sales.Domain;

public readonly record struct CustomerId(Guid Value);
public readonly record struct SalesOrderId(Guid Value);
public readonly record struct SalesInvoiceId(Guid Value);
public readonly record struct PaymentId(Guid Value);

public enum SalesOrderStatus { Draft, Open, Released, Invoiced, Cancelled }
public enum InvoiceStatus { Open, Paid }
public enum PaymentMethod { Cash, Bank }

public sealed class Customer : AggregateRoot<CustomerId>
{
    private Customer()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Customer(CustomerId id, CompanyId companyId, string code, string name, string? email, string? phone,
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

    public static Customer Create(CompanyId companyId, string code, string name, string? email, string? phone,
        int paymentTermsDays, DateTimeOffset createdAt) =>
        new(new CustomerId(Guid.NewGuid()), companyId, code, name, email, phone, paymentTermsDays, createdAt);

    public static string NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code) ? throw new DomainException("Customer code is required.") : code.Trim().ToUpperInvariant();

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
        string.IsNullOrWhiteSpace(name) ? throw new DomainException("Customer name is required.") : name.Trim();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ValidateTerms(int days) =>
        days is < 0 or > 365 ? throw new DomainException("Payment terms must be between 0 and 365 days.") : days;
}

public sealed record SalesOrderLine
{
    public SalesOrderLine(Guid itemId, string sku, string description, decimal quantity, decimal unitPrice)
    {
        if (quantity <= 0) throw new DomainException("Line quantity must be positive.");
        if (unitPrice < 0) throw new DomainException("Line unit price cannot be negative.");
        ItemId = itemId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid ItemId { get; }
    public string Sku { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal LineTotal => decimal.Round(Quantity * UnitPrice, 2);
}

public sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<SalesOrderLine> _lines = [];

    private SalesOrder()
    {
        Number = string.Empty;
    }

    private SalesOrder(SalesOrderId id, CompanyId companyId, string number, CustomerId customerId,
        DateOnly orderDate, string? notes, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        CustomerId = customerId;
        OrderDate = orderDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public string? Notes { get; private set; }
    public SalesOrderStatus Status { get; private set; } = SalesOrderStatus.Draft;
    public SalesInvoiceId? InvoiceId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<SalesOrderLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(x => x.LineTotal);

    public static SalesOrder Create(CompanyId companyId, string number, CustomerId customerId,
        DateOnly orderDate, string? notes, DateTimeOffset createdAt) =>
        new(new SalesOrderId(Guid.NewGuid()), companyId, number, customerId, orderDate, notes, createdAt);

    public void AddLine(Guid itemId, string sku, string description, decimal quantity, decimal unitPrice)
    {
        if (Status != SalesOrderStatus.Draft) throw new DomainException("Only draft sales orders can be changed.");
        _lines.Add(new SalesOrderLine(itemId, sku, description, quantity, unitPrice));
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

    public void MarkInvoiced(SalesInvoiceId invoiceId)
    {
        EnsureStatus(SalesOrderStatus.Released);
        InvoiceId = invoiceId;
        Status = SalesOrderStatus.Invoiced;
    }

    public void Cancel()
    {
        if (Status is SalesOrderStatus.Invoiced or SalesOrderStatus.Cancelled)
            throw new DomainException("This sales order cannot be cancelled.");
        Status = SalesOrderStatus.Cancelled;
    }

    private void EnsureStatus(SalesOrderStatus expected)
    {
        if (Status != expected) throw new DomainException($"Sales order must be {expected}.");
    }
}

public sealed record SalesInvoiceLine
{
    public SalesInvoiceLine(Guid itemId, string sku, string description, decimal quantity, decimal unitPrice, decimal unitCost)
    {
        ItemId = itemId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        UnitCost = unitCost;
    }

    public Guid ItemId { get; }
    public string Sku { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal UnitCost { get; }
    public decimal LineTotal => decimal.Round(Quantity * UnitPrice, 2);
}

public sealed class SalesInvoice : AggregateRoot<SalesInvoiceId>
{
    private readonly List<SalesInvoiceLine> _lines = [];

    private SalesInvoice()
    {
        Number = string.Empty;
        JournalNumber = string.Empty;
    }

    private SalesInvoice(SalesInvoiceId id, CompanyId companyId, string number, SalesOrderId orderId, CustomerId customerId,
        DateOnly invoiceDate, DateOnly dueDate, Guid journalId, string journalNumber, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        SalesOrderId = orderId;
        CustomerId = customerId;
        InvoiceDate = invoiceDate;
        DueDate = dueDate;
        JournalId = journalId;
        JournalNumber = journalNumber;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public SalesOrderId SalesOrderId { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public DateOnly InvoiceDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public Guid JournalId { get; private set; }
    public string JournalNumber { get; private set; }
    public decimal AmountPaid { get; private set; }
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Open;
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<SalesInvoiceLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(x => x.LineTotal);
    public decimal Balance => Total - AmountPaid;
    public decimal CostOfGoods => decimal.Round(_lines.Sum(x => x.Quantity * x.UnitCost), 2);

    public static SalesInvoice Create(CompanyId companyId, string number, SalesOrder order, DateOnly invoiceDate,
        int paymentTermsDays, IReadOnlyList<SalesInvoiceLine> lines, Guid journalId, string journalNumber, DateTimeOffset createdAt)
    {
        if (lines.Count == 0) throw new DomainException("An invoice must contain at least one line.");
        var invoice = new SalesInvoice(new SalesInvoiceId(Guid.NewGuid()), companyId, number, order.Id, order.CustomerId,
            invoiceDate, invoiceDate.AddDays(paymentTermsDays), journalId, journalNumber, createdAt);
        invoice._lines.AddRange(lines);
        return invoice;
    }

    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Payment amount must be positive.");
        if (amount > Balance) throw new DomainException($"Payment exceeds the invoice balance of {Balance:N2}.");
        AmountPaid += amount;
        if (Balance == 0) Status = InvoiceStatus.Paid;
    }
}

public sealed class Payment : AggregateRoot<PaymentId>
{
    private Payment()
    {
        Number = string.Empty;
        Reference = string.Empty;
        JournalNumber = string.Empty;
    }

    private Payment(PaymentId id, CompanyId companyId, string number, SalesInvoiceId invoiceId, CustomerId customerId,
        DateOnly paymentDate, decimal amount, PaymentMethod method, string? reference, string journalNumber,
        DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        InvoiceId = invoiceId;
        CustomerId = customerId;
        PaymentDate = paymentDate;
        Amount = amount;
        Method = method;
        Reference = reference?.Trim() ?? string.Empty;
        JournalNumber = journalNumber;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public SalesInvoiceId InvoiceId { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public DateOnly PaymentDate { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string Reference { get; private set; }
    public string JournalNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Payment Create(CompanyId companyId, string number, SalesInvoiceId invoiceId, CustomerId customerId,
        DateOnly paymentDate, decimal amount, PaymentMethod method, string? reference, string journalNumber,
        DateTimeOffset createdAt)
    {
        if (amount <= 0) throw new DomainException("Payment amount must be positive.");
        return new Payment(new PaymentId(Guid.NewGuid()), companyId, number, invoiceId, customerId, paymentDate,
            amount, method, reference, journalNumber, createdAt);
    }
}
