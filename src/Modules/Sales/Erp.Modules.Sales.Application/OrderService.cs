using Erp.Application.Abstractions;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;

namespace Erp.Modules.Sales.Application;

public sealed class OrderService(
    ISalesStore store,
    IInventoryCatalog catalog,
    IDocumentNumberGenerator numbers,
    ICompanyContext companyContext,
    IClock clock,
    IAuditRecorder audit) : IOrderService
{
    public const string OrderSequence = "SO";

    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        var customer = await store.GetCustomerAsync(companyId, new CustomerId(request.CustomerId), cancellationToken)
            ?? throw new DomainException("The customer was not found.");
        if (!customer.IsActive) throw new DomainException($"Customer '{customer.Code}' is inactive.");
        if (request.Lines.Count == 0) throw new DomainException("A sales order must contain at least one line.");

        var items = (await catalog.GetItemsAsync(request.Lines.Select(x => x.ItemId).Distinct().ToArray(), cancellationToken))
            .ToDictionary(x => x.Id);
        var number = await numbers.NextAsync(OrderSequence, "SO-", cancellationToken);
        var order = SalesOrder.Create(
            companyId, number, customer.Id, request.OrderDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            request.Notes, clock.UtcNow);
        foreach (var line in request.Lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item)) throw new DomainException("An item on the order was not found.");
            if (!item.IsActive) throw new DomainException($"Item '{item.Sku}' is inactive.");
            order.AddLine(item.Id, item.Sku, item.Name, line.Quantity, line.UnitPrice ?? item.UnitPrice);
        }

        await store.AddOrderAsync(order, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync("Sales", "order.created", "SalesOrder", order.Id.Value.ToString(), order.Number, true,
            new Dictionary<string, object?> { ["Customer"] = customer.Code, ["Total"] = order.Total }, cancellationToken);
        return Map(order, customer.Name);
    }

    public async Task<IReadOnlyList<OrderResponse>> ListAsync(int take, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        var orders = await store.ListOrdersAsync(companyId, Math.Clamp(take, 1, 500), cancellationToken);
        var names = (await store.ListCustomersAsync(companyId, cancellationToken)).ToDictionary(x => x.Id, x => x.Name);
        return orders.Select(o => Map(o, names.GetValueOrDefault(o.CustomerId, string.Empty))).ToArray();
    }

    public async Task<OrderResponse?> GetAsync(SalesOrderId id, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        var order = await store.GetOrderAsync(companyId, id, cancellationToken);
        if (order is null) return null;
        var customer = await store.GetCustomerAsync(companyId, order.CustomerId, cancellationToken);
        return Map(order, customer?.Name ?? string.Empty);
    }

    public async Task<OrderResponse?> TransitionAsync(SalesOrderId id, OrderAction action, CancellationToken cancellationToken)
    {
        var companyId = SalesCompany.Require(companyContext);
        return await store.InLockedTransactionAsync<OrderResponse?>(companyId, $"order:{id.Value}", async ct =>
        {
            var order = await store.GetOrderAsync(companyId, id, ct);
            if (order is null) return null;
            switch (action)
            {
                case OrderAction.Open: order.Open(); break;
                case OrderAction.Release: order.Release(); break;
                case OrderAction.Cancel: order.Cancel(); break;
            }

            await store.SaveChangesAsync(ct);
            await audit.RecordAsync("Sales", $"order.{action.ToString().ToLowerInvariant()}", "SalesOrder",
                order.Id.Value.ToString(), order.Number, true, null, ct);
            var customer = await store.GetCustomerAsync(companyId, order.CustomerId, ct);
            return Map(order, customer?.Name ?? string.Empty);
        }, cancellationToken);
    }

    internal static OrderResponse Map(SalesOrder o, string customerName) => new(
        o.Id.Value, o.Number, o.CustomerId.Value, customerName, o.OrderDate, o.Notes, o.Status, o.Total, o.InvoiceId?.Value,
        o.Lines.Select(l => new OrderLineResponse(l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitPrice, l.LineTotal)).ToArray());
}
