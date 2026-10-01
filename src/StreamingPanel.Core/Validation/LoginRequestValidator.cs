using FluentValidation;
using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Core.Validation;

/// <summary>
/// Validates <see cref="LoginRequest"/>: email and password both present and ≤256 chars.
/// Login deliberately skips the stricter registration format rules; it only guards
/// against absent or oversized credentials.
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(256).WithMessage("Password must not exceed 256 characters.");
    }
}
