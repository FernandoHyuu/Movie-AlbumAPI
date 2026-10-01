using FluentValidation;
using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Core.Validation;

/// <summary>
/// Validates <see cref="PersonWriteDto"/> for create and update: a known role, every
/// string field capped at 500 chars, and at most 10 addresses and 10 phones.
/// </summary>
public sealed class PersonUpdateValidator : AbstractValidator<PersonWriteDto>
{
    private const int MaxStringLength = 500;
    private const int MaxChildren = 10;

    public PersonUpdateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(MaxStringLength).WithMessage("Name must not exceed 500 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(MaxStringLength).WithMessage("Email must not exceed 500 characters.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(RoleRules.IsKnownRole).WithMessage("Role is invalid.");

        RuleFor(x => x.Password)
            .MaximumLength(MaxStringLength).WithMessage("Password must not exceed 500 characters.");

        RuleFor(x => x.Addresses)
            .NotNull().WithMessage("Addresses is required.")
            .Must(a => a is not null && a.Count <= MaxChildren)
            .WithMessage("A Person may have at most 10 addresses.");

        RuleFor(x => x.Phones)
            .NotNull().WithMessage("Phones is required.")
            .Must(p => p is not null && p.Count <= MaxChildren)
            .WithMessage("A Person may have at most 10 phones.");

        RuleForEach(x => x.Addresses).SetValidator(new AddressDtoValidator());
        RuleForEach(x => x.Phones).SetValidator(new PhoneDtoValidator());
    }
}

/// <summary>Validates an <see cref="AddressDto"/>: each field capped at 500 chars.</summary>
public sealed class AddressDtoValidator : AbstractValidator<AddressDto>
{
    private const int MaxStringLength = 500;

    public AddressDtoValidator()
    {
        RuleFor(x => x.Street).MaximumLength(MaxStringLength).WithMessage("Street must not exceed 500 characters.");
        RuleFor(x => x.City).MaximumLength(MaxStringLength).WithMessage("City must not exceed 500 characters.");
        RuleFor(x => x.State).MaximumLength(MaxStringLength).WithMessage("State must not exceed 500 characters.");
        RuleFor(x => x.ZipCode).MaximumLength(MaxStringLength).WithMessage("ZipCode must not exceed 500 characters.");
    }
}

/// <summary>Validates a <see cref="PhoneDto"/>: each field capped at 500 chars.</summary>
public sealed class PhoneDtoValidator : AbstractValidator<PhoneDto>
{
    private const int MaxStringLength = 500;

    public PhoneDtoValidator()
    {
        RuleFor(x => x.Number).MaximumLength(MaxStringLength).WithMessage("Number must not exceed 500 characters.");
        RuleFor(x => x.Type).MaximumLength(MaxStringLength).WithMessage("Type must not exceed 500 characters.");
    }
}
