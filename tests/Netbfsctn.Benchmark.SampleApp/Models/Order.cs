namespace Netbfsctn.Benchmark.SampleApp.Models;

internal class OrderItem
{
    private string _productName;
    private int _quantity;
    private decimal _unitPrice;

    public string ProductName => _productName;
    public int Quantity => _quantity;
    public decimal UnitPrice => _unitPrice;

    public OrderItem(string productName, int quantity, decimal unitPrice)
    {
        _productName = productName;
        _quantity = quantity;
        _unitPrice = unitPrice;
    }

    internal decimal GetSubtotal()
    {
        return _quantity * _unitPrice;
    }

    internal string GetDescription()
    {
        return $"{_productName} x{_quantity} @ {_unitPrice:C}";
    }
}

internal class Order
{
    private int _orderId;
    private DateTime _orderDate;
    private readonly List<OrderItem> _items = [];
    private string _status;

    public int OrderId => _orderId;
    public DateTime OrderDate => _orderDate;
    public string Status => _status;

    public Order(int orderId, DateTime orderDate)
    {
        _orderId = orderId;
        _orderDate = orderDate;
        _status = "Pending";
    }

    internal void AddItem(OrderItem item)
    {
        _items.Add(item);
    }

    internal decimal GetTotal()
    {
        decimal total = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            total += _items[i].GetSubtotal();
        }
        return total;
    }

    internal void Process()
    {
        switch (_status)
        {
            case "Pending":
                _status = "Processing";
                break;
            case "Processing":
                _status = "Shipped";
                break;
            case "Shipped":
                _status = "Delivered";
                break;
            default:
                _status = "Unknown";
                break;
        }
    }

    internal string GetSummary()
    {
        var total = GetTotal();
        return $"Order #{_orderId} ({_orderDate:yyyy-MM-dd}): {_items.Count} items, Total: {total:F2}, Status: {_status}";
    }
}
