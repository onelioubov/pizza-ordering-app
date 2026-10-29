using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    private async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Update(string id, object body, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/orders/{id}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await Read(await _client.SendAsync(request));
    }

    private async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> Cancel(string id, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{id}/cancel");
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await Read(await _client.SendAsync(request));
    }

    private async Task<JsonElement> GetOrder(string id) =>
        await (await _client.GetAsync($"/api/orders/{id}")).Content.ReadFromJsonAsync<JsonElement>();

    private static readonly object G4Body = new
    {
        lineItems = new[] { new { size = "large", quantity = 2, toppings = Array.Empty<string>() } },
        promoCode = "SAVE5",
        tip = new { mode = "amount", value = 3.00m },
    };

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
            Assert.That(pricing.GetProperty("lineItems")[0].GetProperty("toppings").GetRawText(),
                Is.EqualTo("""[{"name":"bacon","price":1.32},{"name":"pineapple","price":1.32}]"""));
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

    [Test]
    public async Task Inv3_Update_RepricesLikeTheQuote_StoresTheNewDraft_AndGetReturnsIt()
    {
        var id = await SubmitNewOrderId();
        var quote = await (await _client.PostAsJsonAsync("/api/quotes", G4Body)).Content.ReadFromJsonAsync<JsonElement>();

        var (status, order, _) = await Update(id, G4Body);

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        var draft = order.GetProperty("draft");
        Assert.Multiple(() =>
        {
            Assert.That(order.GetProperty("id").GetString(), Is.EqualTo(id));
            Assert.That(order.GetProperty("status").GetString(), Is.EqualTo("NotStarted"));
            Assert.That(order.GetProperty("pricing").GetRawText(), Is.EqualTo(quote.GetRawText()));
            Assert.That(order.GetProperty("pricing").GetProperty("total").GetDecimal(), Is.EqualTo(23.24m));
            Assert.That(draft.GetProperty("lineItems")[0].GetProperty("size").GetString(), Is.EqualTo("large"));
            Assert.That(draft.GetProperty("lineItems")[0].GetProperty("quantity").GetInt32(), Is.EqualTo(2));
            Assert.That(draft.GetProperty("promoCode").GetString(), Is.EqualTo("SAVE5"));
            Assert.That(draft.GetProperty("tip").GetProperty("mode").GetString(), Is.EqualTo("amount"));
        });
        Assert.That((await GetOrder(id)).GetRawText(), Is.EqualTo(order.GetRawText()));
    }

    [Test]
    public async Task Inv5_Update_Rejects_UnknownPromoCode_AndLeavesTheOrderUnchanged()
    {
        var id = await SubmitNewOrderId();
        var before = await GetOrder(id);

        var (status, problem, _) = await Update(id, new { lineItems = new[] { new { size = "small", quantity = 1 } }, promoCode = "XYZ" });

        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem.GetProperty("errors").GetProperty("promoCode")[0].GetString(), Is.EqualTo("Promo code XYZ isn't valid."));
        Assert.That((await GetOrder(id)).GetRawText(), Is.EqualTo(before.GetRawText()));
    }

    [Test]
    public async Task Inv5_Update_Rejects_EmptyOrder_AndLeavesTheOrderUnchanged()
    {
        var id = await SubmitNewOrderId();
        var before = await GetOrder(id);

        var (status, problem, _) = await Update(id, new { lineItems = Array.Empty<object>() });

        Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem.GetProperty("errors").GetProperty("lineItems")[0].GetString(), Is.EqualTo("Add at least one pizza to your order."));
        Assert.That((await GetOrder(id)).GetRawText(), Is.EqualTo(before.GetRawText()));
    }

    [Test]
    public async Task Inv4_Update_Save5AtExactlyTwentyDollars_IsAcceptedWithoutDiscountAndSaysWhy()
    {
        var id = await SubmitNewOrderId();

        var (status, order, _) = await Update(id, new { lineItems = new[] { new { size = "small", quantity = 4 } }, promoCode = "SAVE5" });

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        var pricing = order.GetProperty("pricing");
        Assert.Multiple(() =>
        {
            Assert.That(pricing.GetProperty("discount").GetDecimal(), Is.EqualTo(0.00m));
            Assert.That(pricing.GetProperty("total").GetDecimal(), Is.EqualTo(21.30m));
            Assert.That(pricing.GetProperty("promo").GetProperty("message").GetString(), Is.EqualTo("SAVE5 requires a subtotal over $20.00."));
        });
    }

    [Test]
    public async Task Inv6_UpdateAndCancel_AfterTheKitchenStarts_Return409WithCurrentStatus_AndChangeNothing()
    {
        var id = await SubmitNewOrderId();
        await Advance(id);
        var before = await GetOrder(id);

        var (updateStatus, updateConflict, _) = await Update(id, G4Body);
        var (cancelStatus, cancelConflict, _) = await Cancel(id);

        Assert.Multiple(() =>
        {
            Assert.That(updateStatus, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(updateConflict.GetProperty("error").GetString(), Is.EqualTo("statusConflict"));
            Assert.That(updateConflict.GetProperty("order").GetProperty("status").GetString(), Is.EqualTo("Started"));
            Assert.That(cancelStatus, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(cancelConflict.GetProperty("error").GetString(), Is.EqualTo("statusConflict"));
            Assert.That(cancelConflict.GetProperty("order").GetProperty("status").GetString(), Is.EqualTo("Started"));
        });
        Assert.That((await GetOrder(id)).GetRawText(), Is.EqualTo(before.GetRawText()));
    }

    [Test]
    public async Task Inv6_Cancel_DeliveredOrder_Returns409()
    {
        var id = await SubmitNewOrderId();
        for (var i = 0; i < 3; i++) await Advance(id);

        var (status, conflict, _) = await Cancel(id);

        Assert.That(status, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(conflict.GetProperty("order").GetProperty("status").GetString(), Is.EqualTo("Delivered"));
    }

    [Test]
    public async Task Inv6_Cancel_NotStartedOrder_Cancels_AndCancellingAgainReturnsTheOrder()
    {
        var id = await SubmitNewOrderId();

        var (status, cancelled, _) = await Cancel(id);
        var (againStatus, again, _) = await Cancel(id);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(cancelled.GetProperty("status").GetString(), Is.EqualTo("Cancelled"));
            Assert.That(againStatus, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(again.GetRawText(), Is.EqualTo(cancelled.GetRawText()));
        });
    }

    [Test]
    public async Task Inv6_CancelledOrder_RejectsAdvanceAndUpdate_With409()
    {
        var id = await SubmitNewOrderId();
        await Cancel(id);

        var (advanceStatus, advanceConflict, _) = await Advance(id);
        var (updateStatus, updateConflict, _) = await Update(id, G4Body);

        Assert.Multiple(() =>
        {
            Assert.That(advanceStatus, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(advanceConflict.GetProperty("order").GetProperty("status").GetString(), Is.EqualTo("Cancelled"));
            Assert.That(updateStatus, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(updateConflict.GetProperty("order").GetProperty("status").GetString(), Is.EqualTo("Cancelled"));
        });
    }

    [Test]
    public async Task Inv9_CancelRacingAdvance_ExactlyOneWins()
    {
        for (var round = 0; round < 300; round++)
        {
            var id = await SubmitNewOrderId();

            var results = await Task.WhenAll(Task.Run(() => Cancel(id)), Task.Run(() => Advance(id)));

            var (cancel, advance) = (results[0], results[1]);
            Assert.That(results.Count(r => r.Status == HttpStatusCode.OK), Is.EqualTo(1), $"round {round}");
            Assert.That(results.Count(r => r.Status == HttpStatusCode.Conflict), Is.EqualTo(1), $"round {round}");
            var expected = cancel.Status == HttpStatusCode.OK ? "Cancelled" : "Started";
            var loser = cancel.Status == HttpStatusCode.OK ? advance : cancel;
            Assert.That(loser.Body.GetProperty("order").GetProperty("status").GetString(), Is.EqualTo(expected), $"round {round}");
            Assert.That((await GetOrder(id)).GetProperty("status").GetString(), Is.EqualTo(expected), $"round {round}");
        }
    }

    [Test]
    public async Task Inv9_UpdateRacingAdvance_IsLinearizable()
    {
        for (var round = 0; round < 300; round++)
        {
            var id = await SubmitNewOrderId();

            var results = await Task.WhenAll(Task.Run(() => Update(id, G4Body)), Task.Run(() => Advance(id)));

            var (update, advance) = (results[0], results[1]);
            Assert.That(advance.Status, Is.EqualTo(HttpStatusCode.OK), $"round {round}");
            var final = await GetOrder(id);
            Assert.That(final.GetProperty("status").GetString(), Is.EqualTo("Started"), $"round {round}");
            // The update either landed before the kitchen started (and its pricing stuck) or was refused.
            var expectedTotal = update.Status == HttpStatusCode.OK ? 23.24m : 32.27m;
            Assert.That(update.Status, Is.EqualTo(HttpStatusCode.OK).Or.EqualTo(HttpStatusCode.Conflict), $"round {round}");
            Assert.That(final.GetProperty("pricing").GetProperty("total").GetDecimal(), Is.EqualTo(expectedTotal), $"round {round}");
        }
    }

    [Test]
    public async Task UpdateAndCancel_WithoutIdempotencyKey_Return400()
    {
        var id = await SubmitNewOrderId();

        var (updateStatus, updateProblem, _) = await Read(await _client.PutAsJsonAsync($"/api/orders/{id}", G4Body));
        var (cancelStatus, cancelProblem, _) = await Read(await _client.PostAsync($"/api/orders/{id}/cancel", null));

        Assert.Multiple(() =>
        {
            Assert.That(updateStatus, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(updateProblem.GetProperty("errors").TryGetProperty("Idempotency-Key", out _), Is.True);
            Assert.That(cancelStatus, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(cancelProblem.GetProperty("errors").TryGetProperty("Idempotency-Key", out _), Is.True);
        });
        Assert.That((await GetOrder(id)).GetProperty("status").GetString(), Is.EqualTo("NotStarted"));
    }

    [Test]
    public async Task UnknownOrderId_Returns404_OnUpdateAndCancel()
    {
        var (updateStatus, _, _) = await Update("ord_missing", G4Body);
        var (cancelStatus, _, _) = await Cancel("ord_missing");

        Assert.That(updateStatus, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(cancelStatus, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>The body with the total the customer last saw (spec §6.1).</summary>
    private static JsonObject WithExpectedTotal(object body, decimal expectedTotal)
    {
        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        json["expectedTotal"] = expectedTotal;
        return json;
    }

    [Test]
    public async Task Inv7_Submit_ExpectedTotalMismatch_Returns409WithPricing_AndCreatesNothing()
    {
        var key = Guid.NewGuid().ToString();

        var (status, body, response) = await Submit(WithExpectedTotal(G2Body, 30.00m), key);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(body.GetProperty("error").GetString(), Is.EqualTo("totalChanged"));
            Assert.That(body.GetProperty("pricing").GetProperty("total").GetDecimal(), Is.EqualTo(32.27m));
            Assert.That(body.TryGetProperty("id", out _), Is.False);
            Assert.That(response.Headers.Location, Is.Null);
        });

        // Nothing was stored under the key: the corrected request runs fresh rather than replaying or returning 422.
        var (retryStatus, order, _) = await Submit(WithExpectedTotal(G2Body, 32.27m), key);
        Assert.That(retryStatus, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(order.GetProperty("pricing").GetProperty("total").GetDecimal(), Is.EqualTo(32.27m));
    }

    [Test]
    public async Task Inv7_Update_ExpectedTotalMismatch_Returns409WithPricing_AndLeavesTheOrderUnchanged()
    {
        var id = await SubmitNewOrderId();
        var before = await GetOrder(id);

        var (status, body, _) = await Update(id, WithExpectedTotal(G4Body, 32.27m));

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(body.GetProperty("error").GetString(), Is.EqualTo("totalChanged"));
            Assert.That(body.GetProperty("pricing").GetProperty("total").GetDecimal(), Is.EqualTo(23.24m));
        });
        Assert.That((await GetOrder(id)).GetRawText(), Is.EqualTo(before.GetRawText()));
    }

    [Test]
    public async Task Inv7_MatchingExpectedTotal_IsAccepted_OnSubmitAndUpdate()
    {
        var (submitStatus, order, _) = await Submit(WithExpectedTotal(G2Body, 32.27m));
        Assert.That(submitStatus, Is.EqualTo(HttpStatusCode.Created));

        var (updateStatus, updated, _) = await Update(order.GetProperty("id").GetString()!, WithExpectedTotal(G4Body, 23.24m));

        Assert.That(updateStatus, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(updated.GetProperty("pricing").GetProperty("total").GetDecimal(), Is.EqualTo(23.24m));
        Assert.That(updated.GetProperty("draft").TryGetProperty("expectedTotal", out _), Is.False);
    }

    [Test]
    public async Task Inv8_TenConcurrentSubmitsWithOneKey_CreateOneOrder()
    {
        for (var round = 0; round < 50; round++)
        {
            var key = Guid.NewGuid().ToString();

            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => Submit(G2Body, key))));

            Assert.That(results.Select(r => r.Status), Is.All.EqualTo(HttpStatusCode.Created), $"round {round}");
            var ids = results.Select(r => r.Body.GetProperty("id").GetString()).Distinct().ToList();
            Assert.That(ids, Has.Count.EqualTo(1), $"round {round}");
            Assert.That(results.Select(r => r.Body.GetRawText()).Distinct().Count(), Is.EqualTo(1), $"round {round}");
        }
    }

    [Test]
    public async Task Inv8_SameKeyWithDifferentBody_Returns422()
    {
        var key = Guid.NewGuid().ToString();
        var (firstStatus, _, _) = await Submit(G2Body, key);
        Assert.That(firstStatus, Is.EqualTo(HttpStatusCode.Created));

        var (status, problem, _) = await Submit(G4Body, key);

        Assert.That(status, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(problem.GetProperty("errors").TryGetProperty("Idempotency-Key", out _), Is.True);
    }

    [Test]
    public async Task Inv8_FailedSubmit_StoresNothing_SoARetryWithTheSameKeyRunsAgain()
    {
        var key = Guid.NewGuid().ToString();
        var (failedStatus, _, _) = await Submit(new { lineItems = Array.Empty<object>() }, key);
        Assert.That(failedStatus, Is.EqualTo(HttpStatusCode.BadRequest));

        var (status, _, _) = await Submit(G2Body, key);

        Assert.That(status, Is.EqualTo(HttpStatusCode.Created));
    }

    [Test]
    public async Task Inv8_KeysAreScopedByOperation()
    {
        var key = Guid.NewGuid().ToString();
        var (submitStatus, order, _) = await Submit(G2Body, key);
        Assert.That(submitStatus, Is.EqualTo(HttpStatusCode.Created));

        var (cancelStatus, cancelled, _) = await Cancel(order.GetProperty("id").GetString()!, key);

        Assert.That(cancelStatus, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(cancelled.GetProperty("status").GetString(), Is.EqualTo("Cancelled"));
    }

    [Test]
    public async Task Inv8_RepeatedUpdate_ReplaysTheStoredResponse_EvenAfterTheKitchenStarts()
    {
        var id = await SubmitNewOrderId();
        var key = Guid.NewGuid().ToString();
        var (status, updated, _) = await Update(id, G4Body, key);
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        await Advance(id);

        var (replayStatus, replayed, _) = await Update(id, G4Body, key);

        Assert.That(replayStatus, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(replayed.GetRawText(), Is.EqualTo(updated.GetRawText()));
        Assert.That((await GetOrder(id)).GetProperty("status").GetString(), Is.EqualTo("Started"));
    }

    [Test]
    public async Task Inv8_CancelKeyReusedForAnotherOrder_Returns422_AndLeavesItAlone()
    {
        var first = await SubmitNewOrderId();
        var second = await SubmitNewOrderId();
        var key = Guid.NewGuid().ToString();
        var (firstStatus, _, _) = await Cancel(first, key);
        Assert.That(firstStatus, Is.EqualTo(HttpStatusCode.OK));

        var (status, _, _) = await Cancel(second, key);

        Assert.That(status, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That((await GetOrder(second)).GetProperty("status").GetString(), Is.EqualTo("NotStarted"));
    }
}
