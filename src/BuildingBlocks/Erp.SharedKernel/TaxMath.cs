namespace Erp.SharedKernel;

/// <summary>Tax arithmetic shared by sales and purchasing. Prices are tax-exclusive; tax is rounded per document line.</summary>
public static class TaxMath
{
    /// <summary>Tax on a net amount at a percentage rate, rounded to cents (half away from zero).</summary>
    public static decimal Tax(decimal net, decimal ratePercent) =>
        decimal.Round(net * ratePercent / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The share of a line's tax that belongs to <paramref name="quantity"/> of its <paramref name="lineQuantity"/>, given what earlier
    /// credits already took. The share that completes the line takes whatever remains, so partial credits always add up to the line's tax.
    /// </summary>
    public static decimal Portion(decimal lineTax, decimal lineQuantity, decimal quantityBefore, decimal taxBefore, decimal quantity)
    {
        var remaining = lineTax - taxBefore;
        if (quantityBefore + quantity >= lineQuantity) return remaining;
        var share = decimal.Round(lineTax * quantity / lineQuantity, 2, MidpointRounding.AwayFromZero);
        return Math.Clamp(share, 0m, remaining);
    }
}
