using FluentValidation;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Enums;

namespace StreamingPanel.Core.Validation;

/// <summary>
/// Validates <see cref="RegisterRequest"/>: email present (5-254 chars) and well-formed,
/// password 8-128 chars, and a known <see cref="Role"/> name.
/// </summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MinimumLength(5).WithMessage("Email must be at least 5 characters.")
            .MaximumLength(254).WithMessage("Email must not exceed 254 characters.")
            .Must(EmailRules.IsWellFormed).WithMessage("Email format is invalid.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(RoleRules.IsKnownRole).WithMessage("Role is invalid.");
    }
}
