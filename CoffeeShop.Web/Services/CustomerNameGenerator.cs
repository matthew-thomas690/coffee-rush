namespace CoffeeShop.Web.Services;

public sealed class CustomerNameGenerator
{
    private readonly string[] _firstNames;
    private readonly string[] _surnames;

    public CustomerNameGenerator(IWebHostEnvironment environment)
    {
        _firstNames = LoadNames(environment.ContentRootPath, "first-names.txt");
        _surnames = LoadNames(environment.ContentRootPath, "surnames.txt");
    }

    public string NextName() =>
        $"{_firstNames[Random.Shared.Next(_firstNames.Length)]} " +
        _surnames[Random.Shared.Next(_surnames.Length)];

    private static string[] LoadNames(string root, string fileName)
    {
        var path = Path.Combine(root, "Data", fileName);
        var names = File.ReadAllLines(path)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToArray();

        if (names.Length == 0)
        {
            throw new InvalidOperationException($"No names found in {path}.");
        }

        return names;
    }
}
