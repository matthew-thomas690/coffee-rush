namespace CoffeeShop.Web.Models;

public sealed record PlaceOrdersRequest(int Count, string Drink);

public sealed record PlaceOrdersResponse(int PublishedCount, string? Error);

public static class CoffeeMenu
{
    public static readonly string[] Drinks = ["Espresso", "Latte", "Cappuccino", "Flat White"];
}
