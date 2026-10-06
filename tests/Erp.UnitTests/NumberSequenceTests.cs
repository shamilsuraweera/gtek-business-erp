using Erp.Modules.Platform.Domain;
using Erp.SharedKernel;

namespace Erp.UnitTests;

public sealed class NumberSequenceTests
{
    [Fact]
    public void Reserve_formats_and_advances_sequence()
    {
        var sequence = NumberSequence.Create(
            CompanyId.New(), " inv ", "Invoices", "INV-", null, 7, 4, 2, DateTimeOffset.UtcNow, null);

        Assert.Equal("INV-0007", sequence.Reserve());
        Assert.Equal("INV-0009", sequence.Reserve());
        Assert.Equal(11, sequence.NextValue);
        Assert.Equal("INV", sequence.Code);
    }

    [Fact]
    public void Inactive_sequence_cannot_reserve()
    {
        var sequence = NumberSequence.Create(
            CompanyId.New(), "INV", "Invoices", "INV-", "/2026", 1, 3, 1, DateTimeOffset.UtcNow, null);
        sequence.Deactivate(null, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => sequence.Reserve());
    }

    [Fact]
    public void Configuration_does_not_reset_next_value()
    {
        var sequence = NumberSequence.Create(
            CompanyId.New(), "INV", "Invoices", "INV-", "/2026", 41, 3, 1, DateTimeOffset.UtcNow, null);

        Assert.Equal("INV-041/2026", sequence.Reserve());
        sequence.ChangeConfiguration("BILL-", "/2027", 5, 2, null, DateTimeOffset.UtcNow);

        Assert.Equal("BILL-00042/2027", sequence.Reserve());
    }

    [Fact]
    public void Overflow_is_rejected_without_advancing()
    {
        var sequence = NumberSequence.Create(
            CompanyId.New(), "INV", "Invoices", "INV-", null, long.MaxValue, 19, 1, DateTimeOffset.UtcNow, null);

        Assert.Throws<InvalidOperationException>(() => sequence.Reserve());
        Assert.Equal(long.MaxValue, sequence.NextValue);
    }

    [Fact]
    public void Invalid_generated_length_is_rejected()
    {
        Assert.Throws<DomainException>(() => NumberSequence.Create(
            CompanyId.New(), "INV", "Invoices", new string('P', 50), new string('S', 50),
            1, 19, 1, DateTimeOffset.UtcNow, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void Invalid_padding_is_rejected(int padding)
    {
        Assert.Throws<DomainException>(() => NumberSequence.Create(
            CompanyId.New(), "INV", "Invoices", "INV-", null, 1, padding, 1, DateTimeOffset.UtcNow, null));
    }
}
