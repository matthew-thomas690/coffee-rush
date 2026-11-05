namespace CoffeeShop.Contracts;

// JSON sent through the RabbitMQ work queue.
public sealed record CoffeeOrder(
    Guid OrderId,
    Guid RunId,
    string CustomerName,
    string Drink);
