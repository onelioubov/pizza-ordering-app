using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Pizza.OrderService.Pricing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PricingEngine>();
builder.Services.AddProblemDetails();
// Unreadable bodies throw, so the exception handler below can answer them as ProblemDetails.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.Configure<JsonOptions>(o =>
{
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
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
        await result.ExecuteAsync(context);
    },
    // Customer input errors are expected; don't log them as unhandled exceptions.
    SuppressDiagnosticsCallback = c => c.Exception is InvalidDraftException or BadHttpRequestException,
});

var api = app.MapGroup("/api");

api.MapGet("/menu", (PricingEngine engine) => new
{
    sizes = engine.Menu.Sizes.Values.Select(s => new { id = s.Id, name = s.Name, basePrice = s.BasePrice }),
    toppings = engine.Menu.Toppings.Values.Select(t => new { id = t.Id, name = t.Name, category = t.Category }),
});

api.MapPost("/quotes", (OrderDraft draft, PricingEngine engine) => engine.Price(draft));

app.Run();

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
