using Microsoft.AspNetCore.Identity;

namespace ffw.Services;

/// <summary>Deutsche Fehlermeldungen für Identity (Passwortregeln, doppelte E-Mail usw.).</summary>
public sealed class DeutscheIdentityFehler : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => new() { Code = nameof(DefaultError), Description = "Ein unbekannter Fehler ist aufgetreten." };

    public override IdentityError ConcurrencyFailure() => new() { Code = nameof(ConcurrencyFailure), Description = "Der Datensatz wurde zwischenzeitlich geändert. Bitte erneut versuchen." };

    public override IdentityError PasswordMismatch() => new() { Code = nameof(PasswordMismatch), Description = "Das aktuelle Passwort ist falsch." };

    public override IdentityError InvalidUserName(string? userName) => new() { Code = nameof(InvalidUserName), Description = "Die E-Mail-Adresse enthält ungültige Zeichen." };

    public override IdentityError InvalidEmail(string? email) => new() { Code = nameof(InvalidEmail), Description = "Die E-Mail-Adresse ist ungültig." };

    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = "Diese E-Mail-Adresse ist bereits vergeben." };

    public override IdentityError DuplicateEmail(string email) => new() { Code = nameof(DuplicateEmail), Description = "Diese E-Mail-Adresse ist bereits vergeben." };

    public override IdentityError UserAlreadyInRole(string role) => new() { Code = nameof(UserAlreadyInRole), Description = $"Der Benutzer gehört bereits zur Gruppe „{role}“." };

    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = $"Das Passwort muss mindestens {length} Zeichen lang sein." };

    public override IdentityError PasswordRequiresNonAlphanumeric() => new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "Das Passwort braucht mindestens ein Sonderzeichen." };

    public override IdentityError PasswordRequiresDigit() => new() { Code = nameof(PasswordRequiresDigit), Description = "Das Passwort braucht mindestens eine Ziffer." };

    public override IdentityError PasswordRequiresLower() => new() { Code = nameof(PasswordRequiresLower), Description = "Das Passwort braucht mindestens einen Kleinbuchstaben." };

    public override IdentityError PasswordRequiresUpper() => new() { Code = nameof(PasswordRequiresUpper), Description = "Das Passwort braucht mindestens einen Großbuchstaben." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"Das Passwort braucht mindestens {uniqueChars} verschiedene Zeichen." };
}
