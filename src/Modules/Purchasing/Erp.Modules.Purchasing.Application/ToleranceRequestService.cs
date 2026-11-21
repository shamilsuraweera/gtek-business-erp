using Erp.Application.Abstractions;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Purchasing.Application;

public sealed record ToleranceRequestLineInput(Guid ItemId, decimal? PriceTolerancePercent, decimal? QuantityTolerancePercent, decimal? UnderDeliveryTolerancePercent);
public sealed record CreateToleranceRequest(string Reason, IReadOnlyList<ToleranceRequestLineInput> Lines);
public sealed record ToleranceDecisionRequest(string? Note);

/// <summary>A requested line, with the order line's current overrides so the approver sees what would change.</summary>
public sealed record ToleranceRequestLineResponse(
    Guid ItemId, string Sku, string Description,
    decimal? PriceTolerancePercent, decimal? QuantityTolerancePercent, decimal? UnderDeliveryTolerancePercent,
    decimal? CurrentPriceOverride, decimal? CurrentQuantityOverride, decimal? CurrentUnderDeliveryOverride);

public sealed record ToleranceRequestResponse(
    Guid Id, string Number, Guid PurchaseOrderId, string OrderNumber, string VendorName, ToleranceRequestStatus Status, string Reason,
    Guid RequestedById, string RequestedBy, DateTimeOffset RequestedAt, string? DecidedBy, DateTimeOffset? DecidedAt, string? DecisionNote,
    IReadOnlyList<ToleranceRequestLineResponse> Lines);

