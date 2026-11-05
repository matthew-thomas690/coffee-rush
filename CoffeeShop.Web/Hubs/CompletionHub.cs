using CoffeeShop.Contracts;
using CoffeeShop.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace CoffeeShop.Web.Hubs;

public sealed class CompletionHub(ExperimentState experiment) : Hub
{
    // The worker awaits this result before acknowledging its RabbitMQ delivery.
    public async Task<string> CompleteOrder(CompletionNotification completion)
    {
        var result = experiment.RecordCompletion(completion);

        if (result is "recorded" or "duplicate")
        {
            await Clients.All.SendAsync("ExperimentUpdated", experiment.GetSnapshot());
        }

        return result;
    }
}
