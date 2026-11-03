using MiniShop.Application.Abstractions;

namespace MiniShop.Application.Payments;

/// <summary>E-shop krok 8: uložení změn modulu Payments – platby, outbox a inbox v databázi payments.db.</summary>
public interface IPaymentsUnitOfWork : IUnitOfWork;
