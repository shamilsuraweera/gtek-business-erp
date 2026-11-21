using Erp.SharedKernel;

namespace Erp.Modules.Purchasing.Domain;

public readonly record struct VendorId(Guid Value);
public readonly record struct PurchaseOrderId(Guid Value);
public readonly record struct GoodsReceiptId(Guid Value);
public readonly record struct VendorBillId(Guid Value);
public readonly record struct VendorPaymentId(Guid Value);
public readonly record struct VendorReturnId(Guid Value);
public readonly record struct DebitNoteId(Guid Value);
public readonly record struct VendorRefundId(Guid Value);

public enum PurchaseOrderStatus { Draft, Open, Released, PartiallyReceived, Received, Billed, Cancelled }
public enum BillStatus { Open, Paid, Debited }
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
    /// <summary>This vendor's price tolerance for the three-way match; none means the company default applies.</summary>
    public decimal? PriceTolerancePercent { get; private set; }
    /// <summary>How far above the ordered quantity this vendor may deliver; none means the company default (no over-delivery).</summary>
    public decimal? QuantityTolerancePercent { get; private set; }
    /// <summary>How far below the ordered quantity this vendor's delivery still counts as complete; none means exact quantities (company default).</summary>
    public decimal? UnderDeliveryTolerancePercent { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Vendor Create(CompanyId companyId, string code, string name, string? email, string? phone,
        int paymentTermsDays, DateTimeOffset createdAt, decimal? priceTolerancePercent = null, decimal? quantityTolerancePercent = null, decimal? underDeliveryTolerancePercent = null)
    {
        var vendor = new Vendor(new VendorId(Guid.NewGuid()), companyId, code, name, email, phone, paymentTermsDays, createdAt);
        vendor.PriceTolerancePercent = ThreeWayMatch.ValidateTolerance(priceTolerancePercent);
        vendor.QuantityTolerancePercent = ThreeWayMatch.ValidateTolerance(quantityTolerancePercent, "Quantity");
        vendor.UnderDeliveryTolerancePercent = ThreeWayMatch.ValidateTolerance(underDeliveryTolerancePercent, "Under-delivery");
        return vendor;
    }

    public static string NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code) ? throw new DomainException("Vendor code is required.") : code.Trim().ToUpperInvariant();

    public void Update(string name, string? email, string? phone, int paymentTermsDays, decimal? priceTolerancePercent = null, decimal? quantityTolerancePercent = null, decimal? underDeliveryTolerancePercent = null)
    {
        Name = NormalizeName(name);
        Email = Clean(email);
        Phone = Clean(phone);
        PaymentTermsDays = ValidateTerms(paymentTermsDays);
        PriceTolerancePercent = ThreeWayMatch.ValidateTolerance(priceTolerancePercent);
        QuantityTolerancePercent = ThreeWayMatch.ValidateTolerance(quantityTolerancePercent, "Quantity");
        UnderDeliveryTolerancePercent = ThreeWayMatch.ValidateTolerance(underDeliveryTolerancePercent, "Under-delivery");
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
    public decimal QuantityReceived { get; private set; }
    public decimal QuantityBilled { get; private set; }
    /// <summary>Goods sent back to the vendor.</summary>
    public decimal QuantityReturned { get; private set; }
    /// <summary>Billed quantity cancelled by debit notes.</summary>
    public decimal QuantityDebited { get; private set; }
    public decimal LineTotal => decimal.Round(Quantity * UnitCost, 2);
    public decimal OutstandingToReceive => Math.Max(0m, Quantity - QuantityReceived);
    /// <summary>True once enough has arrived: the ordered quantity less the under-delivery tolerance.</summary>
    public bool IsComplete(decimal underDeliveryTolerancePercent) =>
        QuantityReceived >= ThreeWayMatch.MinCompleteQuantity(Quantity, underDeliveryTolerancePercent);

    /// <summary>How much more may be received when over-delivery up to the tolerance is accepted.</summary>
    public decimal ReceivableWithTolerance(decimal quantityTolerancePercent) =>
        Math.Max(0m, ThreeWayMatch.MaxReceivable(Quantity, quantityTolerancePercent) - QuantityReceived);
    /// <summary>Goods we still hold (received less returned).</summary>
    public decimal NetReceived => QuantityReceived - QuantityReturned;
    /// <summary>Quantity we owe the vendor for (billed less debited).</summary>
    public decimal NetBilled => QuantityBilled - QuantityDebited;
    /// <summary>What can still be billed: goods held that are not yet covered by a bill.</summary>
    public decimal ReceivedNotBilled => Math.Max(0m, NetReceived - NetBilled);
    /// <summary>Billed goods that were sent back: a debit note from the vendor is due for these.</summary>
    public decimal DebitableQuantity => Math.Max(0m, NetBilled - NetReceived);

    internal void ApplyReceipt(decimal quantity) => QuantityReceived += quantity;
    internal void ApplyBilling(decimal quantity) => QuantityBilled += quantity;
    internal void ApplyReturn(decimal quantity) => QuantityReturned += quantity;
    internal void ApplyDebit(decimal quantity) => QuantityDebited += quantity;
}

