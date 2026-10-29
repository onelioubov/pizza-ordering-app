using Pizza.OrderService.Pricing;

namespace Pizza.OrderService.Tests;

public class PricingEngineTests
{
    // A menu that exists only in tests: the $8.50 size from G6 and promos for the data-only checks.
    private const string TestMenuJson = """
        {
          "sizes": [ { "id": "test", "name": "Test", "basePrice": 8.50 } ],
          "toppingCategories": [ { "id": "basic", "rate": 0.08 }, { "id": "premium", "rate": 0.11 } ],
          "toppings": [
            { "id": "pepperoni", "name": "pepperoni", "category": "basic" },
            { "id": "bacon", "name": "bacon", "category": "premium" }
          ],
          "promotions": [
            { "code": "HALF", "kind": "PercentOff", "group": "order-discount", "parameters": { "percent": 50 } },
            { "code": "HUNDRED", "kind": "AmountOffOverThreshold", "group": "order-discount", "parameters": { "amount": 100.00, "threshold": 0.00 } }
          ]
        }
        """;

    private readonly PricingEngine _engine = new();

    private static LineItemDraft Line(string size, int quantity, params string[] toppings) => new(size, quantity, toppings);

    private static OrderDraft Draft(params LineItemDraft[] lines) => new(lines, null, null);

    private static IEnumerable<TestCaseData> GoldenCases()
    {
        yield return new TestCaseData(null, Draft(Line("small", 1)),
            5.00m, 0.00m, 0.33m, 0.00m, 5.33m).SetName("Inv1_GoldenCase_G1");
        yield return new TestCaseData(null,
            Draft(Line("large", 2, "bacon", "pineapple")) with { PromoCode = "ILOVEPIZZA", Tip = new TipDraft(TipMode.Percent, 15m) },
            29.28m, 2.93m, 1.71m, 4.21m, 32.27m).SetName("Inv1_GoldenCase_G2");
        yield return new TestCaseData(null, Draft(Line("small", 4)) with { PromoCode = "SAVE5" },
            20.00m, 0.00m, 1.30m, 0.00m, 21.30m).SetName("Inv1_GoldenCase_G3");
        yield return new TestCaseData(null,
            Draft(Line("large", 2)) with { PromoCode = "SAVE5", Tip = new TipDraft(TipMode.Amount, 3.00m) },
            24.00m, 5.00m, 1.24m, 3.00m, 23.24m).SetName("Inv1_GoldenCase_G4");
        yield return new TestCaseData(null,
            Draft(Line("medium", 3, "pepperoni", "chicken")) with { Tip = new TipDraft(TipMode.Percent, 20m) },
            24.99m, 0.00m, 1.62m, 5.32m, 31.93m).SetName("Inv1_GoldenCase_G5");
        yield return new TestCaseData(TestMenuJson, Draft(Line("test", 2, "pepperoni", "bacon")),
            20.24m, 0.00m, 1.32m, 0.00m, 21.56m).SetName("Inv1_GoldenCase_G6");
    }

    [TestCaseSource(nameof(GoldenCases))]
    public void Inv1_GoldenCase_MatchesExactly(string? menuJson, OrderDraft draft,
        decimal subtotal, decimal discount, decimal tax, decimal tip, decimal total)
    {
        var engine = menuJson is null ? _engine : PricingEngine.FromMenuJson(menuJson);

        var result = engine.Price(draft);

        Assert.Multiple(() =>
        {
            Assert.That(result.Subtotal, Is.EqualTo(subtotal));
            Assert.That(result.Discount, Is.EqualTo(discount));
            Assert.That(result.Tax, Is.EqualTo(tax));
            Assert.That(result.Tip, Is.EqualTo(tip));
            Assert.That(result.Total, Is.EqualTo(total));
        });
    }

    [Test]
    public void Inv1_GoldenCase_G2_LineItemAndDescription()
    {
        var draft = Draft(Line("large", 2, "bacon", "pineapple")) with { PromoCode = "ILOVEPIZZA", Tip = new TipDraft(TipMode.Percent, 15m) };

        var result = _engine.Price(draft);

        Assert.Multiple(() =>
        {
            Assert.That(result.LineItems[0].UnitPrice, Is.EqualTo(14.64m));
            Assert.That(result.LineItems[0].LineTotal, Is.EqualTo(29.28m));
            Assert.That(result.LineItems[0].Description, Is.EqualTo("2 x Large pizza with bacon, pineapple"));
            Assert.That(result.Promo, Is.EqualTo(new PromoResult("ILOVEPIZZA", true, null)));
        });
    }

