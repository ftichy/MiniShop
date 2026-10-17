namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>Nastavení RabbitMQ (sekce Messaging:RabbitMq v appsettings.json).</summary>
public sealed class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    /// <summary>Fronta ukázkového consumeru, navázaná na všechny integrační události.</summary>
    public string AuditQueue { get; set; } = "minishop.audit";
}
