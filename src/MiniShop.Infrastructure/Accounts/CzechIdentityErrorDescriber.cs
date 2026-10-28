using Microsoft.AspNetCore.Identity;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// České chybové hlášky Identity (výchozí jsou anglicky). Přeložené jsou ty, které uživatel MiniShopu může vidět.
/// </summary>
/// <remarks>
/// Duplicitní jméno i e-mail mají stejnou hlášku – přihlašovací jméno je e-mail, Identity hlásí obojí
/// a stránka stejné hlášky sloučí.
/// </remarks>
public sealed class CzechIdentityErrorDescriber : IdentityErrorDescriber
{
    private const string DuplicateAccountMessage = "Účet s tímto e-mailem už existuje.";

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = DuplicateAccountMessage };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = DuplicateAccountMessage };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "E-mail obsahuje nepovolené znaky." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "Zadejte platný e-mail." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Heslo musí mít alespoň {length} znaků." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"Heslo musí obsahovat alespoň {uniqueChars} různých znaků." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "Nesprávné heslo." };

    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = "Něco se nepovedlo, zkuste to prosím znovu." };
}
