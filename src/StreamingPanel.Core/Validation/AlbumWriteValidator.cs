using FluentValidation;
using StreamingPanel.Core.Dtos;

namespace StreamingPanel.Core.Validation;

/// <summary>
/// Validates <see cref="AlbumWriteDto"/>: title required (1-200 chars), release year in
/// [1888, 2100], and optional band/genre each ≤200 chars.
/// </summary>
public sealed class AlbumWriteValidator : AbstractValidator<AlbumWriteDto>
{
    public AlbumWriteValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.ReleaseYear)
            .InclusiveBetween(1888, 2100)
            .WithMessage("ReleaseYear must be between 1888 and 2100.");

        RuleFor(x => x.Genre)
            .MaximumLength(200).WithMessage("Genre must not exceed 200 characters.");

        RuleFor(x => x.Band)
            .MaximumLength(200).WithMessage("Band must not exceed 200 characters.");
    }
}
