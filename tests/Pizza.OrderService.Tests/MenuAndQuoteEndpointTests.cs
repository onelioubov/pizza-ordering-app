using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pizza.OrderService.Tests;

public class MenuAndQuoteEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly HttpClient _client;

    public MenuAndQuoteEndpointTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static readonly object G2Body = new
    {
        lineItems = new[] { new { size = "large", quantity = 2, toppings = new[] { "bacon", "pineapple" } } },
        promoCode = "ILOVEPIZZA",
        tip = new { mode = "percent", value = 15 },
    };

    private async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> PostQuote(object body) =>
        await Read(await _client.PostAsJsonAsync("/api/quotes", body));

    private static async Task<(HttpStatusCode, JsonElement, string)> Read(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    [Test]
    public async Task Inv1_GoldenCase_G2_QuoteMatchesSpecResponse()
    {
        var (status, quote, _) = await PostQuote(G2Body);

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        var line = quote.GetProperty("lineItems")[0];
        var promo = quote.GetProperty("promo");
        Assert.Multiple(() =>
        {
            Assert.That(quote.GetProperty("lineItems").GetArrayLength(), Is.EqualTo(1));
            Assert.That(line.GetProperty("description").GetString(), Is.EqualTo("2 x Large pizza with bacon, pineapple"));
            Assert.That(line.GetProperty("unitPrice").GetDecimal(), Is.EqualTo(14.64m));
            Assert.That(line.GetProperty("lineTotal").GetDecimal(), Is.EqualTo(29.28m));
            Assert.That(quote.GetProperty("subtotal").GetDecimal(), Is.EqualTo(29.28m));
            Assert.That(promo.GetProperty("code").GetString(), Is.EqualTo("ILOVEPIZZA"));
            Assert.That(promo.GetProperty("applied").GetBoolean(), Is.True);
            Assert.That(promo.GetProperty("message").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(quote.GetProperty("discount").GetDecimal(), Is.EqualTo(2.93m));
            Assert.That(quote.GetProperty("tax").GetDecimal(), Is.EqualTo(1.71m));
            Assert.That(quote.GetProperty("tip").GetDecimal(), Is.EqualTo(4.21m));
            Assert.That(quote.GetProperty("total").GetDecimal(), Is.EqualTo(32.27m));
            Assert.That(quote.GetProperty("description").GetString(), Is.EqualTo("2 x Large pizza with bacon, pineapple"));
        });
    }

    [Test]
    public async Task Inv1_GoldenCase_G1_MoneyIsWrittenWithTwoDecimals()
    {
        var (status, _, raw) = await PostQuote(new { lineItems = new[] { new { size = "small", quantity = 1 } } });

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(raw, Does.Contain("\"subtotal\":5.00"));
        Assert.That(raw, Does.Contain("\"discount\":0.00"));
        Assert.That(raw, Does.Contain("\"tax\":0.33"));
        Assert.That(raw, Does.Contain("\"tip\":0.00"));
        Assert.That(raw, Does.Contain("\"total\":5.33"));
    }

    [Test]
    public async Task Inv1_WholeDollarTipAmountIsWrittenWithTwoDecimals()
    {
        var (_, _, raw) = await PostQuote(new
        {
            lineItems = new[] { new { size = "large", quantity = 2 } },
            promoCode = "SAVE5",
            tip = new { mode = "amount", value = 3 },
        });

        Assert.That(raw, Does.Contain("\"tip\":3.00"));
        Assert.That(raw, Does.Contain("\"total\":23.24"));
    }

    private static IEnumerable<TestCaseData> AssortedBodies()
    {
        yield return new TestCaseData(G2Body);
        yield return new TestCaseData(new
        {
            lineItems = new object[]
            {
                new { size = "small", quantity = 3, toppings = new[] { "onions", "garlic" } },
                new { size = "medium", quantity = 7, toppings = new[] { "sun-dried-tomatoes" } },
            },
            promoCode = "save5",
            tip = new { mode = "percent", value = 17.5m },
        });
        yield return new TestCaseData(new
        {
            lineItems = new[] { new { size = "medium", quantity = 19, toppings = new[] { "sausage", "bacon" } } },
            promoCode = "NOPE",
            tip = new { mode = "amount", value = 0.07m },
        });
    }

    [TestCaseSource(nameof(AssortedBodies))]
    public async Task Inv2_QuoteTotalEqualsSubtotalMinusDiscountPlusTaxPlusTip(object body)
    {
        var (status, quote, _) = await PostQuote(body);

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        decimal Money(string name) => quote.GetProperty(name).GetDecimal();
        Assert.That(Money("total"), Is.EqualTo(Money("subtotal") - Money("discount") + Money("tax") + Money("tip")));
    }

    private static IEnumerable<TestCaseData> InvalidBodies()
    {
        yield return new TestCaseData(new { lineItems = Array.Empty<object>() },
            "lineItems", "Add at least one pizza to your order.").SetName("Inv5_Quote_Rejects_EmptyOrder");
        yield return new TestCaseData(new { lineItems = new[] { new { size = "xl", quantity = 1 } } },
            "lineItems[0].size", "We don't have a size called 'xl'.").SetName("Inv5_Quote_Rejects_UnknownSize");
        yield return new TestCaseData(new { lineItems = new[] { new { size = "small", quantity = 1, toppings = new[] { "anchovies" } } } },
            "lineItems[0].toppings", "We don't have a topping called 'anchovies'.").SetName("Inv5_Quote_Rejects_UnknownTopping");
        yield return new TestCaseData(new { lineItems = new[] { new { size = "small", quantity = 1, toppings = new[] { "pepperoni", "pepperoni" } } } },
            "lineItems[0].toppings", "Pepperoni is already on this pizza.").SetName("Inv5_Quote_Rejects_DuplicateTopping");
        yield return new TestCaseData(new { lineItems = new[] { new { size = "small", quantity = 21 } } },
            "lineItems[0].quantity", "Quantity must be between 1 and 20.").SetName("Inv5_Quote_Rejects_QuantityOutOfRange");
        yield return new TestCaseData(new { lineItems = new[] { new { size = "small", quantity = 1 } }, tip = new { mode = "percent", value = -1 } },
            "tip", "Tip percentage can't be negative.").SetName("Inv5_Quote_Rejects_TipOutOfRange");
        yield return new TestCaseData(new { lineItems = new object?[] { new { size = "small", quantity = 1 }, null } },
            "lineItems[1]", "This pizza is missing its size and quantity.").SetName("Inv5_Quote_Rejects_NullLineItem");
    }

    [TestCaseSource(nameof(InvalidBodies))]
    public async Task Inv5_QuoteRejectsBadInputWithProblemDetails(object body, string field, string message)
    {
        var (status, problem, _) = await PostQuote(body);

        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        var messages = problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(m => m.GetString());
        Assert.That(messages, Does.Contain(message));
    }

    [TestCase("{ \"lineItems\": [ { \"size\": \"small\", \"quantity\": \"two\" } ] }", TestName = "Inv5_Quote_Rejects_NonNumericQuantity")]
    [TestCase("{ \"lineItems\": [ { \"size\": \"small\", \"quantity\": 1 } ], \"tip\": { \"mode\": \"bogus\", \"value\": 1 } }", TestName = "Inv5_Quote_Rejects_UnknownTipMode")]
    [TestCase("not json", TestName = "Inv5_Quote_Rejects_MalformedJson")]
    public async Task Inv5_QuoteRejectsUnreadableBodyWithProblemDetails(string json)
    {
        var (status, problem, _) = await Read(await _client.PostAsync("/api/quotes",
            new StringContent(json, Encoding.UTF8, "application/json")));

        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        var messages = problem.GetProperty("errors").GetProperty("body").EnumerateArray().Select(m => m.GetString());
        Assert.That(messages, Does.Contain("We couldn't read your order."));
    }

    [Test]
    public async Task UnknownPromoCode_OnQuote_PricesWithoutDiscountAndSaysSo()
    {
        var (status, quote, _) = await PostQuote(new { lineItems = new[] { new { size = "large", quantity = 1 } }, promoCode = "XYZ" });

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(quote.GetProperty("discount").GetDecimal(), Is.EqualTo(0.00m));
        Assert.That(quote.GetProperty("promo").GetProperty("applied").GetBoolean(), Is.False);
        Assert.That(quote.GetProperty("promo").GetProperty("message").GetString(), Is.EqualTo("Promo code XYZ isn't valid."));
    }

    [Test]
    public async Task Menu_ListsSizesAndToppingsFromMenuJson_WithoutPromoCodes()
    {
        var (status, menu, raw) = await Read(await _client.GetAsync("/api/menu"));

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        var sizes = menu.GetProperty("sizes").EnumerateArray().ToList();
        var toppings = menu.GetProperty("toppings").EnumerateArray().ToList();
        var large = sizes.Single(s => s.GetProperty("id").GetString() == "large");
        var bacon = toppings.Single(t => t.GetProperty("id").GetString() == "bacon");
        Assert.Multiple(() =>
        {
            Assert.That(sizes.Select(s => s.GetProperty("id").GetString()), Is.EqualTo(new[] { "small", "medium", "large" }));
            Assert.That(large.GetProperty("name").GetString(), Is.EqualTo("Large"));
            Assert.That(large.GetProperty("basePrice").GetDecimal(), Is.EqualTo(12.00m));
            Assert.That(toppings, Has.Count.EqualTo(12));
            Assert.That(bacon.GetProperty("name").GetString(), Is.EqualTo("bacon"));
            Assert.That(bacon.GetProperty("category").GetString(), Is.EqualTo("premium"));
            Assert.That(raw, Does.Contain("\"basePrice\":5.00"));
            Assert.That(raw, Does.Not.Contain("ILOVEPIZZA").And.Not.Contain("SAVE5"));
        });
    }
}
