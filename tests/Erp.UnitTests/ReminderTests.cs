using System.Reflection;
using Erp.Application.Abstractions;
using Erp.Modules.Inventory.Application;
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Platform.Application;
using Erp.Modules.Purchasing.Application;
using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Sales.Application;
using Erp.Modules.Sales.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class ReminderTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(int daysAgo, int hour = 9) => new DateTimeOffset(2026, 11, 21, hour, 0, 0, TimeSpan.Zero).AddDays(-daysAgo);

    // ---- options --------------------------------------------------------------------------------------------------

    [Fact]
    public void Defaults_are_valid_and_remind_daily_from_eight_utc()
    {
        var o = new ReminderOptions();
        Assert.Empty(o.Validate());
        Assert.Equal((true, 8, 1), (o.Enabled, o.HourUtc, o.RepeatDays));
    }

    [Fact]
    public void Every_bad_value_is_reported_with_its_key()
    {
        var errors = new ReminderOptions { HourUtc = 24, RepeatDays = 0, IntervalMinutes = 0, InitialDelaySeconds = -1 }.Validate();
        Assert.Equal(4, errors.Count);
        Assert.All(errors, e => Assert.StartsWith("Reminders:", e));
    }

    [Theory]
    [InlineData(9, null, true)]       // never sent
    [InlineData(7, null, false)]      // before the hour
    [InlineData(9, 0, false)]         // already sent today
    [InlineData(9, 1, true)]          // sent yesterday
    [InlineData(8, 1, true)]          // exactly the hour
    [InlineData(8, 0, false)]
    public void A_daily_reminder_is_due_once_per_calendar_day_after_the_hour(int hourNow, int? sentDaysAgo, bool due)
    {
        var now = new DateTimeOffset(2026, 11, 21, hourNow, 30, 0, TimeSpan.Zero);
        DateTimeOffset? lastSent = sentDaysAgo is { } d ? new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero).AddDays(-d) : null;
        Assert.Equal(due, new ReminderOptions().IsDue(now, lastSent));
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(30, true)]
    public void A_weekly_reminder_waits_for_the_period(int daysAgo, bool due) =>
        Assert.Equal(due, new ReminderOptions { RepeatDays = 7 }.IsDue(Now, At(daysAgo, 10)));

    [Fact]
    public void Sending_late_one_day_does_not_delay_the_next_day()
    {
        // Sent at 08:55 yesterday, checked at 08:05 today: a calendar-day comparison still counts it as due.
        Assert.True(new ReminderOptions().IsDue(new DateTimeOffset(2026, 11, 21, 8, 5, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 20, 8, 55, 0, TimeSpan.Zero)));
    }

    // ---- fakes ----------------------------------------------------------------------------------------------------

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class FixedCompany : ICompanyContext { public CompanyId CompanyId => Company; public bool HasCompany => true; }

    private sealed class FakeNotifier : INotifier
    {
        public DateTimeOffset? Last { get; set; }
        public List<(string Permission, NotificationMessage Message)> Sent { get; } = [];
        public List<string> Resolved { get; } = [];
        public Task NotifyUsersAsync(IEnumerable<Guid> userIds, NotificationMessage message, CancellationToken ct) => throw new NotSupportedException();
        public Task NotifyPermissionHoldersAsync(string permissionCode, NotificationMessage message, Guid? exceptUserId, CancellationToken ct)
        {
            Sent.Add((permissionCode, message));
            return Task.CompletedTask;
        }
        public Task ResolveSubjectAsync(string subject, CancellationToken ct) { Resolved.Add(subject); return Task.CompletedTask; }
        public Task<DateTimeOffset?> LastSentAsync(string subject, CancellationToken ct) => Task.FromResult(Last);
    }

    private static T Stub<T>(string method, Func<object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, NotificationTests.Proxy<T>>();
        ((NotificationTests.Proxy<T>)(object)proxy).Handlers[method] = handler;
        return proxy;
    }

    // ---- invoices -------------------------------------------------------------------------------------------------

    private static SalesInvoice Invoice(string number, DateOnly invoiceDate, int terms, decimal price = 100m)
    {
        var order = SalesOrder.Create(Company, "SO-" + number, new CustomerId(Guid.NewGuid()), invoiceDate, null, Now);
        order.AddLine(Guid.NewGuid(), "SKU", "Item", 1m, price);
        return SalesInvoice.Create(Company, number, order, invoiceDate, terms,
            order.Lines.Select(l => new SalesInvoiceLine(l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitPrice, 0m)).ToArray(), Guid.NewGuid(), "JE", Now);
    }

    private static (OverdueInvoiceReminder Job, FakeNotifier Notifier) InvoiceJob(IReadOnlyList<SalesInvoice> overdue, ReminderOptions? options = null)
    {
        var notifier = new FakeNotifier();
        var store = Stub<ISalesStore>("ListOverdueInvoicesAsync", a =>
        {
            Assert.Equal(Company, (CompanyId)a[0]!);
            Assert.Equal(new DateOnly(2026, 11, 21), (DateOnly)a[1]!);
            return Task.FromResult(overdue);
        });
        return (new OverdueInvoiceReminder(store, notifier, new FixedCompany(), new FixedClock(), options ?? new ReminderOptions()), notifier);
    }

    [Fact]
    public async Task Overdue_invoices_are_summarised_for_the_people_who_collect_payments()
    {
        var (job, notifier) = InvoiceJob([Invoice("INV-1", new DateOnly(2026, 9, 1), 30, 1000m), Invoice("INV-2", new DateOnly(2026, 10, 1), 30, 250.5m)]);
        Assert.Equal(1, await job.RunAsync(default));

        var (permission, message) = Assert.Single(notifier.Sent);
        Assert.Equal(OverdueInvoiceReminder.Recipients, permission);
        Assert.Equal(("overdue-invoice", "overdue-invoices", "/invoices", true), (message.Category, message.Subject, message.Link, message.Email));
        Assert.Equal("2 invoices are overdue", message.Title);
        Assert.Contains("1,250.50 is outstanding", message.Body);
        Assert.Contains("INV-1", message.Body);                      // the oldest
        Assert.Contains("was due 2026-10-01 (51 days ago)", message.Body);
        Assert.Equal(["overdue-invoices"], notifier.Resolved);       // the previous summary is replaced
    }

    [Fact]
    public async Task A_single_overdue_invoice_reads_naturally()
    {
        var (job, notifier) = InvoiceJob([Invoice("INV-1", new DateOnly(2026, 10, 21), 30)]);
        await job.RunAsync(default);
        Assert.Equal("1 invoice is overdue", notifier.Sent.Single().Message.Title);
        Assert.Contains("(1 day ago)", notifier.Sent.Single().Message.Body);
    }

    [Fact]
    public async Task Nothing_overdue_sends_nothing_and_clears_the_old_summary()
    {
        var (job, notifier) = InvoiceJob([]);
        Assert.Equal(0, await job.RunAsync(default));
        Assert.Empty(notifier.Sent);
        Assert.Equal(["overdue-invoices"], notifier.Resolved);
    }

    [Fact]
    public async Task A_summary_already_sent_today_is_not_repeated_and_the_old_one_is_kept()
    {
        var (job, notifier) = InvoiceJob([Invoice("INV-1", new DateOnly(2026, 9, 1), 30)]);
        notifier.Last = At(0, 8);
        Assert.Equal(0, await job.RunAsync(default));
        Assert.Empty(notifier.Sent);
        Assert.Empty(notifier.Resolved);
    }

    [Fact]
    public async Task It_is_sent_again_the_next_day()
    {
        var (job, notifier) = InvoiceJob([Invoice("INV-1", new DateOnly(2026, 9, 1), 30)]);
        notifier.Last = At(1, 8);
        Assert.Equal(1, await job.RunAsync(default));
    }

    [Fact]
    public async Task Nothing_is_sent_before_the_configured_hour()
    {
        var (job, notifier) = InvoiceJob([Invoice("INV-1", new DateOnly(2026, 9, 1), 30)], new ReminderOptions { HourUtc = 12 });
        Assert.Equal(0, await job.RunAsync(default));
        Assert.Empty(notifier.Sent);
    }

    // ---- bills ----------------------------------------------------------------------------------------------------

    private static VendorBill Bill(string number, DateOnly billDate, int terms)
    {
        var order = PurchaseOrder.Create(Company, "PO-" + number, new VendorId(Guid.NewGuid()), billDate, null, Now);
        order.AddLine(Guid.NewGuid(), "A", "Item A", 10m, 25m);
        order.Open();
        order.Release();
        return VendorBill.Create(Company, number, null, order, billDate, terms,
            order.Lines.Select(l => new VendorBillLine(l.ItemId, l.Sku, l.Description, l.Quantity, l.UnitCost, l.UnitCost)).ToArray(), Guid.NewGuid(), "JE", false, Now);
    }

    [Fact]
    public async Task Overdue_bills_are_summarised_for_the_people_who_pay_vendors()
    {
        var notifier = new FakeNotifier();
        var store = Stub<IPurchasingStore>("ListOverdueBillsAsync", _ => Task.FromResult<IReadOnlyList<VendorBill>>([Bill("BILL-7", new DateOnly(2026, 9, 1), 30)]));
        var job = new OverdueBillReminder(store, notifier, new FixedCompany(), new FixedClock(), new ReminderOptions());
        Assert.Equal(1, await job.RunAsync(default));
        var (permission, message) = Assert.Single(notifier.Sent);
        Assert.Equal(OverdueBillReminder.Recipients, permission);
        Assert.Equal(("overdue-bill", "overdue-bills", "/bills", true), (message.Category, message.Subject, message.Link, message.Email));
        Assert.Equal("1 bill is overdue", message.Title);
        Assert.Contains("250.00 is outstanding", message.Body);
        Assert.Contains("BILL-7", message.Body);
    }

    // ---- low stock ------------------------------------------------------------------------------------------------

    private static Item Stocked(string sku, decimal? level, bool active = true)
    {
        var item = Item.Create(Company, sku, "Name of " + sku, "EA", 10m, 5m, Now, reorderLevel: level);
        if (!active) item.Deactivate();
        return item;
    }

    private static (LowStockReminder Job, FakeNotifier Notifier) StockJob(IReadOnlyList<Item> items, Func<Item, decimal> onHand, ReminderOptions? options = null)
    {
        var notifier = new FakeNotifier();
        var itemStore = Stub<IItemStore>("ListAsync", _ => Task.FromResult(items));
        var stock = Stub<IStockStore>("OnHandByItemAsync", _ => Task.FromResult<IReadOnlyList<ItemQuantity>>(items.Select(i => new ItemQuantity(i.Id, onHand(i), 0m)).ToList()));
        return (new LowStockReminder(itemStore, stock, notifier, new FixedCompany(), new FixedClock(), options ?? new ReminderOptions()), notifier);
    }

    [Fact]
    public async Task Low_items_are_summarised_most_urgent_first_for_the_people_who_manage_stock()
    {
        var items = new[] { Stocked("A", 10m), Stocked("B", 10m), Stocked("C", 10m), Stocked("D", null), Stocked("E", 10m, active: false) };
        var qty = new Dictionary<string, decimal> { ["A"] = 4m, ["B"] = 0m, ["C"] = 11m, ["D"] = 0m, ["E"] = 0m };
        var (job, notifier) = StockJob(items, i => qty[i.Sku]);
        Assert.Equal(1, await job.RunAsync(default));

        var (permission, message) = Assert.Single(notifier.Sent);
        Assert.Equal(LowStockReminder.Recipients, permission);
        Assert.Equal(("low-stock", "low-stock", "/items", true), (message.Category, message.Subject, message.Link, message.Email));
        Assert.Equal("2 items are low on stock", message.Title);       // A and B; C is above, D is unmonitored, E is inactive
        Assert.StartsWith("1 out of stock. Lowest: B (Name of B) has 0 EA left, reorder level 10.", message.Body);
        Assert.Equal(["low-stock"], notifier.Resolved);
    }

    [Fact]
    public async Task One_low_item_reads_naturally_and_decimals_are_trimmed()
    {
        var (job, notifier) = StockJob([Stocked("A", 2.5m)], _ => 1.25m);
        await job.RunAsync(default);
        var message = notifier.Sent.Single().Message;
        Assert.Equal("1 item is low on stock", message.Title);
        Assert.Equal("Lowest: A (Name of A) has 1.25 EA left, reorder level 2.5.", message.Body);
    }

    [Fact]
    public async Task Nothing_low_sends_nothing_and_clears_the_old_summary()
    {
        var (job, notifier) = StockJob([Stocked("A", 5m), Stocked("B", null)], i => 50m);
        Assert.Equal(0, await job.RunAsync(default));
        Assert.Empty(notifier.Sent);
        Assert.Equal(["low-stock"], notifier.Resolved);
    }

    [Fact]
    public async Task Low_stock_is_not_repeated_within_the_period_and_waits_for_the_hour()
    {
        var (job, notifier) = StockJob([Stocked("A", 5m)], _ => 0m);
        notifier.Last = At(0, 8);
        Assert.Equal(0, await job.RunAsync(default));
        Assert.Empty(notifier.Sent);

        var (early, earlyNotifier) = StockJob([Stocked("A", 5m)], _ => 0m, new ReminderOptions { HourUtc = 12 });
        Assert.Equal(0, await early.RunAsync(default));
        Assert.Empty(earlyNotifier.Sent);
    }

    // ---- wiring ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Recipient_permissions_match_the_catalogue_and_the_categories_are_known()
    {
        Assert.Equal(Permissions.Sales.PaymentsRecord, OverdueInvoiceReminder.Recipients);
        Assert.Equal(Permissions.Purchasing.PaymentsRecord, OverdueBillReminder.Recipients);
        Assert.Contains(OverdueInvoiceReminder.Category, NotificationCategories.Known);
        Assert.Contains(OverdueBillReminder.Category, NotificationCategories.Known);
        Assert.Equal(Permissions.Inventory.StockManage, LowStockReminder.Recipients);
        Assert.Contains(LowStockReminder.Category, NotificationCategories.Known);
    }
}