/// <summary>
/// Draft → Open (confirmed) → Released (sent to vendor) → PartiallyReceived ⇄ Received → Billed.
/// Goods can arrive in several receipts and be billed in several bills. The quantity received and billed per line
/// drives the status; billing is limited to goods that were received (the quantity leg of the three-way match).
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
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(x => x.LineTotal);

    public static PurchaseOrder Create(CompanyId companyId, string number, VendorId vendorId,
        DateOnly orderDate, string? notes, DateTimeOffset createdAt) =>
        new(new PurchaseOrderId(Guid.NewGuid()), companyId, number, vendorId, orderDate, notes, createdAt);

    public void AddLine(Guid itemId, string sku, string description, decimal quantity, decimal unitCost)
    {
        if (Status != PurchaseOrderStatus.Draft) throw new DomainException("Only draft purchase orders can be changed.");
        if (_lines.Any(x => x.ItemId == itemId))
            throw new DomainException($"Item '{sku}' is already on this order; combine the quantities into one line.");
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

    /// <summary>The outstanding (not yet received) quantity of every line, keyed by item.</summary>
    public IReadOnlyDictionary<Guid, decimal> OutstandingToReceive() =>
        _lines.Where(x => x.OutstandingToReceive > 0).ToDictionary(x => x.ItemId, x => x.OutstandingToReceive);

    /// <summary>The received-but-not-yet-billed quantity of every line, keyed by item.</summary>
    public IReadOnlyDictionary<Guid, decimal> ReceivedNotBilled() =>
        _lines.Where(x => x.ReceivedNotBilled > 0).ToDictionary(x => x.ItemId, x => x.ReceivedNotBilled);

    public PurchaseOrderLine LineFor(Guid itemId) =>
        _lines.SingleOrDefault(x => x.ItemId == itemId) ?? throw new DomainException("The item is not on this purchase order.");

    /// <param name="quantityTolerances">Over-delivery tolerance per item (percent); items not listed accept no over-delivery.</param>
    /// <param name="underDeliveryTolerances">Under-delivery tolerance per item (percent); a line within it counts as fully received. Items not listed need the full quantity.</param>
    public void RecordReceipt(IReadOnlyCollection<(Guid ItemId, decimal Quantity)> items, IReadOnlyDictionary<Guid, decimal>? quantityTolerances = null,
        IReadOnlyDictionary<Guid, decimal>? underDeliveryTolerances = null)
    {
        if (Status is not (PurchaseOrderStatus.Released or PurchaseOrderStatus.PartiallyReceived))
            throw new DomainException("Goods can only be received against released purchase orders that are not fully received.");
        decimal ToleranceOf(PurchaseOrderLine l) => quantityTolerances?.GetValueOrDefault(l.ItemId) ?? 0m;
        var checkedItems = ValidateQuantities(items, l => l.ReceivableWithTolerance(ToleranceOf(l)), "receive",
            l => ToleranceOf(l) > 0 ? $"outstanding including the {ToleranceOf(l):0.##}% over-delivery tolerance" : "outstanding");
        foreach (var (itemId, quantity) in checkedItems) LineFor(itemId).ApplyReceipt(quantity);
        Status = _lines.All(x => x.IsComplete(underDeliveryTolerances?.GetValueOrDefault(x.ItemId) ?? 0m)) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
    }

    /// <summary>Accepts the vendor's short delivery: no further receipts are expected.</summary>
    public void CloseShort()
    {
        EnsureStatus(PurchaseOrderStatus.PartiallyReceived);
        Status = PurchaseOrderStatus.Received;
        CompleteIfFullyBilled();
    }

    /// <summary>The goods we still hold per item (received less returned): the most that can be sent back.</summary>
    public IReadOnlyDictionary<Guid, decimal> ReturnableQuantities() =>
        _lines.Where(x => x.NetReceived > 0).ToDictionary(x => x.ItemId, x => x.NetReceived);

    /// <summary>Billed goods that have been returned and are awaiting a debit note, per item.</summary>
    public IReadOnlyDictionary<Guid, decimal> DebitableQuantities() =>
        _lines.Where(x => x.DebitableQuantity > 0).ToDictionary(x => x.ItemId, x => x.DebitableQuantity);

    /// <summary>Sends goods back to the vendor. Returns do not re-open the order: replacements need a new order.</summary>
    public void RecordReturn(IReadOnlyCollection<(Guid ItemId, decimal Quantity)> items)
    {
        if (Status is not (PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.Billed))
            throw new DomainException("Goods can only be returned for purchase orders with received goods.");
        var checkedItems = ValidateQuantities(items, l => l.NetReceived, "return", "held (received and not yet returned)");
        foreach (var (itemId, quantity) in checkedItems) LineFor(itemId).ApplyReturn(quantity);
        CompleteIfFullyBilled();
    }

    /// <summary>Records a vendor debit note: only billed goods that were returned can be debited.</summary>
    public void RecordDebit(IReadOnlyCollection<(Guid ItemId, decimal Quantity)> items)
    {
        var checkedItems = ValidateQuantities(items, l => l.DebitableQuantity, "debit", "returned after being billed");
        foreach (var (itemId, quantity) in checkedItems) LineFor(itemId).ApplyDebit(quantity);
        CompleteIfFullyBilled();
    }

    public void RecordBilling(IReadOnlyCollection<(Guid ItemId, decimal Quantity)> items)
    {
        if (Status is not (PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received))
            throw new DomainException("Bills can only be entered for purchase orders with received goods.");
        var checkedItems = ValidateQuantities(items, l => l.ReceivedNotBilled, "bill", "received and not yet billed");
        foreach (var (itemId, quantity) in checkedItems) LineFor(itemId).ApplyBilling(quantity);
        CompleteIfFullyBilled();
    }

    public void Cancel()
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Open or PurchaseOrderStatus.Released))
            throw new DomainException("This purchase order cannot be cancelled" +
                (Status == PurchaseOrderStatus.Cancelled ? "." : " because goods have been received; close it short instead."));
        Status = PurchaseOrderStatus.Cancelled;
    }

    private void CompleteIfFullyBilled()
    {
        if (Status == PurchaseOrderStatus.Received && _lines.All(x => x.NetBilled >= x.NetReceived))
            Status = PurchaseOrderStatus.Billed;
    }

    private List<(Guid ItemId, decimal Quantity)> ValidateQuantities(
        IReadOnlyCollection<(Guid ItemId, decimal Quantity)> items, Func<PurchaseOrderLine, decimal> available, string verb, string limitName)
        => ValidateQuantities(items, available, verb, _ => limitName);

    private List<(Guid ItemId, decimal Quantity)> ValidateQuantities(
        IReadOnlyCollection<(Guid ItemId, decimal Quantity)> items, Func<PurchaseOrderLine, decimal> available, string verb, Func<PurchaseOrderLine, string> limitName)
    {
        if (items.Count == 0) throw new DomainException($"Specify at least one line to {verb}.");
        if (items.Select(x => x.ItemId).Distinct().Count() != items.Count)
            throw new DomainException("Each item can appear only once.");
        foreach (var (itemId, quantity) in items)
        {
            var line = LineFor(itemId);
            if (quantity <= 0) throw new DomainException($"The quantity to {verb} for '{line.Sku}' must be positive.");
            var limit = available(line);
            if (quantity > limit)
                throw new DomainException($"Cannot {verb} {quantity:0.####} of '{line.Sku}': only {limit:0.####} {limitName(line)}.");
        }

        return items.ToList();
    }

    private void EnsureStatus(PurchaseOrderStatus expected)
    {
        if (Status != expected) throw new DomainException($"Purchase order must be {expected}.");
    }
}

