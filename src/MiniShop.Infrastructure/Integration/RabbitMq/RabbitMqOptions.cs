namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>Nastavení RabbitMQ (sekce Messaging:RabbitMq v appsettings.json).</summary>
public sealed class RabbitMqOptions
{
    /// <summary>
    /// Krok 10B: celá adresa brokeru (amqp://uzivatel:heslo@host:port). Pokud je vyplněná, má přednost
    /// před HostName/Port/UserName/Password. Pod Aspire ji služba dostane jako ConnectionStrings:messaging.
    /// </summary>
    public string? ConnectionString { get; set; }

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>Jméno spojení v management UI – podle něj poznáte, která služba je připojená.</summary>
    public string ClientName { get; set; } = "MiniShop";

    /// <summary>
    /// Fronta ukázkového consumeru, navázaná na všechny integrační události.
    /// Prázdná hodnota = služba auditní frontu neposlouchá (krok 10B: poslouchá jen Web).
    /// </summary>
    public string? AuditQueue { get; set; } = "minishop.audit";
}
