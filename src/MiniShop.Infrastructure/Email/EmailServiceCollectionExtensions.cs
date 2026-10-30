using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniShop.Application.Abstractions;

namespace MiniShop.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Účty krok 5: <see cref="IEmailSender"/> podle sekce "Email" – s nastaveným serverem SMTP, jinak jen log.
    /// </summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EmailSettings.SectionName);
        services.Configure<EmailSettings>(section);

        // Replace – AddInfrastructureCore už zaregistroval výchozí LoggingEmailSender.
        services.Replace(string.IsNullOrWhiteSpace(section[nameof(EmailSettings.Host)])
            ? ServiceDescriptor.Singleton<IEmailSender, LoggingEmailSender>()
            : ServiceDescriptor.Singleton<IEmailSender, SmtpEmailSender>());

        return services;
    }
}