public sealed record GoodsReceiptLine
{
    public GoodsReceiptLine(Guid itemId, string sku, string description, decimal quantity, decimal unitCost, decimal quantityTolerancePercent = 0m, decimal underDeliveryTolerancePercent = 0m)
    {
        QuantityTolerancePercent = quantityTolerancePercent;
        UnderDeliveryTolerancePercent = underDeliveryTolerancePercent;
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
    /// <summary>The over-delivery tolerance this line was accepted under (item, else vendor, else none).</summary>
    public decimal QuantityTolerancePercent { get; }
    /// <summary>The under-delivery tolerance in force when this line was received (item, else vendor, else none).</summary>
    public decimal UnderDeliveryTolerancePercent { get; }
    public decimal LineTotal => decimal.Round(Quantity * UnitCost, 2);
}

/// <summary>One delivery against a purchase order (the "receipt" leg of the three-way match).</summary>
public sealed class GoodsReceipt : AggregateRoot<GoodsReceiptId>
{
    private readonly List<GoodsReceiptLine> _lines = [];

    private GoodsReceipt()
    {
        Number = string.Empty;
        Reference = string.Empty;
        JournalNumber = string.Empty;
    }

    private GoodsReceipt(GoodsReceiptId id, CompanyId companyId, string number, PurchaseOrderId orderId, VendorId vendorId,
        DateOnly receiptDate, string? reference, Guid journalId, string journalNumber, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        PurchaseOrderId = orderId;
        VendorId = vendorId;
        ReceiptDate = receiptDate;
        Reference = reference?.Trim() ?? string.Empty;
        JournalId = journalId;
        JournalNumber = journalNumber;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public PurchaseOrderId PurchaseOrderId { get; private set; }
    public VendorId VendorId { get; private set; }
    public DateOnly ReceiptDate { get; private set; }
    /// <summary>The vendor's delivery note number.</summary>
    public string Reference { get; private set; }
    public Guid JournalId { get; private set; }
    public string JournalNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<GoodsReceiptLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(x => x.LineTotal);

    public static GoodsReceipt Create(CompanyId companyId, string number, PurchaseOrderId orderId, VendorId vendorId,
        DateOnly receiptDate, string? reference, IReadOnlyList<GoodsReceiptLine> lines, Guid journalId, string journalNumber,
        DateTimeOffset createdAt)
    {
        if (lines.Count == 0) throw new DomainException("A goods receipt must contain at least one line.");
        var receipt = new GoodsReceipt(new GoodsReceiptId(Guid.NewGuid()), companyId, number, orderId, vendorId, receiptDate,
            reference, journalId, journalNumber, createdAt);
        receipt._lines.AddRange(lines);
        return receipt;
    }
}

/// <summary>Price tolerance rules for matching a vendor bill to the purchase order.</summary>
public static class ThreeWayMatch
{
    /// <summary>The company-wide default: billed prices may differ from the order price by this much before the bill needs an approved override.</summary>
    public const decimal PriceTolerancePercent = 2m;

