namespace MiniShop.Domain.Exceptions;

public sealed class InsufficientStockException : DomainException
{
    public InsufficientStockException(string productName, int requested, int available)
        : base($"Produktu '{productName}' není dostatek skladem (požadováno {requested}, k dispozici {available}).")
    {
        ProductName = productName;
        Requested = requested;
        Available = available;
    }

    public string ProductName { get; }
    public int Requested { get; }
    public int Available { get; }
}
