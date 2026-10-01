using FluentValidation;
using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Core.Validation;

/// <summary>
/// Validates <see cref="MovieWriteDto"/>: title required (1-200 chars), optional studio
/// (≤200), release year in [1888, 2100], and 0-50 main actors each 1-200 chars.
/// </summary>
public sealed class MovieWriteValidator : AbstractValidator<MovieWriteDto>
{
    public MovieWriteValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.Studio)
            .MaximumLength(200).WithMessage("Studio must not exceed 200 characters.");

        RuleFor(x => x.ReleaseYear)
            .InclusiveBetween(1888, 2100)
            .WithMessage("ReleaseYear must be between 1888 and 2100.");

        RuleFor(x => x.MainActors)
            .NotNull().WithMessage("MainActors is required.")
            .Must(actors => actors is not null && actors.Count <= 50)
            .WithMessage("MainActors must contain at most 50 entries.");

        RuleForEach(x => x.MainActors)
            .NotEmpty().WithMessage("Each actor name must be between 1 and 200 characters.")
            .MaximumLength(200).WithMessage("Each actor name must be between 1 and 200 characters.");
    }
}
