namespace MaltasGarage.Domain.Common;

/// <summary>The marketplace commission: 10% of the sale price, at least €1.</summary>
public static class PlatformFee
{
    public const decimal Percentage = 0.10m;
    public const decimal Minimum = 1.00m;

    public static decimal Calculate(decimal price) => Math.Max(price * Percentage, Minimum);
}
