namespace CoffeeShop.Contracts;

// Data sent back to the website through SignalR after making a coffee.
public sealed record CompletionNotification(
    Guid OrderId,
    Guid RunId,
    string CustomerName,
    string WorkerId);
