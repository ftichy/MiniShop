using RabbitMQ.Client.Exceptions;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// Proud B3: fronty, které kód už nepoužívá, a jejich bezpečné smazání. Trvalá fronta navázaná na exchange by bez
/// posluchače jen rostla (každá zpráva se do ní dál kopíruje) – proto ji nestačí přestat poslouchat, musí zmizet.
/// </summary>
/// <remarks>
/// Maže se s podmínkou <c>ifUnused</c>: dokud frontu poslouchá stará kopie služby (postupné nasazení), RabbitMQ smazání
/// odmítne a zkusí se to později. Kdyby se smazala hned, starý posluchač by si ji při obnově spojení založil znovu.
/// Zprávy ve frontě se zahodí záměrně (<c>ifEmpty: false</c>). Neexistující fronta = hotovo.
/// </remarks>
public static class RabbitMqObsoleteQueues
{
    /// <summary>Auditní „odposlech“ z EDA kroku 8 – nahradil ho proud <c>minishop.events</c> (B1); logoval celé zprávy i s osobními údaji.</summary>
    public const string AuditQueue = "minishop.audit";

    public static readonly IReadOnlyList<string> Names = [AuditQueue, RabbitMqDeadLetters.DeadQueueFor(AuditQueue)];

    /// <summary>Zkusí smazat každou zrušenou frontu; vrátí ty, které zatím zůstaly (někdo je ještě poslouchá).</summary>
    public static async Task<IReadOnlyList<string>> DeleteUnusedAsync(RabbitMqConnection connection, IEnumerable<string> queues,
        CancellationToken cancellationToken)
    {
        var remaining = new List<string>();
        foreach (var queue in queues)
        {
            // Odmítnuté smazání (PRECONDITION_FAILED) zavře kanál – každý pokus proto na vlastním kanálu.
            await using var channel = await connection.CreateChannelAsync(publisherConfirms: false, cancellationToken);
            try
            {
                await channel.QueueDeleteAsync(queue, ifUnused: true, ifEmpty: false, cancellationToken: cancellationToken);
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
            {
                remaining.Add(queue);
            }
        }

        return remaining;
    }
}
