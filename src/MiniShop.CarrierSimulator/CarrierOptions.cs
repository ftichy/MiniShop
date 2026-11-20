namespace MiniShop.CarrierSimulator;

/// <summary>
/// Nastavení simulátoru (sekce <c>Carrier</c>, v kontejneru proměnné <c>Carrier__…</c>). Klíč a tajemství webhooků jsou
/// povinné – bez nich se simulátor nespustí (fail fast, stejně jako servisní klíče služeb MiniShopu).
/// </summary>
public sealed class CarrierOptions
{
    public const string SectionName = "Carrier";

    /// <summary>Klíč e-shopu u dopravce (hlavička <c>X-Api-Key</c>). Min. 32 znaků.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Sdílené tajemství pro podpis webhooků (HMAC-SHA256, hlavička <c>Carrier-Signature</c>). Min. 32 znaků.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Kam posílat webhooky o stavu zásilek. Prázdné = neposílat (stav jde jen přečíst přes GET).</summary>
    public string WebhookUrl { get; set; } = "";

    /// <summary>Složka se souborem zásilek (v kontejneru <c>/data</c>).</summary>
    public string DataPath { get; set; } = "data";

    /// <summary>Po kolika sekundách se zásilka posune o jeden stav (převzato → na cestě → doručuje se → doručeno).</summary>
    public int StepSeconds { get; set; } = 30;

    /// <summary>Jak často plánovač kontroluje, co je na řadě.</summary>
    public int TickMilliseconds { get; set; } = 1000;

    /// <summary>Doručené zásilky (i s adresou příjemce) se smažou po tolika dnech.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Kolikrát zkusit doručit jeden webhook, než ho dopravce vzdá (pak už jen GET).</summary>
    public int MaxWebhookAttempts { get; set; } = 8;

    public ChaosOptions Chaos { get; set; } = new();

    public void Validate()
    {
        if (ApiKey.Length < 32)
        {
            throw new InvalidOperationException("Chybí Carrier:ApiKey (aspoň 32 znaků) – simulátor dopravce se nespustí.");
        }

        if (WebhookSecret.Length < 32)
        {
            throw new InvalidOperationException("Chybí Carrier:WebhookSecret (aspoň 32 znaků) – simulátor dopravce se nespustí.");
        }

        if (StepSeconds < 1 || TickMilliseconds < 10 || RetentionDays < 1 || MaxWebhookAttempts < 1)
        {
            throw new InvalidOperationException("Carrier: StepSeconds ≥ 1, TickMilliseconds ≥ 10, RetentionDays ≥ 1, MaxWebhookAttempts ≥ 1.");
        }

        Chaos.Validate();
    }
}

/// <summary>
/// „Zlobení“ dopravce – pravděpodobnosti 0–1. Výchozí 0 = spolehlivý dopravce; produkce a lokální zkoušky si je zapnou.
/// Každé zlobení odpovídá skutečnému problému integrace s cizí službou.
/// </summary>
public sealed class ChaosOptions
{
    /// <summary>Odpověď 503 hned (nic nevznikne) – e-shop musí zopakovat.</summary>
    public double FailureRate { get; set; }

    /// <summary>Zásilka vznikne, ale odpověď „se ztratí“ (503) – opakování se stejným Idempotency-Key ji musí najít.</summary>
    public double LostResponseRate { get; set; }

    /// <summary>Krok 26B: dopravce na adresu nedoručuje (422 <c>address_not_serviceable</c>) – trvalá chyba, rozhodne člověk.</summary>
    public double RejectRate { get; set; }

    /// <summary>Webhook přijde dvakrát (stejné ID události) – e-shop ho musí zpracovat jen jednou.</summary>
    public double DuplicateRate { get; set; }

    /// <summary>Webhook se zdrží až za následující – e-shop nesmí vrátit stav zpět.</summary>
    public double ReorderRate { get; set; }

    /// <summary>Webhook se vůbec nepošle – e-shop se musí sám zeptat (GET).</summary>
    public double DropRate { get; set; }

    /// <summary>Pevné semínko náhody (zkoušky, které se mají opakovat stejně). Prázdné = náhodně.</summary>
    public int? Seed { get; set; }

    public void Validate()
    {
        foreach (var (name, value) in new[]
        {
            (nameof(FailureRate), FailureRate), (nameof(LostResponseRate), LostResponseRate),
            (nameof(DuplicateRate), DuplicateRate), (nameof(ReorderRate), ReorderRate), (nameof(DropRate), DropRate),
            (nameof(RejectRate), RejectRate)
        })
        {
            if (value is < 0 or > 1)
            {
                throw new InvalidOperationException($"Carrier:Chaos:{name} musí být mezi 0 a 1.");
            }
        }
    }
}