    [Test]
    public void Description_HasOneLinePerLineItem()
    {
        var result = _engine.Price(Draft(Line("large", 2, "bacon", "pineapple"), Line("small", 1)));

        Assert.That(result.Description, Is.EqualTo("2 x Large pizza with bacon, pineapple\n1 x Small pizza (no toppings)"));
    }

    private static IEnumerable<TestCaseData> AssortedDrafts()
    {
        foreach (var data in GoldenCases()) yield return new TestCaseData(data.Arguments[0], data.Arguments[1]);
        yield return new TestCaseData(null, new OrderDraft(
            [Line("small", 3, "onions", "black-olives", "garlic"), Line("medium", 7, "sun-dried-tomatoes")],
            "iLovePizza", new TipDraft(TipMode.Percent, 17.5m)));
        yield return new TestCaseData(null, new OrderDraft(
            [Line("large", 1, "extra-cheese", "artichoke-hearts"), Line("medium", 2, "green-peppers")],
            "SAVE5", new TipDraft(TipMode.Percent, 12.33m)));
        yield return new TestCaseData(null, new OrderDraft([Line("medium", 19, "sausage", "bacon")], "NOPE", new TipDraft(TipMode.Amount, 0.07m)));
        yield return new TestCaseData(TestMenuJson, new OrderDraft([Line("test", 1)], "HUNDRED", new TipDraft(TipMode.Percent, 10m)));
    }

    [TestCaseSource(nameof(AssortedDrafts))]
    public void Inv2_TotalEqualsSubtotalMinusDiscountPlusTaxPlusTip(string? menuJson, OrderDraft draft)
    {
        var engine = menuJson is null ? _engine : PricingEngine.FromMenuJson(menuJson);

        var result = engine.Price(draft);

        Assert.That(result.Total, Is.EqualTo(result.Subtotal - result.Discount + result.Tax + result.Tip));
        Assert.That(result.Subtotal, Is.EqualTo(result.LineItems.Sum(l => l.LineTotal)));
    }

    [Test]
    public void Inv4_Save5_NotEligibleAtExactly20()
    {
        var result = _engine.Price(Draft(Line("small", 4)) with { PromoCode = "SAVE5" });

        Assert.That(result.Subtotal, Is.EqualTo(20.00m));
        Assert.That(result.Discount, Is.EqualTo(0.00m));
        Assert.That(result.Promo, Is.EqualTo(new PromoResult("SAVE5", false, "SAVE5 requires a subtotal over $20.00.")));
    }

    [Test]
    public void Inv4_Save5_EligibleAbove20()
    {
        var result = _engine.Price(Draft(Line("small", 3), Line("medium", 1)) with { PromoCode = "SAVE5" });

        Assert.That(result.Subtotal, Is.EqualTo(22.00m));
        Assert.That(result.Discount, Is.EqualTo(5.00m));
        Assert.That(result.Promo, Is.EqualTo(new PromoResult("SAVE5", true, null)));
    }

    [Test]
    public void PromoCode_IsCaseInsensitiveAndTrimmed()
    {
        var result = _engine.Price(Draft(Line("large", 1)) with { PromoCode = "  iLovePizza " });

        Assert.That(result.Discount, Is.EqualTo(1.20m));
        Assert.That(result.Promo, Is.EqualTo(new PromoResult("ILOVEPIZZA", true, null)));
    }

    [Test]
    public void UnknownPromoCode_OnQuote_PricesWithoutDiscountAndSaysSo()
    {
        var result = _engine.Price(Draft(Line("large", 1)) with { PromoCode = "XYZ" });

        Assert.That(result.Discount, Is.EqualTo(0.00m));
        Assert.That(result.Total, Is.EqualTo(12.78m));
        Assert.That(result.Promo, Is.EqualTo(new PromoResult("XYZ", false, "Promo code XYZ isn't valid.")));
    }

    [Test]
    public void Discount_NeverExceedsSubtotal()
    {
        var engine = PricingEngine.FromMenuJson(TestMenuJson);

        var result = engine.Price(Draft(Line("test", 1)) with { PromoCode = "HUNDRED" });

        Assert.That(result.Discount, Is.EqualTo(result.Subtotal));
        Assert.That(result.Tax, Is.EqualTo(0.00m));
        Assert.That(result.Total, Is.EqualTo(0.00m));
    }

