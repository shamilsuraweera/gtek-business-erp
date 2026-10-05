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

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void Invalid_padding_is_rejected(int padding)
    {
        Assert.Throws<DomainException>(() => NumberSequence.Create(
            CompanyId.New(), "INV", "Invoices", "INV-", null, 1, padding, 1, DateTimeOffset.UtcNow, null));
    }
}
