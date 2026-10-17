namespace MiniShop.Application.Ordering;

public interface IOrderService
{
    Task<IReadOnlyList<OrderDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<OrderDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<OrderDto> PlaceOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> CompleteAsync(int id, CancellationToken cancellationToken = default);
    Task<OrderDto> CancelAsync(int id, CancellationToken cancellationToken = default);
}
