using System.Security.Cryptography;

namespace MiniShop.Web.Notifications;

/// <summary>
/// E-shop krok 23: vytvoří nový pár klíčů VAPID pro oznámení push – spouští se místo webové aplikace, nic jiného nedělá
/// (nepotřebuje databázi ani konfiguraci):
/// <code>dotnet run --project src/MiniShop.Web -- generate-vapid-keys</code>
/// Veřejný klíč patří do <c>deploy/production.env</c> (<c>PUSH_VAPID_PUBLIC_KEY</c>), soukromý jen do GitHub secretu
/// <c>PUSH_VAPID_PRIVATE_KEY</c> (docs/secrets.md) – nikdy do gitu ani do chatu.
/// Výměna klíčů = všechna zařízení si musí oznámení zapnout znovu (přihlášení je vázané na veřejný klíč).
/// </summary>
public static class GenerateVapidKeysCommand
{
    public const string Name = "generate-vapid-keys";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    public static int Run()
    {
        var (publicKey, privateKey) = Create();
        Console.WriteLine("Nový pár klíčů VAPID (oznámení push):");
        Console.WriteLine();
        Console.WriteLine($"PUSH_VAPID_PUBLIC_KEY={publicKey}");
        Console.WriteLine($"PUSH_VAPID_PRIVATE_KEY={privateKey}");
        Console.WriteLine();
        Console.WriteLine("Veřejný klíč → deploy/production.env. Soukromý klíč → GitHub → Environments → production → secret");
        Console.WriteLine("PUSH_VAPID_PRIVATE_KEY. Soukromý klíč nikam jinam nekopírujte (git, chat, e-mail).");
        return 0;
    }

    /// <summary>Klíče P-256 v base64url: veřejný = nekomprimovaný bod (0x04 | X | Y, 65 bajtů), soukromý = D (32 bajtů).</summary>
    public static (string PublicKey, string PrivateKey) Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        var point = new byte[65];
        point[0] = 0x04;
        parameters.Q.X!.CopyTo(point, 1);
        parameters.Q.Y!.CopyTo(point, 33);
        return (Base64Url(point), Base64Url(parameters.D!));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
