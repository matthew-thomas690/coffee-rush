const placed = document.getElementById("placed");
const completed = document.getElementById("completed");
const outstanding = document.getElementById("outstanding");
const activityList = document.getElementById("activity-list");
const orderMessage = document.getElementById("order-message");
const resetButton = document.getElementById("reset-button");
const connectionStatus = document.getElementById("connection-status");
const orderButtons = document.querySelectorAll(".order-button");
let sendingOrder = false;

function showMessage(message, isError = false) {
  orderMessage.textContent = message;
  orderMessage.classList.toggle("error", isError);
}

function render(snapshot) {
  placed.textContent = snapshot.placed;
  completed.textContent = snapshot.completed;
  outstanding.textContent = snapshot.outstanding;
  resetButton.disabled = !snapshot.canReset;

  const percent = snapshot.placed === 0 ? 0 : Math.round(snapshot.completed / snapshot.placed * 100);
  document.getElementById("progress-text").textContent = `${percent}%`;
  document.getElementById("progress-fill").style.width = `${percent}%`;
  document.querySelector(".progress-track").setAttribute("aria-valuenow", percent);

  const state = document.getElementById("experiment-state");
  state.classList.toggle("complete", snapshot.isComplete);
  state.textContent = snapshot.isComplete
    ? "All coffees are ready. Experiment complete!"
    : snapshot.outstanding > 0
      ? `${snapshot.outstanding} ${snapshot.outstanding === 1 ? "coffee is" : "coffees are"} waiting or being made.`
      : "Place an order to begin an experiment.";

  activityList.replaceChildren();
  if (snapshot.recentCompletions.length === 0) {
    const empty = document.createElement("li");
    empty.className = "empty-activity";
    empty.textContent = "No coffees made yet. The baristas will appear here.";
    activityList.append(empty);
    return;
  }

  for (const coffee of snapshot.recentCompletions) {
    const item = document.createElement("li");
    const person = document.createElement("div");
    const avatar = document.createElement("span");
    const details = document.createElement("span");
    const name = document.createElement("strong");
    const drink = document.createElement("small");
    const worker = document.createElement("span");

    person.className = "activity-person";
    avatar.className = "activity-avatar";
    avatar.textContent = coffee.customerName.slice(0, 1);
    name.textContent = coffee.customerName;
    drink.textContent = coffee.drink;
    worker.className = "activity-worker";
    worker.textContent = `Made by ${coffee.workerId}`;

    details.append(name, drink);
    person.append(avatar, details);
    item.append(person, worker);
    activityList.append(item);
  }
}

async function refresh() {
  try {
    const response = await fetch("/api/experiment");
    if (!response.ok) throw new Error("Could not load the experiment.");
    render(await response.json());
  } catch (error) {
    showMessage(error.message, true);
  }
}

async function placeOrders(count) {
  if (sendingOrder) return;
  sendingOrder = true;
  orderButtons.forEach(button => button.disabled = true);
  document.querySelector(".primary-button").disabled = true;
  showMessage(`Sending ${count} ${count === 1 ? "order" : "orders"} to RabbitMQ…`);

  try {
    const response = await fetch("/api/orders", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ count, drink: document.getElementById("drink").value })
    });
    const result = await response.json();
    if (result.error) {
      showMessage(result.publishedCount
        ? `${result.publishedCount} orders were queued. ${result.error}`
        : result.error, true);
    } else {
      showMessage(`${result.publishedCount} ${result.publishedCount === 1 ? "order was" : "orders were"} queued. No need to wait at the counter!`);
    }
    await refresh();
  } catch {
    showMessage("The website could not send the order. Please try again.", true);
  } finally {
    sendingOrder = false;
    orderButtons.forEach(button => button.disabled = false);
    document.querySelector(".primary-button").disabled = false;
  }
}

orderButtons.forEach(button => button.addEventListener("click", () => placeOrders(Number(button.dataset.count))));
document.getElementById("custom-order-form").addEventListener("submit", event => {
  event.preventDefault();
  const countInput = document.getElementById("custom-count");
  if (countInput.reportValidity()) placeOrders(Number(countInput.value));
});

resetButton.addEventListener("click", async () => {
  resetButton.disabled = true;
  showMessage("Clearing waiting orders from RabbitMQ…");
  try {
    const response = await fetch("/api/reset", { method: "POST" });
    const result = await response.json();
    if (!response.ok) throw new Error(result.error || "Could not reset the experiment.");
    render(result);
    showMessage("Waiting orders cleared. New experiment started.");
  } catch (error) {
    showMessage(error.message, true);
    await refresh();
  }
});

const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/completions")
  .withAutomaticReconnect()
  .build();

connection.on("ExperimentUpdated", render);
connection.onreconnecting(() => {
  connectionStatus.textContent = "Reconnecting…";
  connectionStatus.classList.remove("connected");
});
connection.onreconnected(() => {
  connectionStatus.textContent = "Connected live";
  connectionStatus.classList.add("connected");
  refresh();
});
connection.onclose(() => {
  connectionStatus.textContent = "Reconnecting…";
  connectionStatus.classList.remove("connected");
  setTimeout(connect, 3000);
});

async function connect() {
  try {
    await connection.start();
    connectionStatus.textContent = "Connected live";
    connectionStatus.classList.add("connected");
    await refresh();
  } catch {
    connectionStatus.textContent = "Reconnecting…";
    setTimeout(connect, 3000);
  }
}

refresh();
connect();
