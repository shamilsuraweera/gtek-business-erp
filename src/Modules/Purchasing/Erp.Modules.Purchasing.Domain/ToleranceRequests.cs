using Erp.SharedKernel;

namespace Erp.Modules.Purchasing.Domain;

/// <summary>One line of a requested change: the overrides it should end up with (a null clears that override).</summary>
public sealed record ToleranceRequestLine
{
    public ToleranceRequestLine(Guid itemId, string sku, decimal? priceTolerancePercent, decimal? quantityTolerancePercent, decimal? underDeliveryTolerancePercent)
    {
        ItemId = itemId;
        Sku = sku;
        PriceTolerancePercent = ThreeWayMatch.ValidateTolerance(priceTolerancePercent);
        QuantityTolerancePercent = ThreeWayMatch.ValidateTolerance(quantityTolerancePercent, "Over-delivery");
        UnderDeliveryTolerancePercent = ThreeWayMatch.ValidateTolerance(underDeliveryTolerancePercent, "Under-delivery");
    }

    public Guid ItemId { get; }
    public string Sku { get; }
    public decimal? PriceTolerancePercent { get; }
    public decimal? QuantityTolerancePercent { get; }
    public decimal? UnderDeliveryTolerancePercent { get; }
}

/// <summary>
/// A request to set per-order tolerance overrides (ADR 0021) made by someone who may not set them directly. Someone holding the
/// approve permission decides it: approval applies the overrides, rejection leaves the order untouched. The requester can withdraw it.
/// </summary>
public sealed class ToleranceRequest : AggregateRoot<ToleranceRequestId>
{
    private readonly List<ToleranceRequestLine> _lines = [];

    private ToleranceRequest()
    {
        Number = string.Empty;
        Reason = string.Empty;
    }

    private ToleranceRequest(ToleranceRequestId id, CompanyId companyId, string number, PurchaseOrderId orderId, Guid requestedBy,
        string reason, DateTimeOffset createdAt) : base(id)
    {
        CompanyId = companyId;
        Number = number;
        PurchaseOrderId = orderId;
        RequestedBy = requestedBy;
        Reason = reason;
        Status = ToleranceRequestStatus.Pending;
        CreatedAt = createdAt;
    }

    public CompanyId CompanyId { get; private set; }
    public string Number { get; private set; }
    public PurchaseOrderId PurchaseOrderId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public string Reason { get; private set; }
    public ToleranceRequestStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }
    public IReadOnlyCollection<ToleranceRequestLine> Lines => _lines.AsReadOnly();

    public static ToleranceRequest Create(CompanyId companyId, string number, PurchaseOrderId orderId, Guid requestedBy, string reason,
        IReadOnlyList<ToleranceRequestLine> lines, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Say why the tolerances need to change.");
        if (reason.Trim().Length > 500) throw new DomainException("The reason is too long (500 characters at most).");
        if (lines.Count == 0) throw new DomainException("Specify at least one line.");
        if (lines.Select(x => x.ItemId).Distinct().Count() != lines.Count) throw new DomainException("Each item can appear only once.");
        var request = new ToleranceRequest(new ToleranceRequestId(Guid.NewGuid()), companyId, number, orderId, requestedBy, reason.Trim(), createdAt);
        request._lines.AddRange(lines);
        return request;
    }

    /// <summary>The person who raised a request cannot decide it: four eyes. (Someone who may set tolerances can do so directly.)</summary>
    public void Approve(Guid decidedBy, string? note, DateTimeOffset at)
    {
        EnsurePending();
        EnsureNotRequester(decidedBy, "approve");
        Decide(ToleranceRequestStatus.Approved, decidedBy, note, at);
    }

    public void Reject(Guid decidedBy, string? note, DateTimeOffset at)
    {
        EnsurePending();
        EnsureNotRequester(decidedBy, "reject");
        if (string.IsNullOrWhiteSpace(note)) throw new DomainException("Say why the request is rejected.");
        Decide(ToleranceRequestStatus.Rejected, decidedBy, note, at);
    }

    public void Withdraw(Guid by, DateTimeOffset at)
    {
        EnsurePending();
        if (by != RequestedBy) throw new DomainException("Only the person who raised a request can withdraw it.");
        Decide(ToleranceRequestStatus.Withdrawn, by, null, at);
    }

    private void Decide(ToleranceRequestStatus status, Guid by, string? note, DateTimeOffset at)
    {
        Status = status;
        DecidedBy = by;
        DecidedAt = at;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private void EnsurePending()
    {
        if (Status != ToleranceRequestStatus.Pending)
            throw new DomainException($"This request has already been {Status.ToString().ToLowerInvariant()}.");
    }

    private void EnsureNotRequester(Guid by, string verb)
    {
        if (by == RequestedBy) throw new DomainException($"You cannot {verb} your own request; ask another approver.");
    }
}
