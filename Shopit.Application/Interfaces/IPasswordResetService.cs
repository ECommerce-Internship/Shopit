using Shopit.Application.DTOs.Auth;

namespace Shopit.Application.Interfaces;

public interface IPasswordResetService
{
    // Generates a short code, stores its hash with a short TTL, and emails it.
    // Always completes successfully even for an unknown email (no account enumeration).
    Task RequestPasswordResetAsync(ForgotPasswordRequest request);

    // Validates the code (matches + not expired + not used), sets the new password,
    // marks the token used, and revokes the user's existing refresh tokens.
    Task ResetPasswordAsync(ResetPasswordRequest request);
}
