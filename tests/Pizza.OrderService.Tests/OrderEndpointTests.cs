using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pizza.OrderService.Tests;

public class OrderEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly HttpClient _client;

    public OrderEndpointTests() => _client = _factory.CreateClient();

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

    private async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Submit(object body, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await Read(await _client.SendAsync(request));
    }

    private async Task<string> SubmitNewOrderId()
    {
        var (status, order, _) = await Submit(G2Body);
        Assert.That(status, Is.EqualTo(HttpStatusCode.Created));
        return order.GetProperty("id").GetString()!;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Advance(string id) =>
        await Read(await _client.PostAsync($"/api/orders/{id}/advance", null));

    private static async Task<(HttpStatusCode, JsonElement, HttpResponseMessage)> Read(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var body = raw.Length == 0 ? default : JsonDocument.Parse(raw).RootElement.Clone();
        return (response.StatusCode, body, response);
    }

    [Test]
    public async Task Inv1_Inv3_SubmitG2_PricesLikeTheQuote_AndGetReturnsTheSameOrder()
    {
        var (status, order, response) = await Submit(G2Body);

        Assert.That(status, Is.EqualTo(HttpStatusCode.Created));
        var id = order.GetProperty("id").GetString()!;
        var pricing = order.GetProperty("pricing");
        Assert.Multiple(() =>
        {
            Assert.That(id, Does.StartWith("ord_"));
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/api/orders/{id}"));
            Assert.That(order.GetProperty("status").GetString(), Is.EqualTo("NotStarted"));
            Assert.That(pricing.GetProperty("subtotal").GetDecimal(), Is.EqualTo(29.28m));
            Assert.That(pricing.GetProperty("discount").GetDecimal(), Is.EqualTo(2.93m));
            Assert.That(pricing.GetProperty("tax").GetDecimal(), Is.EqualTo(1.71m));
            Assert.That(pricing.GetProperty("tip").GetDecimal(), Is.EqualTo(4.21m));
            Assert.That(pricing.GetProperty("total").GetDecimal(), Is.EqualTo(32.27m));
            Assert.That(pricing.GetProperty("description").GetString(), Is.EqualTo("2 x Large pizza with bacon, pineapple"));
        });

        var (getStatus, fetched, _) = await Read(await _client.GetAsync($"/api/orders/{id}"));
        Assert.That(getStatus, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(fetched.GetRawText(), Is.EqualTo(order.GetRawText()));
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
            promoCode = " save5 ",
            tip = new { mode = "percent", value = 17.5m },
        });
        yield return new TestCaseData(new
        {
            lineItems = new[] { new { size = "medium", quantity = 19, toppings = new[] { "sausage", "bacon" } } },
            tip = new { mode = "amount", value = 0.07m },
        });
    }

    [TestCaseSource(nameof(AssortedBodies))]
    public async Task Inv3_SameDraftGetsSameTotalFromQuoteAndOrder(object body)
    {
        var quote = await (await _client.PostAsJsonAsync("/api/quotes", body)).Content.ReadFromJsonAsync<JsonElement>();
        var (status, order, _) = await Submit(body);

        Assert.That(status, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(order.GetProperty("pricing").GetRawText(), Is.EqualTo(quote.GetRawText()));
    }

    [Test]
    public async Task Inv5_Submit_Rejects_UnknownPromoCode()
    {
        var (status, problem, _) = await Submit(new { lineItems = new[] { new { size = "large", quantity = 1 } }, promoCode = " XYZ " });

        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        var messages = problem.GetProperty("errors").GetProperty("promoCode").EnumerateArray().Select(m => m.GetString());
        Assert.That(messages, Does.Contain("Promo code XYZ isn't valid."));
    }

    [Test]
    public async Task Inv5_Submit_ReportsUnknownPromoCodeTogetherWithOtherErrors()
    {
        var (status, problem, _) = await Submit(new { lineItems = new[] { new { size = "xl", quantity = 1 } }, promoCode = "XYZ" });

        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        var errors = problem.GetProperty("errors");
        Assert.Multiple(() =>
        {
            Assert.That(errors.GetProperty("promoCode")[0].GetString(), Is.EqualTo("Promo code XYZ isn't valid."));
            Assert.That(errors.GetProperty("lineItems[0].size")[0].GetString(), Is.EqualTo("We don't have a size called 'xl'."));
        });
    }

    [Test]
    public async Task Inv5_Submit_Rejects_EmptyOrder()
    {
        var (status, problem, _) = await Submit(new { lineItems = Array.Empty<object>() });

        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        var messages = problem.GetProperty("errors").GetProperty("lineItems").EnumerateArray().Select(m => m.GetString());
        Assert.That(messages, Does.Contain("Add at least one pizza to your order."));
    }

    [Test]
    public async Task Inv4_Submit_Save5AtExactlyTwentyDollars_IsAcceptedWithoutDiscountAndSaysWhy()
    {
        var (status, order, _) = await Submit(new { lineItems = new[] { new { size = "small", quantity = 4 } }, promoCode = "SAVE5" });

        Assert.That(status, Is.EqualTo(HttpStatusCode.Created));
        var pricing = order.GetProperty("pricing");
        Assert.Multiple(() =>
        {
            Assert.That(pricing.GetProperty("discount").GetDecimal(), Is.EqualTo(0.00m));
            Assert.That(pricing.GetProperty("total").GetDecimal(), Is.EqualTo(21.30m));
            Assert.That(pricing.GetProperty("promo").GetProperty("message").GetString(), Is.EqualTo("SAVE5 requires a subtotal over $20.00."));
        });
    }

    [Test]
    public async Task Submit_WithoutIdempotencyKey_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", G2Body);
        var (status, problem, _) = await Read(response);

        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem.GetProperty("errors").TryGetProperty("Idempotency-Key", out _), Is.True);
    }

    [Test]
    public async Task Inv6_AdvanceWalksTheLifecycle_ThenTerminalAdvanceReturns409WithCurrentStatus()
    {
        var id = await SubmitNewOrderId();

        foreach (var expected in new[] { "Started", "ReadyForPickup", "Delivered" })
        {
            var (status, order, _) = await Advance(id);
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(order.GetProperty("status").GetString(), Is.EqualTo(expected));
        }

        var (conflictStatus, conflict, _) = await Advance(id);
        Assert.That(conflictStatus, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(conflict.GetProperty("error").GetString(), Is.EqualTo("statusConflict"));
        Assert.That(conflict.GetProperty("order").GetProperty("status").GetString(), Is.EqualTo("Delivered"));
    }

    [Test]
    public async Task Inv6_ConcurrentAdvances_ExactlyThreeSucceed_AndTheOrderEndsDelivered()
    {
        for (var round = 0; round < 25; round++)
        {
            var id = await SubmitNewOrderId();

            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Advance(id))));

            Assert.That(results.Count(r => r.Status == HttpStatusCode.OK), Is.EqualTo(3), $"round {round}");
            Assert.That(results.Count(r => r.Status == HttpStatusCode.Conflict), Is.EqualTo(17), $"round {round}");
            var final = await (await _client.GetAsync($"/api/orders/{id}")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.That(final.GetProperty("status").GetString(), Is.EqualTo("Delivered"));
        }
    }

    [Test]
    public async Task UnknownOrderId_Returns404_OnGetAndAdvance()
    {
        var (getStatus, _, _) = await Read(await _client.GetAsync("/api/orders/ord_missing"));
        var (advanceStatus, _, _) = await Advance("ord_missing");

        Assert.That(getStatus, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(advanceStatus, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
