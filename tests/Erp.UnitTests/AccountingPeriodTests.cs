using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public sealed class AccountingPeriodTests
{
    [Fact]
    public void Period_starts_open_and_contains_inclusive_dates()
    {
        var period = AccountingPeriod.Create(
            CompanyId.New(),
            " fy-2026 ",
            "Fiscal year 2026",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            DateTimeOffset.UtcNow,
            null);

        Assert.Equal("FY-2026", period.Code);
        Assert.Equal(AccountingPeriodStatus.Open, period.Status);
        Assert.True(period.Contains(new DateOnly(2026, 1, 1)));
        Assert.True(period.Contains(new DateOnly(2026, 12, 31)));
        Assert.False(period.Contains(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Period_requires_valid_date_range()
    {
        Assert.Throws<DomainException>(() => AccountingPeriod.Create(
            CompanyId.New(),
            "FY-2026",
            "Fiscal year 2026",
            new DateOnly(2026, 12, 31),
            new DateOnly(2026, 1, 1),
            DateTimeOffset.UtcNow,
            null));
    }

    [Fact]
    public void Period_must_close_before_it_can_be_locked()
    {
        var period = CreatePeriod();

        Assert.Throws<DomainException>(() => period.Lock(null, DateTimeOffset.UtcNow));

        period.Close(null, DateTimeOffset.UtcNow);
        period.Lock(null, DateTimeOffset.UtcNow);

        Assert.Equal(AccountingPeriodStatus.Locked, period.Status);
        Assert.Throws<DomainException>(() => period.Reopen(null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Closed_period_can_be_reopened_but_locked_period_cannot()
    {
        var period = CreatePeriod();

        period.Close(null, DateTimeOffset.UtcNow);
        period.Reopen(null, DateTimeOffset.UtcNow);

        Assert.Equal(AccountingPeriodStatus.Open, period.Status);
    }

    [Fact]
    public void Locked_period_cannot_be_renamed()
    {
        var period = CreatePeriod();
        period.Close(null, DateTimeOffset.UtcNow);
        period.Lock(null, DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() =>
            period.Rename("Renamed", null, DateTimeOffset.UtcNow));
    }

    private static AccountingPeriod CreatePeriod() =>
        AccountingPeriod.Create(
            CompanyId.New(),
            "FY-2026",
            "Fiscal year 2026",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            DateTimeOffset.UtcNow,
            null);
}