    /// <summary>The most a tolerance may be set to.</summary>
    public const decimal MaxTolerancePercent = 100m;

    /// <summary>The tolerance that applies to a line: the item's own, else the vendor's, else the company default.</summary>
    public static decimal EffectiveTolerance(decimal? itemTolerance, decimal? vendorTolerance) =>
        itemTolerance ?? vendorTolerance ?? PriceTolerancePercent;

    /// <summary>The company default for over-delivery: none, so a delivery may not exceed the order unless a tolerance says so.</summary>
    public const decimal QuantityTolerancePercent = 0m;

    /// <summary>The over-delivery tolerance that applies to a line: the item's own, else the vendor's, else none.</summary>
    public static decimal EffectiveQuantityTolerance(decimal? itemTolerance, decimal? vendorTolerance) =>
        itemTolerance ?? vendorTolerance ?? QuantityTolerancePercent;

    /// <summary>The company default for under-delivery: none, so a line is complete only when the full quantity has arrived.</summary>
    public const decimal UnderDeliveryTolerancePercent = 0m;

    /// <summary>The under-delivery tolerance that applies to a line: the item's own, else the vendor's, else none.</summary>
    public static decimal EffectiveUnderDeliveryTolerance(decimal? itemTolerance, decimal? vendorTolerance) =>
        itemTolerance ?? vendorTolerance ?? UnderDeliveryTolerancePercent;

