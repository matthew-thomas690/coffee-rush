using CoffeeShop.Contracts;

namespace CoffeeShop.Web.Services;

public sealed record CompletedCoffee(
    Guid OrderId,
    string CustomerName,
    string Drink,
    string WorkerId,
    DateTimeOffset CompletedAt);

public sealed record ExperimentSnapshot(
    Guid RunId,
    int Placed,
    int Completed,
    int Outstanding,
    bool CanReset,
    bool IsComplete,
    CompletedCoffee[] RecentCompletions);

public sealed class ExperimentState
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, OrderEntry> _orders = new();
    private readonly List<CompletedCoffee> _recentCompletions = new();
    private Guid _runId = Guid.NewGuid();
    private int _submissionsInProgress;
    private bool _resetInProgress;

    public bool TryBeginSubmission(out Guid runId)
    {
        lock (_gate)
        {
            runId = _runId;
            if (_resetInProgress)
            {
                return false;
            }

            _submissionsInProgress++;
            return true;
        }
    }

    public void ReserveOrders(IEnumerable<CoffeeOrder> orders)
    {
        lock (_gate)
        {
            foreach (var order in orders)
            {
                if (order.RunId != _runId)
                {
                    throw new InvalidOperationException("The experiment changed while placing orders.");
                }

                _orders.Add(order.OrderId, new OrderEntry(order));
            }
        }
    }

    public void FinishSubmission(IReadOnlyList<CoffeeOrder> orders, int publishedCount)
    {
        lock (_gate)
        {
            for (var index = 0; index < orders.Count; index++)
            {
                var entry = _orders[orders[index].OrderId];

                if (index < publishedCount || entry.Completed)
                {
                    entry.Published = true;
                }
                else
                {
                    _orders.Remove(orders[index].OrderId);
                }
            }
        }
    }

    public void EndSubmission()
    {
        lock (_gate)
        {
            _submissionsInProgress--;
        }
    }

    public string RecordCompletion(CompletionNotification completion)
    {
        lock (_gate)
        {
            if (completion.RunId != _runId)
            {
                return "old-run";
            }

            if (!_orders.TryGetValue(completion.OrderId, out var entry) ||
                completion.CustomerName != entry.Order.CustomerName ||
                string.IsNullOrWhiteSpace(completion.WorkerId))
            {
                return "unknown-order";
            }

            if (entry.Completed)
            {
                return "duplicate";
            }

            entry.Published = true;
            entry.Completed = true;

            _recentCompletions.Insert(0, new CompletedCoffee(
                entry.Order.OrderId,
                entry.Order.CustomerName,
                entry.Order.Drink,
                completion.WorkerId.Trim(),
                DateTimeOffset.UtcNow));

            if (_recentCompletions.Count > 10)
            {
                _recentCompletions.RemoveAt(10);
            }

            return "recorded";
        }
    }

    public ExperimentSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return Snapshot();
        }
    }

    public bool TryBeginReset()
    {
        lock (_gate)
        {
            if (_submissionsInProgress > 0 || _resetInProgress)
            {
                return false;
            }

            _resetInProgress = true;
            return true;
        }
    }

    public ExperimentSnapshot CompleteReset()
    {
        lock (_gate)
        {
            _runId = Guid.NewGuid();
            _orders.Clear();
            _recentCompletions.Clear();
            _resetInProgress = false;
            return Snapshot();
        }
    }

    public void CancelReset()
    {
        lock (_gate)
        {
            _resetInProgress = false;
        }
    }

    private ExperimentSnapshot Snapshot()
    {
        var placed = _orders.Values.Count(order => order.Published);
        var completed = _orders.Values.Count(order => order.Completed);
        var outstanding = placed - completed;

        return new ExperimentSnapshot(
            _runId,
            placed,
            completed,
            outstanding,
            _submissionsInProgress == 0 && !_resetInProgress,
            placed > 0 && _submissionsInProgress == 0 && outstanding == 0,
            _recentCompletions.ToArray());
    }

    private sealed class OrderEntry(CoffeeOrder order)
    {
        public CoffeeOrder Order { get; } = order;
        public bool Published { get; set; }
        public bool Completed { get; set; }
    }
}
