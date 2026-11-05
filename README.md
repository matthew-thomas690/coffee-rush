# The Coffee Rush

A small distributed coffee shop for a one-hour coding workshop. The website takes orders quickly, RabbitMQ holds them in one shared queue, and each worker container acts as one barista. Every coffee takes three seconds to make. More workers improve total throughput without changing the code.

Open `CoffeeShop.sln` in an IDE to see the website, worker, and shared contracts together.

## Start

```bash
docker compose up --build -d
```

Open the dashboard at <http://localhost:8080>. By default, Compose starts the website and RabbitMQ with **no barista workers**. Place a few orders and watch them wait in the queue before hiring a barista.

Open RabbitMQ Management at <http://localhost:15672> and sign in with username `coffee` and password `coffee`. Under **Queues and Streams → coffee.pending**, watch **Ready** messages wait for a barista, **Unacked** messages being made, and the number of consumers.

Start one barista when you are ready to make the queued coffees:

```bash
docker compose --profile baristas up -d --scale worker=1
```

To see which barista made each coffee:

```bash
docker compose logs -f worker
```

## Run the scaling experiment

1. Start one worker with the command above and place 30 orders. Watch the outstanding count and RabbitMQ queue.
2. Once all 30 are complete, use **Reset & clear queue** on the dashboard.
3. Start three workers, then place 30 orders again:

   ```bash
   docker compose --profile baristas up -d --scale worker=3
   ```

4. Repeat with five workers:

   ```bash
   docker compose --profile baristas up -d --scale worker=5
   ```

Compose creates identical worker containers. Each worker consumes from the **same** `coffee.pending` queue with manual acknowledgements and prefetch count 1. The preparation time remains three seconds per coffee; more workers make coffees in parallel.

To stop and remove the workshop containers:

```bash
docker compose --profile baristas down
```

## How an order travels

1. The browser calls `POST /api/orders` with a drink and a count of 1–100. Web mixes random names from `CoffeeShop.Web/Data/first-names.txt` and `surnames.txt`, then gives every order a unique GUID `OrderId` and the current experiment's GUID `RunId`.
2. Web publishes each JSON order to the `coffee.orders` direct exchange with routing key `coffee.make`. RabbitMQ routes it to `coffee.pending`. Web returns after RabbitMQ confirms publication; it does **not** wait for a coffee to be made.
3. An available worker receives one order, logs the customer and drink with its container identity, and waits asynchronously for three seconds.
4. The worker invokes `CompleteOrder` on Web's SignalR hub at `/hubs/completions`. Web records the completion once, updates connected browsers, and returns a result to the worker.
5. The worker acknowledges the RabbitMQ delivery only after the hub returns `recorded`, `duplicate`, or `old-run`. If the callback fails, it keeps the delivery unacknowledged and retries. A redelivered order cannot increment the completed count twice.

`CoffeeShop.Contracts` contains the two shared C# records: `CoffeeOrder` for the queue and `CompletionNotification` for the SignalR callback. The queue carries work **to** baristas; SignalR carries completion notices **back** to the website and live updates **to** browsers.

## Reset

**Reset & clear queue** purges messages still waiting in `coffee.pending`, clears Web's in-memory statistics, and creates a new `RunId`. Reset waits for any current order submission to finish. If RabbitMQ is unavailable, Web leaves the experiment unchanged.

RabbitMQ cannot purge a delivery that a worker has already taken but not acknowledged. That barista may finish; Web ignores the callback from the previous run, and the worker acknowledges the `old-run` result. Web has no database, so restarting it loses the current experiment's statistics. Restart the full Compose stack between workshop sessions.

The browser's SignalR JavaScript client is bundled locally in `CoffeeShop.Web/wwwroot/lib/signalr.min.js` (`@microsoft/signalr` 8.0.7). Its MIT licence is alongside it in `SIGNALR-LICENSE.txt`.
