using Microsoft.AspNetCore.Identity;

namespace ControlGastos.Web.Infrastructure;

/// <summary>Mensajes de Identity en español (los que se pueden mostrar en registro y cambio de contraseña).</summary>
public class ErroresIdentityEnEspanol : IdentityErrorDescriber
{
    private static IdentityError E(string code, string msg) => new() { Code = code, Description = msg };

    public override IdentityError DuplicateEmail(string email) => E(nameof(DuplicateEmail), "Ya existe una cuenta con ese correo.");
    public override IdentityError DuplicateUserName(string userName) => E(nameof(DuplicateUserName), "Ya existe una cuenta con ese correo.");
    public override IdentityError InvalidEmail(string? email) => E(nameof(InvalidEmail), "El correo no es válido.");
    public override IdentityError InvalidUserName(string? userName) => E(nameof(InvalidUserName), "El correo no es válido.");
    public override IdentityError PasswordTooShort(int length) => E(nameof(PasswordTooShort), $"La contraseña debe tener al menos {length} caracteres.");
    public override IdentityError PasswordRequiresDigit() => E(nameof(PasswordRequiresDigit), "La contraseña debe tener al menos un número.");
    public override IdentityError PasswordRequiresLower() => E(nameof(PasswordRequiresLower), "La contraseña debe tener al menos una letra minúscula.");
    public override IdentityError PasswordRequiresUpper() => E(nameof(PasswordRequiresUpper), "La contraseña debe tener al menos una letra mayúscula.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => E(nameof(PasswordRequiresNonAlphanumeric), "La contraseña debe tener al menos un símbolo.");
    public override IdentityError PasswordMismatch() => E(nameof(PasswordMismatch), "La contraseña actual no es correcta.");
    public override IdentityError DefaultError() => E(nameof(DefaultError), "No se pudo completar la operación.");
}
