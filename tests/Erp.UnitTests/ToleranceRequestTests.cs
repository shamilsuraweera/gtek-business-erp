using Erp.Modules.Purchasing.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public class ToleranceRequestTests
{
    private static readonly CompanyId Company = new(Guid.NewGuid());
    private static readonly PurchaseOrderId Order = new(Guid.NewGuid());
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly Guid ItemA = Guid.NewGuid();
    private static readonly Guid ItemB = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private static ToleranceRequest Pending(string reason = "  Vendor warned of a price rise  ") =>
        ToleranceRequest.Create(Company, "TOL-00001", Order, Requester, reason,
            [new ToleranceRequestLine(ItemA, "A", 6m, null, 3m)], Now);

    [Fact]
    public void A_new_request_is_pending_and_keeps_the_trimmed_reason()
    {
        var r = Pending();
        Assert.Equal(ToleranceRequestStatus.Pending, r.Status);
        Assert.Equal("Vendor warned of a price rise", r.Reason);
        Assert.Equal(Requester, r.RequestedBy);
        Assert.Null(r.DecidedBy);
        Assert.Equal((6m, (decimal?)null, 3m), (r.Lines.Single().PriceTolerancePercent, r.Lines.Single().QuantityTolerancePercent, r.Lines.Single().UnderDeliveryTolerancePercent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_reason_is_required(string reason) =>
        Assert.Throws<DomainException>(() => Pending(reason));

    [Fact]
    public void An_overlong_reason_is_rejected() =>
        Assert.Throws<DomainException>(() => Pending(new string('x', 501)));

    [Fact]
    public void At_least_one_line_and_each_item_once()
    {
        Assert.Throws<DomainException>(() => ToleranceRequest.Create(Company, "T", Order, Requester, "why", [], Now));
        Assert.Throws<DomainException>(() => ToleranceRequest.Create(Company, "T", Order, Requester, "why",
            [new ToleranceRequestLine(ItemA, "A", 1m, null, null), new ToleranceRequestLine(ItemA, "A", 2m, null, null)], Now));
        Assert.Equal(2, ToleranceRequest.Create(Company, "T", Order, Requester, "why",
            [new ToleranceRequestLine(ItemA, "A", 1m, null, null), new ToleranceRequestLine(ItemB, "B", null, 2m, null)], Now).Lines.Count);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 101, 0)]
    [InlineData(0, 0, 100.5)]
    public void Requested_values_are_validated(double price, double over, double under) =>
        Assert.Throws<DomainException>(() => new ToleranceRequestLine(ItemA, "A", (decimal)price, (decimal)over, (decimal)under));

    [Fact]
    public void Another_person_can_approve_and_the_decision_is_recorded()
    {
        var r = Pending();
        r.Approve(Approver, "  ok for this supplier ", Now.AddHours(1));
        Assert.Equal(ToleranceRequestStatus.Approved, r.Status);
        Assert.Equal(Approver, r.DecidedBy);
        Assert.Equal(Now.AddHours(1), r.DecidedAt);
        Assert.Equal("ok for this supplier", r.DecisionNote);
    }

    [Fact]
    public void Approval_without_a_note_is_allowed() { var r = Pending(); r.Approve(Approver, null, Now); Assert.Null(r.DecisionNote); }

    [Fact]
    public void Nobody_decides_their_own_request()
    {
        var ex = Assert.Throws<DomainException>(() => Pending().Approve(Requester, null, Now));
        Assert.Contains("your own request", ex.Message);
        Assert.Throws<DomainException>(() => Pending().Reject(Requester, "no", Now));
    }

    [Fact]
    public void Rejection_needs_a_reason_and_leaves_the_request_rejected()
    {
        var r = Pending();
        Assert.Throws<DomainException>(() => r.Reject(Approver, " ", Now));
        Assert.Equal(ToleranceRequestStatus.Pending, r.Status);
        r.Reject(Approver, "Price is contractual", Now);
        Assert.Equal(ToleranceRequestStatus.Rejected, r.Status);
        Assert.Equal("Price is contractual", r.DecisionNote);
    }

    [Fact]
    public void Only_the_requester_can_withdraw()
    {
        var r = Pending();
        Assert.Throws<DomainException>(() => r.Withdraw(Approver, Now));
        r.Withdraw(Requester, Now);
        Assert.Equal(ToleranceRequestStatus.Withdrawn, r.Status);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("withdraw")]
    public void A_decided_request_cannot_be_decided_again(string second)
    {
        var r = Pending();
        r.Approve(Approver, null, Now);
        Assert.Throws<DomainException>(() => _ = second switch
        {
            "approve" => Act(() => r.Approve(Approver, null, Now)),
            "reject" => Act(() => r.Reject(Approver, "x", Now)),
            _ => Act(() => r.Withdraw(Requester, Now))
        });
        Assert.Equal(ToleranceRequestStatus.Approved, r.Status);
    }

    private static int Act(Action a) { a(); return 0; }
}