    /// <summary>The least that must be received for a line to count as complete.</summary>
    public static decimal MinCompleteQuantity(decimal orderedQuantity, decimal underDeliveryTolerancePercent) =>
        decimal.Round(orderedQuantity * (1m - underDeliveryTolerancePercent / 100m), 4);

    /// <summary>The most that may be received in total against an ordered quantity.</summary>
    public static decimal MaxReceivable(decimal orderedQuantity, decimal quantityTolerancePercent) =>
        decimal.Round(orderedQuantity * (1m + quantityTolerancePercent / 100m), 4);

    /// <summary>Checks an optional tolerance (0 means none allowed) and returns it rounded to the stored precision.</summary>
    public static decimal? ValidateTolerance(decimal? tolerancePercent, string kind = "Price")
    {
        if (tolerancePercent is null) return null;
        if (tolerancePercent < 0 || tolerancePercent > MaxTolerancePercent)
            throw new DomainException($"{kind} tolerance must be between 0 and {MaxTolerancePercent:0}%.");
        return decimal.Round(tolerancePercent.Value, 4);
    }

    public static decimal VariancePercent(decimal orderUnitCost, decimal billedUnitCost) =>
        orderUnitCost == 0 ? (billedUnitCost == 0 ? 0 : 100) : (billedUnitCost - orderUnitCost) / orderUnitCost * 100m;

    public static bool WithinTolerance(decimal orderUnitCost, decimal billedUnitCost, decimal tolerancePercent = PriceTolerancePercent) =>
        Math.Abs(VariancePercent(orderUnitCost, billedUnitCost)) <= tolerancePercent;
}

public sealed record VendorBillLine
{
    public VendorBillLine(Guid itemId, string sku, string description, decimal quantity, decimal orderUnitCost, decimal unitCost,
        string? taxCode = null, decimal taxRate = 0m, decimal taxAmount = 0m, decimal tolerancePercent = ThreeWayMatch.PriceTolerancePercent)
    {
        if (quantity <= 0) throw new DomainException("Billed quantity must be positive.");
        TolerancePercent = tolerancePercent;
        if (taxAmount < 0) throw new DomainException("Line tax cannot be negative.");
        TaxCode = taxCode;
        TaxRate = taxRate;
        TaxAmount = taxAmount;
        if (unitCost < 0) throw new DomainException("Billed unit cost cannot be negative.");
        ItemId = itemId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        OrderUnitCost = orderUnitCost;
        UnitCost = unitCost;
    }

