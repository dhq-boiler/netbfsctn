namespace Netbfsctn.Benchmark.SampleApp.Models;

internal enum CustomerType
{
    Regular,
    Premium,
    Enterprise
}

internal class Customer
{
    private string _firstName;
    private string _lastName;
    private int _age;
    private CustomerType _type;
    private readonly List<Order> _orders = [];

    public string FirstName => _firstName;
    public string LastName => _lastName;
    public int Age => _age;
    public CustomerType Type => _type;

    public Customer(string firstName, string lastName, int age, CustomerType type)
    {
        _firstName = firstName;
        _lastName = lastName;
        _age = age;
        _type = type;
    }

    internal string GetFullName()
    {
        return _firstName + " " + _lastName;
    }

    internal string GetDisplayInfo()
    {
        return $"Customer: {GetFullName()}, Age: {_age}, Type: {_type}";
    }

    internal void AddOrder(Order order)
    {
        _orders.Add(order);
    }

    internal decimal GetTotalSpent()
    {
        decimal total = 0;
        foreach (var order in _orders)
        {
            total += order.GetTotal();
        }
        return total;
    }

    internal string GetLoyaltyTier()
    {
        var totalSpent = GetTotalSpent();
        if (totalSpent >= 10000m)
            return "Platinum";
        else if (totalSpent >= 5000m)
            return "Gold";
        else if (totalSpent >= 1000m)
            return "Silver";
        else
            return "Bronze";
    }
}
