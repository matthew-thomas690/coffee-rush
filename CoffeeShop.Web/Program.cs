using CoffeeShop.Contracts;
using CoffeeShop.Web.Hubs;
using CoffeeShop.Web.Models;
using CoffeeShop.Web.Services;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<ExperimentState>();
builder.Services.AddSingleton<CustomerNameGenerator>();
builder.Services.AddSingleton<RabbitMqPublisher>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/experiment", (ExperimentState experiment) => experiment.GetSnapshot());

app.MapPost("/api/orders", async (
    PlaceOrdersRequest request,
    ExperimentState experiment,
    CustomerNameGenerator names,
    RabbitMqPublisher publisher,
    IHubContext<CompletionHub> hub) =>
{
    if (request.Count is < 1 or > 100 ||
        !CoffeeMenu.Drinks.Contains(request.Drink, StringComparer.Ordinal))
    {
        return Results.BadRequest(new { Error = "Choose 1–100 orders and a drink from the menu." });
    }

    if (!experiment.TryBeginSubmission(out var runId))
    {
        return Results.Conflict(new { Error = "Wait for the reset to finish before placing orders." });
    }

    PublishResult result;

    try
    {
        var orders = Enumerable.Range(0, request.Count)
            .Select(_ => new CoffeeOrder(
                Guid.NewGuid(),
                runId,
                names.NextName(),
                request.Drink))
            .ToArray();

        // Register IDs before publishing, so even a very fast callback is recognised.
        experiment.ReserveOrders(orders);
        result = await publisher.PublishAsync(orders);
        experiment.FinishSubmission(orders, result.PublishedCount);
    }
    finally
    {
        experiment.EndSubmission();
    }

    await hub.Clients.All.SendAsync("ExperimentUpdated", experiment.GetSnapshot());

    var response = new PlaceOrdersResponse(
        result.PublishedCount,
        result.PublishedCount == request.Count
            ? null
            : "RabbitMQ could not accept every order. Please try again.");

    return Results.Json(response, statusCode: result.PublishedCount == 0 ? 503 : 200);
});

app.MapPost("/api/reset", async (
    ExperimentState experiment,
    RabbitMqPublisher publisher,
    IHubContext<CompletionHub> hub,
    ILogger<Program> logger) =>
{
    if (!experiment.TryBeginReset())
    {
        return Results.Conflict(new { Error = "Wait for the current order submission or reset to finish." });
    }

    try
    {
        var purgedCount = await publisher.PurgeQueueAsync();
        logger.LogInformation("Reset removed {Count} waiting orders from RabbitMQ", purgedCount);
    }
    catch (Exception exception)
    {
        experiment.CancelReset();
        logger.LogError(exception, "Could not purge the coffee queue");
        return Results.Json(
            new { Error = "RabbitMQ is unavailable, so the experiment was not reset." },
            statusCode: 503);
    }

    var snapshot = experiment.CompleteReset();
    await hub.Clients.All.SendAsync("ExperimentUpdated", snapshot);
    return Results.Ok(snapshot);
});

app.MapHub<CompletionHub>("/hubs/completions");

app.Run();