    public Guid ItemId { get; }
    public string Sku { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    /// <summary>The price on the purchase order (what the goods receipt accrued at).</summary>
    public decimal OrderUnitCost { get; }
    /// <summary>The price the vendor billed.</summary>
    public decimal UnitCost { get; }
    /// <summary>The item's tax code and rate when the bill was entered, and the input tax on this line (charged on the billed price).</summary>
    public string? TaxCode { get; }
    public decimal TaxRate { get; }
    public decimal TaxAmount { get; }
    /// <summary>The price tolerance this line was matched against when the bill was entered (item, else vendor, else company default).</summary>
    public decimal TolerancePercent { get; }
    public bool WithinTolerance => ThreeWayMatch.WithinTolerance(OrderUnitCost, UnitCost, TolerancePercent);
    /// <summary>The billed amount before tax.</summary>
    public decimal LineTotal => decimal.Round(Quantity * UnitCost, 2);
    public decimal LineGross => LineTotal + TaxAmount;
    public decimal AccruedTotal => decimal.Round(Quantity * OrderUnitCost, 2);
    /// <summary>Billed price against order price, before tax.</summary>
    public decimal PriceVariance => LineTotal - AccruedTotal;
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
        VendorId vendorId, DateOnly billDate, DateOnly dueDate, Guid journalId, string journalNumber, bool varianceOverridden,
        DateTimeOffset createdAt) : base(id)
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
        VarianceOverridden = varianceOverridden;
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
    /// <summary>The part of the bill cancelled by debit notes (never more than the balance at the time).</summary>
    public decimal AmountDebited { get; private set; }
    public BillStatus Status { get; private set; } = BillStatus.Open;
    /// <summary>True when a price variance beyond tolerance was approved by someone with the override permission.</summary>
    public bool VarianceOverridden { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<VendorBillLine> Lines => _lines.AsReadOnly();
    /// <summary>The billed amount before tax.</summary>
    public decimal Subtotal => _lines.Sum(x => x.LineTotal);
    public decimal TaxTotal => _lines.Sum(x => x.TaxAmount);
    /// <summary>What we owe the vendor: subtotal plus input tax.</summary>
    public decimal Total => Subtotal + TaxTotal;
    /// <summary>The part of the bill that clears the goods-received accrual.</summary>
    public decimal AccruedTotal => _lines.Sum(x => x.AccruedTotal);
    public decimal PriceVariance => Subtotal - AccruedTotal;
    public decimal Balance => Total - AmountPaid - AmountDebited;

    public static VendorBill Create(CompanyId companyId, string number, string? vendorReference, PurchaseOrder order,
        DateOnly billDate, int paymentTermsDays, IReadOnlyList<VendorBillLine> lines, Guid journalId, string journalNumber,
        bool varianceOverridden, DateTimeOffset createdAt)
    {
        if (lines.Count == 0) throw new DomainException("A bill must contain at least one line.");
        var bill = new VendorBill(new VendorBillId(Guid.NewGuid()), companyId, number, vendorReference, order.Id, order.VendorId,
            billDate, billDate.AddDays(paymentTermsDays), journalId, journalNumber, varianceOverridden, createdAt);
        bill._lines.AddRange(lines);
        return bill;
    }

    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Payment amount must be positive.");
        if (amount > Balance) throw new DomainException($"Payment exceeds the bill balance of {Balance:N2}.");
        AmountPaid += amount;
        UpdateStatus();
    }

