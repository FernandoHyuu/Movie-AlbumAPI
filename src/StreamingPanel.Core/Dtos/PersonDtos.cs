namespace StreamingPanel.Core.Dtos;

/// <summary>An address within a Person payload.</summary>
public record AddressDto(string Street, string City, string State, string ZipCode);

/// <summary>A phone within a Person payload.</summary>
public record PhoneDto(string Number, string Type);

/// <summary>
/// Payload for creating or updating a Person. <see cref="Role"/> is the string name of a
/// <see cref="Enums.Role"/>. A Person may carry up to 10 addresses and 10 phones.
/// Password is optional on update; when present it is hashed before storage.
/// </summary>
public record PersonWriteDto(
    string Name,
    string Email,
    string Role,
    string? Password,
    IReadOnlyList<AddressDto> Addresses,
    IReadOnlyList<PhoneDto> Phones);

/// <summary>
/// Person as returned by read and list endpoints. The password hash is never exposed.
/// </summary>
public record PersonDto(
    Guid Id,
    string Name,
    string Email,
    string Role,
    DateTime CreatedAt,
    IReadOnlyList<AddressDto> Addresses,
    IReadOnlyList<PhoneDto> Phones);
