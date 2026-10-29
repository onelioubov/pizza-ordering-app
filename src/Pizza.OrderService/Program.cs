using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Pizza.OrderService.Orders;
using Pizza.OrderService.Pricing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PricingEngine>();
builder.Services.AddSingleton<OrderBook>();
builder.Services.AddProblemDetails();
// Unreadable bodies throw, so the exception handler below can answer them as ProblemDetails.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.Configure<JsonOptions>(o =>
{
    // Statuses are written as the spec names them ("NotStarted"); other enums are camelCase ("percent").
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter<OrderStatus>(allowIntegerValues: false));
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    o.SerializerOptions.Converters.Add(new MoneyJsonConverter());
});

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    ExceptionHandler = async context =>
    {
        var result = context.Features.Get<IExceptionHandlerFeature>()?.Error switch
        {
            InvalidDraftException e => Results.ValidationProblem(e.Errors.ToDictionary(kv => kv.Key, kv => kv.Value)),
            BadHttpRequestException e => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["body"] = ["We couldn't read your order."] }, statusCode: e.StatusCode),
            OrderNotFoundException => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "We couldn't find that order."),
            OrderStatusConflictException e => Results.Conflict(new { error = "statusConflict", order = e.Current }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
        await result.ExecuteAsync(context);
    },
    // Customer input errors are expected; don't log them as unhandled exceptions.
    SuppressDiagnosticsCallback = c => c.Exception is InvalidDraftException or BadHttpRequestException
        or OrderNotFoundException or OrderStatusConflictException,
});

var api = app.MapGroup("/api");

api.MapGet("/menu", (PricingEngine engine) => new
{
    sizes = engine.Menu.Sizes.Values.Select(s => new { id = s.Id, name = s.Name, basePrice = s.BasePrice }),
    toppings = engine.Menu.Toppings.Values.Select(t => new { id = t.Id, name = t.Name, category = t.Category }),
});

api.MapPost("/quotes", (OrderDraft draft, PricingEngine engine) => engine.Price(draft));

api.MapPost("/orders", (OrderRequest request, [Microsoft.AspNetCore.Mvc.FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, OrderBook orders) =>
{
    if (string.IsNullOrWhiteSpace(idempotencyKey))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Idempotency-Key header is required."] });
    // Slice 6 stores the key and compares ExpectedTotal (spec §6.1, §6.2); until then both are accepted and unused.
    var order = orders.Submit(request.ToDraft());
    return Results.Created($"/api/orders/{order.Id}", order);
});

api.MapGet("/orders/{id}", (string id, OrderBook orders) => orders.Get(id));

api.MapPost("/orders/{id}/advance", (string id, OrderBook orders) => orders.Advance(id));

app.Run();

/// <summary>The submit and update body: a draft plus the total the customer last saw (spec §6.1).</summary>
internal sealed record OrderRequest(IReadOnlyList<LineItemDraft>? LineItems, string? PromoCode, TipDraft? Tip, decimal? ExpectedTotal)
{
    public OrderDraft ToDraft() => new(LineItems, PromoCode, Tip);
}

/// <summary>
/// Writes money with at least 2 decimal places (spec §8). Pads only: amounts are already cents
/// (Money.RoundCents), and this never drops a digit.
/// </summary>
internal sealed class MoneyJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("0.00##########################", CultureInfo.InvariantCulture));
}
