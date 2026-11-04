using System.Text;
using RabbitMQ.Client;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// EDA krok 13: omezené opakování a dead-letter fronty consumerů. Zpráva, kterou handler opakovaně nezvládne
/// (nebo kterou nejde přečíst), skončí ve frontě <c>{fronta}.dead</c> – neztratí se a neběží donekonečna v kruhu.
/// </summary>
/// <remarks>
/// Počet pokusů nese hlavička <see cref="AttemptHeader"/>: consumer selhanou zprávu nepošle zpět přes „nack + requeue“
/// (hlavičky by se nezměnily a pokusy by nešlo počítat), ale publikuje její kopii s vyšším číslem do stejné fronty
/// a originál potvrdí. Existující fronty se nemění (žádné nové argumenty – RabbitMQ by je u existující fronty odmítl).
/// </remarks>
public static class RabbitMqDeadLetters
{
    public const string AttemptHeader = "x-minishop-attempt";
    public const string ErrorHeader = "x-minishop-error";
    public const string DeadAtHeader = "x-minishop-dead-at";

    /// <summary>Kolikrát nejvýš handler zprávu zkusí, než skončí v dead-letter frontě.</summary>
    public const int MaxDeliveryAttempts = 5;

    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(2);
    private const int MaxErrorLength = 500;

    public static string DeadQueueFor(string queue) => $"{queue}.dead";

    /// <summary>Kolikrát už handler zprávu nezvládl (0 = první doručení).</summary>
    public static int ReadAttempts(IReadOnlyBasicProperties properties) =>
        properties.Headers?.TryGetValue(AttemptHeader, out var value) == true
            ? value switch
            {
                int number => number,
                long number => (int)number,
                byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var number) => number,
                _ => 0
            }
            : 0;

    /// <summary>Pauza před dalším pokusem: 2, 4, 8, 16 s (exponenciální backoff).</summary>
    public static TimeSpan RetryDelay(int attempts) => BaseRetryDelay * Math.Pow(2, Math.Max(0, attempts - 1));

    /// <summary>Kopie vlastností přijaté zprávy (typ, ID, trasa…) s novým počtem pokusů, případně s chybou.</summary>
    public static BasicProperties CopyProperties(IReadOnlyBasicProperties source, int attempts, string? error = null)
    {
        var headers = source.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(source.Headers);
        headers[AttemptHeader] = attempts;
        if (attempts == 0)
        {
            // Návrat z dead-letter fronty (admin) – nová šance, stará chyba už neplatí.
            headers.Remove(ErrorHeader);
            headers.Remove(DeadAtHeader);
        }

        if (error is not null)
        {
            headers[ErrorHeader] = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
            headers[DeadAtHeader] = DateTimeOffset.UtcNow.ToString("O");
        }

        return new BasicProperties
        {
            MessageId = source.MessageId,
            Type = source.Type,
            ContentType = source.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = source.Timestamp,
            AppId = source.AppId,
            Headers = headers
        };
    }

    public static Task DeclareDeadQueueAsync(IChannel channel, string queue, CancellationToken cancellationToken) =>
        channel.QueueDeclareAsync(DeadQueueFor(queue), durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
}