    /// <summary>Reduces the balance by a debit note. Only the open balance can be debited here; any excess is refunded by the vendor.</summary>
    public void ApplyDebit(decimal amount)
    {
        if (amount < 0) throw new DomainException("Debit amount cannot be negative.");
        if (amount > Balance) throw new DomainException($"Debit exceeds the bill balance of {Balance:N2}.");
        AmountDebited += amount;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (Balance != 0) return;
        // Settled by debit notes alone is "Debited"; anything that involved a payment is "Paid".
        Status = AmountPaid == 0 && AmountDebited > 0 ? BillStatus.Debited : BillStatus.Paid;
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

public sealed record VendorReturnLine
{
    public VendorReturnLine(Guid itemId, string sku, string description, decimal quantity, decimal unitCost)
    {
        if (quantity <= 0) throw new DomainException("Returned quantity must be positive.");
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
    /// <summary>The order price the goods came in at; they leave stock at the same cost.</summary>
    public decimal UnitCost { get; }
    public decimal LineTotal => decimal.Round(Quantity * UnitCost, 2);
}

/// <summary>Goods sent back to a vendor (return to vendor). Reverses the receipt: Dr GRNI / Cr Inventory.</summary>
public sealed class VendorReturn : AggregateRoot<VendorReturnId>
{
    private readonly List<VendorReturnLine> _lines = [];

    private VendorReturn()
    {
        Number = string.Empty;
        Reason = string.Empty;
        Reference = string.Empty;
        JournalNumber = string.Empty;
    }

    private VendorReturn(VendorReturnId id, CompanyId companyId, string number, PurchaseOrderId orderId, VendorId vendorId,
        DateOnly returnDate, string reason, string? reference, Guid journalId, string journalNumber, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        PurchaseOrderId = orderId;
        VendorId = vendorId;
        ReturnDate = returnDate;
        Reason = reason;
        Reference = reference?.Trim() ?? string.Empty;
        JournalId = journalId;
        JournalNumber = journalNumber;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public PurchaseOrderId PurchaseOrderId { get; private set; }
    public VendorId VendorId { get; private set; }
    public DateOnly ReturnDate { get; private set; }
    public string Reason { get; private set; }
    /// <summary>The vendor's return authorisation or our dispatch note number.</summary>
    public string Reference { get; private set; }
    public Guid JournalId { get; private set; }
    public string JournalNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<VendorReturnLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(x => x.LineTotal);

    public static VendorReturn Create(CompanyId companyId, string number, PurchaseOrderId orderId, VendorId vendorId,
        DateOnly returnDate, string reason, string? reference, IReadOnlyList<VendorReturnLine> lines, Guid journalId,
        string journalNumber, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A reason is required for a return to vendor.");
        if (lines.Count == 0) throw new DomainException("A return must contain at least one line.");
        var ret = new VendorReturn(new VendorReturnId(Guid.NewGuid()), companyId, number, orderId, vendorId, returnDate,
            reason.Trim(), reference, journalId, journalNumber, createdAt);
        ret._lines.AddRange(lines);
        return ret;
    }
}

public sealed record DebitNoteLine
{
    public DebitNoteLine(Guid itemId, string sku, string description, decimal quantity, decimal orderUnitCost, decimal unitCost,
        decimal taxRate = 0m, decimal taxAmount = 0m)
    {
        if (quantity <= 0) throw new DomainException("Debited quantity must be positive.");
        if (taxAmount < 0) throw new DomainException("Line tax cannot be negative.");
        TaxRate = taxRate;
        TaxAmount = taxAmount;
        ItemId = itemId;
        Sku = sku;
        Description = description;
        Quantity = quantity;
        OrderUnitCost = orderUnitCost;
        UnitCost = unitCost;
    }

    public Guid ItemId { get; }
    public string Sku { get; }
    public string Description { get; }
    public decimal Quantity { get; }
    /// <summary>The order price: what the goods receipt accrued (and the return reversed).</summary>
    public decimal OrderUnitCost { get; }
    /// <summary>The price the vendor billed, which is what the vendor now credits.</summary>
    public decimal UnitCost { get; }
    /// <summary>The bill line's rate, and this debit's share of the input tax charged on it.</summary>
    public decimal TaxRate { get; }
    public decimal TaxAmount { get; }
    public decimal LineTotal => decimal.Round(Quantity * UnitCost, 2);
    public decimal LineGross => LineTotal + TaxAmount;
    public decimal AccruedTotal => decimal.Round(Quantity * OrderUnitCost, 2);
    public decimal PriceVariance => LineTotal - AccruedTotal;
}

/// <summary>
/// The vendor's credit for billed goods that were returned. Dr Payables (billed price) / Cr GRNI (order price) with the
/// difference to Purchase Price Variance. The credit first reduces the bill balance; the rest is refundable by the vendor.
/// </summary>
public sealed class DebitNote : AggregateRoot<DebitNoteId>
{
    private readonly List<DebitNoteLine> _lines = [];

    private DebitNote()
    {
        Number = string.Empty;
        Reason = string.Empty;
        JournalNumber = string.Empty;
    }

    private DebitNote(DebitNoteId id, CompanyId companyId, string number, PurchaseOrderId orderId, VendorBillId billId, VendorId vendorId,
        DateOnly debitDate, string reason, Guid journalId, string journalNumber, decimal amountApplied, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        PurchaseOrderId = orderId;
        VendorBillId = billId;
        VendorId = vendorId;
        DebitDate = debitDate;
        Reason = reason;
        JournalId = journalId;
        JournalNumber = journalNumber;
        AmountApplied = amountApplied;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public PurchaseOrderId PurchaseOrderId { get; private set; }
    public VendorBillId VendorBillId { get; private set; }
    public VendorId VendorId { get; private set; }
    public DateOnly DebitDate { get; private set; }
    public string Reason { get; private set; }
    public Guid JournalId { get; private set; }
    public string JournalNumber { get; private set; }
    /// <summary>The part that reduced the bill's open balance.</summary>
    public decimal AmountApplied { get; private set; }
    /// <summary>Money the vendor has paid back against this note.</summary>
    public decimal AmountRefunded { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<DebitNoteLine> Lines => _lines.AsReadOnly();
    public decimal Subtotal => _lines.Sum(x => x.LineTotal);
    public decimal TaxTotal => _lines.Sum(x => x.TaxAmount);
    public decimal Total => Subtotal + TaxTotal;
    public decimal AccruedTotal => _lines.Sum(x => x.AccruedTotal);
    public decimal PriceVariance => Subtotal - AccruedTotal;
    /// <summary>What the vendor still owes us for this note (credit beyond the bill balance, not yet refunded).</summary>
    public decimal RefundDue => Total - AmountApplied - AmountRefunded;

    public static DebitNote Create(CompanyId companyId, string number, VendorBill bill, DateOnly debitDate, string reason,
        IReadOnlyList<DebitNoteLine> lines, Guid journalId, string journalNumber, decimal amountApplied, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A reason is required for a debit note.");
        if (lines.Count == 0) throw new DomainException("A debit note must contain at least one line.");
        var note = new DebitNote(new DebitNoteId(Guid.NewGuid()), companyId, number, bill.PurchaseOrderId, bill.Id, bill.VendorId,
            debitDate, reason.Trim(), journalId, journalNumber, amountApplied, createdAt);
        note._lines.AddRange(lines);
        if (amountApplied < 0 || amountApplied > note.Total) throw new DomainException("The applied debit must be between 0 and the debit note total.");
        return note;
    }

    public void ApplyRefund(decimal amount)
    {
        if (amount <= 0) throw new DomainException("Refund amount must be positive.");
        if (amount > RefundDue) throw new DomainException($"Refund exceeds the amount the vendor owes of {RefundDue:N2}.");
        AmountRefunded += amount;
    }
}

/// <summary>Money received back from a vendor against a debit note (Dr Cash or Bank / Cr Payables).</summary>
public sealed class VendorRefund : AggregateRoot<VendorRefundId>
{
    private VendorRefund()
    {
        Number = string.Empty;
        Reference = string.Empty;
        JournalNumber = string.Empty;
    }

    private VendorRefund(VendorRefundId id, CompanyId companyId, string number, DebitNoteId debitNoteId, VendorId vendorId,
        DateOnly refundDate, decimal amount, PaymentMethod method, string? reference, string journalNumber, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        DebitNoteId = debitNoteId;
        VendorId = vendorId;
        RefundDate = refundDate;
        Amount = amount;
        Method = method;
        Reference = reference?.Trim() ?? string.Empty;
        JournalNumber = journalNumber;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public DebitNoteId DebitNoteId { get; private set; }
    public VendorId VendorId { get; private set; }
    public DateOnly RefundDate { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string Reference { get; private set; }
    public string JournalNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static VendorRefund Create(CompanyId companyId, string number, DebitNoteId debitNoteId, VendorId vendorId,
        DateOnly refundDate, decimal amount, PaymentMethod method, string? reference, string journalNumber, DateTimeOffset createdAt)
    {
        if (amount <= 0) throw new DomainException("Refund amount must be positive.");
        return new VendorRefund(new VendorRefundId(Guid.NewGuid()), companyId, number, debitNoteId, vendorId, refundDate,
            amount, method, reference, journalNumber, createdAt);
    }
}
