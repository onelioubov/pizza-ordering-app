namespace Pizza.OrderService.Pricing;

internal static class Money
{
    // The only rounding in the service (spec §3.2): cents, half away from zero.
    public static decimal RoundCents(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
