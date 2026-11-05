using System.Text.Json;
using CoffeeShop.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CoffeeShop.Worker;

public sealed class BaristaWorker(IConfiguration configuration, ILogger<BaristaWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
        var hubUrl = configuration["Web:HubUrl"] ?? "http://web:8080/hubs/completions";

        await using var hub = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        logger.LogInformation("Barista {WorkerId} is ready to work", workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeOrdersAsync(hub, workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Barista {WorkerId} lost RabbitMQ; reconnecting", workerId);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ConsumeOrdersAsync(HubConnection hub, string workerId, CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMq:HostName"] ?? "rabbitmq",
            UserName = configuration["RabbitMq:UserName"] ?? "coffee",
            Password = configuration["RabbitMq:Password"] ?? "coffee",
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            RequestedHeartbeat = TimeSpan.FromSeconds(10),
            AutomaticRecoveryEnabled = false
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            "coffee.pending", durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: stoppingToken);

        // One unacknowledged delivery per barista means one coffee at a time.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            // RabbitMQ may reuse delivery memory after this callback returns.
            var body = delivery.Body.ToArray();
            await MakeCoffeeAsync(body, delivery.DeliveryTag, channel, hub, workerId, stoppingToken);
        };

        await channel.BasicConsumeAsync(
            "coffee.pending", autoAck: false, consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("Barista {WorkerId} is waiting at coffee.pending", workerId);

        // Initial connection failures and later connection losses both return to the retry loop.
        while (connection.IsOpen && channel.IsOpen)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task MakeCoffeeAsync(
        byte[] body,
        ulong deliveryTag,
        IChannel channel,
        HubConnection hub,
        string workerId,
        CancellationToken stoppingToken)
    {
        CoffeeOrder? order;

        try
        {
            order = JsonSerializer.Deserialize<CoffeeOrder>(body);
            if (order is null || order.OrderId == Guid.Empty || order.RunId == Guid.Empty)
            {
                throw new JsonException("The coffee order is missing an ID.");
            }
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Barista {WorkerId} received an invalid order", workerId);
            await channel.BasicRejectAsync(deliveryTag, requeue: false, stoppingToken);
            return;
        }

        logger.LogInformation(
            "Barista {WorkerId} is making a {Drink} for {CustomerName} ({OrderId})",
            workerId, order.Drink, order.CustomerName, order.OrderId);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            var completion = new CompletionNotification(
                order.OrderId, order.RunId, order.CustomerName, workerId);
            var result = await ReportCompletionAsync(hub, completion, stoppingToken);

            // SignalR confirms the website handled the callback before RabbitMQ is acknowledged.
            await channel.BasicAckAsync(deliveryTag, multiple: false, stoppingToken);
            logger.LogInformation(
                "Barista {WorkerId} finished {CustomerName}'s {Drink}: {Result}",
                workerId, order.CustomerName, order.Drink, result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Closing the channel gives an unfinished delivery back to RabbitMQ.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Barista {WorkerId} could not finish order {OrderId}; returning it to the queue",
                workerId, order.OrderId);

            if (channel.IsOpen)
            {
                try
                {
                    await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true,
                        cancellationToken: stoppingToken);
                }
                catch (Exception nackException)
                {
                    logger.LogWarning(nackException, "RabbitMQ will redeliver after the connection closes");
                }
            }
        }
    }

    private async Task<string> ReportCompletionAsync(
        HubConnection hub,
        CompletionNotification completion,
        CancellationToken stoppingToken)
    {
        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                if (hub.State == HubConnectionState.Disconnected)
                {
                    await hub.StartAsync(stoppingToken);
                }

                var result = await hub.InvokeAsync<string>(
                    "CompleteOrder", completion, stoppingToken);

                if (result is "recorded" or "duplicate" or "old-run")
                {
                    return result;
                }

                logger.LogWarning(
                    "Website returned {Result} for order {OrderId}; retrying callback",
                    result, completion.OrderId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Could not report order {OrderId} to the website; retrying callback",
                    completion.OrderId);
            }

            // Keep this RabbitMQ delivery unacknowledged until the website accepts it.
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