    private static IEnumerable<TestCaseData> InvalidDrafts()
    {
        yield return new TestCaseData(Draft(), "lineItems", "Add at least one pizza to your order.").SetName("Inv5_Rejects_EmptyOrder");
        yield return new TestCaseData(Draft(Line("xl", 1)), "lineItems[0].size", "We don't have a size called 'xl'.").SetName("Inv5_Rejects_UnknownSize");
        yield return new TestCaseData(Draft(Line("small", 1), Line("small", 1, "anchovies")), "lineItems[1].toppings", "We don't have a topping called 'anchovies'.").SetName("Inv5_Rejects_UnknownTopping");
        yield return new TestCaseData(Draft(Line("small", 1, "pepperoni", "pepperoni")), "lineItems[0].toppings", "Pepperoni is already on this pizza.").SetName("Inv5_Rejects_DuplicateTopping");
        yield return new TestCaseData(Draft(Line("small", 0)), "lineItems[0].quantity", "Quantity must be between 1 and 20.").SetName("Inv5_Rejects_QuantityZero");
        yield return new TestCaseData(Draft(Line("small", 21)), "lineItems[0].quantity", "Quantity must be between 1 and 20.").SetName("Inv5_Rejects_QuantityOver20");
        yield return new TestCaseData(Draft(Line("small", 1)) with { Tip = new TipDraft(TipMode.Percent, -1m) }, "tip", "Tip percentage can't be negative.").SetName("Inv5_Rejects_NegativeTipPercent");
        yield return new TestCaseData(Draft(Line("small", 1)) with { Tip = new TipDraft(TipMode.Percent, 100.01m) }, "tip", "Tip percentage can't be more than 100.").SetName("Inv5_Rejects_TipPercentOver100");
        yield return new TestCaseData(Draft(Line("small", 1)) with { Tip = new TipDraft(TipMode.Amount, -0.01m) }, "tip", "Tip amount can't be negative.").SetName("Inv5_Rejects_NegativeTipAmount");
        yield return new TestCaseData(Draft(Line("small", 1)) with { Tip = new TipDraft(TipMode.Amount, 1.005m) }, "tip", "Tip can have at most 2 decimal places.").SetName("Inv5_Rejects_TipWithThreeDecimals");
        yield return new TestCaseData(Draft(Line("small", 1)) with { Tip = new TipDraft(TipMode.Percent, 12.345m) }, "tip", "Tip can have at most 2 decimal places.").SetName("Inv5_Rejects_TipPercentWithThreeDecimals");
    }

    [TestCaseSource(nameof(InvalidDrafts))]
    public void Inv5_BadInputIsRejectedWithMessage(OrderDraft draft, string field, string message)
    {
        var ex = Assert.Throws<InvalidDraftException>(() => _engine.Price(draft));

        Assert.That(ex!.Errors, Does.ContainKey(field));
        Assert.That(ex.Errors[field], Does.Contain(message));
    }

    [Test]
    public void Inv5_BoundaryValuesAreAccepted()
    {
        Assert.DoesNotThrow(() => _engine.Price(Draft(Line("small", 1), Line("small", 20)) with { Tip = new TipDraft(TipMode.Percent, 100m) }));
        Assert.DoesNotThrow(() => _engine.Price(Draft(Line("small", 1)) with { Tip = new TipDraft(TipMode.Amount, 0.00m) }));
    }

    [Test]
    public void Inv10_NewSizeWorksThroughDataAlone()
    {
        var engine = PricingEngine.FromMenuJson(TestMenuJson);

        var result = engine.Price(Draft(Line("test", 1)));

        Assert.That(result.Subtotal, Is.EqualTo(8.50m));
        Assert.That(result.Description, Is.EqualTo("1 x Test pizza (no toppings)"));
    }

    [Test]
    public void Inv10_NewPercentOffPromoWorksThroughDataAlone()
    {
        var engine = PricingEngine.FromMenuJson(TestMenuJson);

        var result = engine.Price(Draft(Line("test", 1, "bacon")) with { PromoCode = "half" });

        // 8.50 + 0.935 → 0.94 = 9.44; 50% = 4.72
        Assert.That(result.Discount, Is.EqualTo(4.72m));
        Assert.That(result.Promo, Is.EqualTo(new PromoResult("HALF", true, null)));
    }
}