public interface IToleranceRequestService
{
    Task<ToleranceRequestResponse?> CreateAsync(PurchaseOrderId orderId, CreateToleranceRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ToleranceRequestResponse>> ListAsync(ToleranceRequestStatus? status, PurchaseOrderId? orderId, int take, CancellationToken cancellationToken);
    Task<ToleranceRequestResponse?> ApproveAsync(ToleranceRequestId id, ToleranceDecisionRequest? request, CancellationToken cancellationToken);
    Task<ToleranceRequestResponse?> RejectAsync(ToleranceRequestId id, ToleranceDecisionRequest request, CancellationToken cancellationToken);
    Task<ToleranceRequestResponse?> WithdrawAsync(ToleranceRequestId id, CancellationToken cancellationToken);
}

/// <summary>The order-tolerance change shared by the direct path (ADR 0021) and approved requests (ADR 0022).</summary>
internal static class OrderTolerances
{
    public static async Task ApplyAsync(
        PurchaseOrder order, Vendor vendor, IReadOnlyCollection<(Guid ItemId, decimal? Price, decimal? Quantity, decimal? UnderDelivery)> lines,
        IInventoryCatalog catalog, CancellationToken cancellationToken)
    {
        order.SetTolerances(lines);

        // A looser under-delivery tolerance may complete a part-received order right away.
        var items = (await catalog.GetItemsAsync(order.Lines.Select(l => l.ItemId).ToArray(), cancellationToken)).ToDictionary(x => x.Id);
        order.ReevaluateReceipt(order.Lines.ToDictionary(l => l.ItemId, l => ThreeWayMatch.EffectiveUnderDeliveryTolerance(
            items.GetValueOrDefault(l.ItemId)?.UnderDeliveryTolerancePercent, vendor.UnderDeliveryTolerancePercent, l.UnderDeliveryTolerancePercent)));
    }
}

public sealed class ToleranceRequestService(
    IPurchasingStore store,
    IInventoryCatalog catalog,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IUserDirectory users,
    IDocumentNumberGenerator numbers,
    IClock clock,
    IAuditRecorder audit,
    INotifier notifier,
    ILogger<ToleranceRequestService> logger) : IToleranceRequestService
{
    public const string RequestSequence = "TOL";

    /// <summary>The permission that approves requests (Platform's purchasing.orders.override-tolerance); a unit test keeps the two in step.</summary>
    public const string ApprovePermission = "purchasing.orders.override-tolerance";

    private static string Subject(Guid requestId) => $"tolerance-request:{requestId}";

    private Guid CurrentUserId() =>
        currentUser.UserId?.Value ?? throw new DomainException("A signed-in user is required.");

    public async Task<ToleranceRequestResponse?> CreateAsync(PurchaseOrderId orderId, CreateToleranceRequest request, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var userId = CurrentUserId();
        var created = await store.InLockedTransactionAsync<ToleranceRequestResponse?>(companyId, $"order:{orderId.Value}", async ct =>
        {
            var order = await store.GetOrderAsync(companyId, orderId, ct);
            if (order is null) return null;
            if (order.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Billed)
                throw new DomainException("Tolerances cannot be changed on a cancelled or fully billed purchase order.");
            if (await store.HasPendingToleranceRequestAsync(companyId, orderId, ct))
                throw new DomainException("This order already has a pending tolerance request. Withdraw it or wait for a decision.");

            // Same-order check up front: each requested item must be on the order.
            var lines = (request.Lines ?? []).Select(l =>
                new ToleranceRequestLine(order.LineFor(l.ItemId).ItemId, order.LineFor(l.ItemId).Sku,
                    l.PriceTolerancePercent, l.QuantityTolerancePercent, l.UnderDeliveryTolerancePercent)).ToList();
            var number = await numbers.NextAsync(RequestSequence, "TOL-", ct);
            var entity = ToleranceRequest.Create(companyId, number, orderId, userId, request.Reason ?? string.Empty, lines, clock.UtcNow);
            await store.AddToleranceRequestAsync(entity, ct);
            await store.SaveChangesAsync(ct);
            await audit.RecordAsync("Purchasing", "order.tolerance-request.created", "ToleranceRequest", entity.Id.Value.ToString(), entity.Number, true,
                new Dictionary<string, object?> { ["Order"] = order.Number, ["Reason"] = entity.Reason, ["Lines"] = entity.Lines.Count }, ct);
            return (await MapAsync([entity], ct))[0];
        }, cancellationToken);

        // After the request is committed: tell the approvers. Best effort; a notification problem never undoes the request.
        if (created is not null)
            await NotifySafelyAsync(() => notifier.NotifyPermissionHoldersAsync(ApprovePermission, new NotificationMessage(
                "approval", $"Tolerance request {created.Number} needs a decision",
                $"{created.RequestedBy} asked to change tolerances on {created.OrderNumber} ({created.VendorName}): {created.Reason}",
                "/tolerance-requests", Subject(created.Id)), userId, cancellationToken));
        return created;
    }

    public async Task<IReadOnlyList<ToleranceRequestResponse>> ListAsync(
        ToleranceRequestStatus? status, PurchaseOrderId? orderId, int take, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var requests = await store.ListToleranceRequestsAsync(companyId, status, orderId, Math.Clamp(take, 1, 500), cancellationToken);
        return await MapAsync(requests, cancellationToken);
    }

    public Task<ToleranceRequestResponse?> ApproveAsync(ToleranceRequestId id, ToleranceDecisionRequest? request, CancellationToken cancellationToken) =>
        DecideAsync(id, "approved", async (entity, order, vendor, userId, ct) =>
        {
            entity.Approve(userId, request?.Note, clock.UtcNow);
            // Applies the overrides; fails (and rolls everything back) if the order can no longer take them.
            await OrderTolerances.ApplyAsync(order, vendor,
                entity.Lines.Select(l => (l.ItemId, l.PriceTolerancePercent, l.QuantityTolerancePercent, l.UnderDeliveryTolerancePercent)).ToList(), catalog, ct);
        }, cancellationToken);

    public Task<ToleranceRequestResponse?> RejectAsync(ToleranceRequestId id, ToleranceDecisionRequest request, CancellationToken cancellationToken) =>
        DecideAsync(id, "rejected", (entity, _, _, userId, _) =>
        {
            entity.Reject(userId, request?.Note, clock.UtcNow);
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<ToleranceRequestResponse?> WithdrawAsync(ToleranceRequestId id, CancellationToken cancellationToken) =>
        DecideAsync(id, "withdrawn", (entity, _, _, userId, _) =>
        {
            entity.Withdraw(userId, clock.UtcNow);
            return Task.CompletedTask;
        }, cancellationToken);

    private async Task<ToleranceRequestResponse?> DecideAsync(
        ToleranceRequestId id, string outcome,
        Func<ToleranceRequest, PurchaseOrder, Vendor, Guid, CancellationToken, Task> decide, CancellationToken cancellationToken)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var userId = CurrentUserId();
        // The decision changes the order, so it is serialized with every other operation on that order.
        var orderId = await store.GetToleranceRequestOrderIdAsync(companyId, id, cancellationToken);
        if (orderId is null) return null;
        var decided = await store.InLockedTransactionAsync<ToleranceRequestResponse?>(companyId, $"order:{orderId.Value.Value}", async ct =>
        {
            var entity = await store.GetToleranceRequestAsync(companyId, id, ct);
            var order = await store.GetOrderAsync(companyId, orderId.Value, ct);
            if (entity is null || order is null) return null;
            var vendor = await store.GetVendorAsync(companyId, order.VendorId, ct) ?? throw new DomainException("The vendor was not found.");
            await decide(entity, order, vendor, userId, ct);
            await store.SaveChangesAsync(ct);
            await audit.RecordAsync("Purchasing", $"order.tolerance-request.{outcome}", "ToleranceRequest", entity.Id.Value.ToString(), entity.Number, true,
                new Dictionary<string, object?> { ["Order"] = order.Number, ["Note"] = entity.DecisionNote, ["OrderStatus"] = order.Status.ToString() }, ct);
            return (await MapAsync([entity], ct))[0];
        }, cancellationToken);

        if (decided is not null)
        {
            // The request no longer needs anyone's attention, then the requester hears the outcome (unless they withdrew it themselves).
            await NotifySafelyAsync(() => notifier.ResolveSubjectAsync(Subject(decided.Id), cancellationToken));
            if (decided.Status is ToleranceRequestStatus.Approved or ToleranceRequestStatus.Rejected)
                await NotifySafelyAsync(() => notifier.NotifyUsersAsync([decided.RequestedById], new NotificationMessage(
                    "decision", $"Tolerance request {decided.Number} {outcome}",
                    $"{decided.DecidedBy} {outcome} your request for {decided.OrderNumber}" + (decided.DecisionNote is null ? "." : $": {decided.DecisionNote}"),
                    $"/purchase-orders/{decided.PurchaseOrderId}", Subject(decided.Id)), cancellationToken));
        }

        return decided;
    }

    private async Task NotifySafelyAsync(Func<Task> notify)
    {
        try { await notify(); }
        catch (Exception ex) { logger.LogWarning(ex, "A notification could not be delivered."); }
    }

    private async Task<IReadOnlyList<ToleranceRequestResponse>> MapAsync(IReadOnlyList<ToleranceRequest> requests, CancellationToken ct)
    {
        var companyId = PurchasingCompany.Require(companyContext);
        var names = await users.GetUserNamesAsync(requests.SelectMany(r => new[] { r.RequestedBy, r.DecidedBy ?? Guid.Empty }).Where(x => x != Guid.Empty), ct);
        var orders = new Dictionary<PurchaseOrderId, (PurchaseOrder Order, string Vendor)>();
        foreach (var orderId in requests.Select(r => r.PurchaseOrderId).Distinct())
        {
            var order = await store.GetOrderAsync(companyId, orderId, ct);
            if (order is null) continue;
            var vendor = await store.GetVendorAsync(companyId, order.VendorId, ct);
            orders[orderId] = (order, vendor?.Name ?? string.Empty);
        }

        return requests.Select(r =>
        {
            orders.TryGetValue(r.PurchaseOrderId, out var o);
            return new ToleranceRequestResponse(
                r.Id.Value, r.Number, r.PurchaseOrderId.Value, o.Order?.Number ?? string.Empty, o.Vendor ?? string.Empty, r.Status, r.Reason,
                r.RequestedBy, names.GetValueOrDefault(r.RequestedBy, "unknown"), r.CreatedAt,
                r.DecidedBy is { } d ? names.GetValueOrDefault(d, "unknown") : null, r.DecidedAt, r.DecisionNote,
                r.Lines.Select(l =>
                {
                    var line = o.Order?.Lines.SingleOrDefault(x => x.ItemId == l.ItemId);
                    return new ToleranceRequestLineResponse(l.ItemId, l.Sku, line?.Description ?? l.Sku,
                        l.PriceTolerancePercent, l.QuantityTolerancePercent, l.UnderDeliveryTolerancePercent,
                        line?.PriceTolerancePercent, line?.QuantityTolerancePercent, line?.UnderDeliveryTolerancePercent);
                }).ToArray());
        }).ToArray();
    }
}
