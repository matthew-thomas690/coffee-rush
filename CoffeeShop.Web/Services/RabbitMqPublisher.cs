using System.Text.Json;
using CoffeeShop.Contracts;
using RabbitMQ.Client;

namespace CoffeeShop.Web.Services;

public sealed record PublishResult(int PublishedCount);

public sealed class RabbitMqPublisher(IConfiguration configuration, ILogger<RabbitMqPublisher> logger)
{
    public async Task<PublishResult> PublishAsync(IReadOnlyList<CoffeeOrder> orders)
    {
        var publishedCount = 0;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var connection = await CreateFactory().CreateConnectionAsync(timeout.Token);
            var options = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);
            await using var channel = await connection.CreateChannelAsync(options, timeout.Token);

            await channel.ExchangeDeclareAsync(
                "coffee.orders", ExchangeType.Direct, durable: true, autoDelete: false,
                arguments: null, cancellationToken: timeout.Token);
            await channel.QueueDeclareAsync(
                "coffee.pending", durable: true, exclusive: false, autoDelete: false,
                arguments: null, cancellationToken: timeout.Token);
            await channel.QueueBindAsync(
                "coffee.pending", "coffee.orders", "coffee.make",
                arguments: null, cancellationToken: timeout.Token);

            foreach (var order in orders)
            {
                var body = JsonSerializer.SerializeToUtf8Bytes(order);
                var properties = new BasicProperties
                {
                    ContentType = "application/json",
                    Persistent = true,
                    MessageId = order.OrderId.ToString()
                };

                // Publisher confirms mean this count includes only broker-confirmed orders.
                await channel.BasicPublishAsync(
                    "coffee.orders", "coffee.make", mandatory: true,
                    basicProperties: properties, body: body, cancellationToken: timeout.Token);
                publishedCount++;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "RabbitMQ stopped accepting orders after {Count} publishes", publishedCount);
        }

        return new PublishResult(publishedCount);
    }

    public async Task<uint> PurgeQueueAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await CreateFactory().CreateConnectionAsync(timeout.Token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: timeout.Token);

        // Declaring the queue also makes reset safe before the first order is placed.
        await channel.QueueDeclareAsync(
            "coffee.pending", durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: timeout.Token);

        // Purge removes orders still waiting in the queue, not deliveries held by workers.
        return await channel.QueuePurgeAsync("coffee.pending", timeout.Token);
    }

    private ConnectionFactory CreateFactory() => new()
    {
        HostName = configuration["RabbitMq:HostName"] ?? "localhost",
        UserName = configuration["RabbitMq:UserName"] ?? "coffee",
        Password = configuration["RabbitMq:Password"] ?? "coffee",
        RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
    };
}
