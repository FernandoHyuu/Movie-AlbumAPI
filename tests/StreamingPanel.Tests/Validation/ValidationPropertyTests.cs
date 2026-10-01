using CsCheck;
using FluentValidation.Results;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Enums;
using StreamingPanel.Core.Validation;

namespace StreamingPanel.Tests.Validation;

/// <summary>
/// Property-based tests for the FluentValidation validators (R1.4, R1.6, R5.3, R5.4, R5.7,
/// R5.8, R6.3, R6.4, R6.5, R7.5, R7.6, R10.1, R10.2, R10.3, R10.4).
///
/// These tests exercise the validators directly — no database is involved. "Accepts exactly
/// in-range" means: inputs inside the valid range produce <c>IsValid == true</c>, and inputs
/// outside the range produce <c>IsValid == false</c> with at least one error naming the
/// offending field. Each property runs a minimum of 100 iterations.
///
/// Feature: streaming-panel
/// Validates: Requirements 1.4, 1.6, 5.3, 5.4, 5.7, 5.8, 6.3, 6.4, 6.5, 7.5, 7.6, 10.1, 10.2, 10.3, 10.4
/// </summary>
public class ValidationPropertyTests
{
    private const int Iterations = 100;

    private static readonly RegisterRequestValidator RegisterValidator = new();
    private static readonly PersonUpdateValidator PersonValidator = new();
    private static readonly MovieWriteValidator MovieValidator = new();
    private static readonly AlbumWriteValidator AlbumValidator = new();

    private static readonly string[] KnownRoleNames = Enum.GetNames<Role>();

    // ---------------------------------------------------------------------
    // Shared generators
    // ---------------------------------------------------------------------

    /// <summary>A well-formed email ≥5 and ≤254 characters: exactly one '@', non-empty local,
    /// a dotted domain whose '.' is interior.</summary>
    private static readonly Gen<string> GenWellFormedEmail =
        from local in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
        from host in Gen.String[Gen.Char.AlphaNumeric, 1, 20]
        from tld in Gen.OneOfConst("com", "net", "org", "io", "dev")
        select $"{local}@{host}.{tld}";

    /// <summary>A valid password: 8–128 characters, non-whitespace.</summary>
    private static readonly Gen<string> GenValidPassword =
        Gen.String[Gen.Char.AlphaNumeric, 8, 128];

    private static readonly Gen<string> GenKnownRole = Gen.OneOfConst(KnownRoleNames);

    /// <summary>
    /// A role string that is NOT one of the four defined roles. Integer-formatted strings
    /// (e.g. "6") are included: <c>RoleRules.IsKnownRole</c> now requires the parsed value to be a
    /// DEFINED enum member, so numeric strings that map to no defined <c>Role</c> are correctly
    /// treated as unknown.
    /// </summary>
    private static readonly Gen<string> GenUnknownRole =
        Gen.String[Gen.Char.AlphaNumeric, 1, 15]
            .Where(s => !KnownRoleNames.Contains(s));

    private static readonly Gen<AddressDto> GenAddress =
        from street in Gen.String[Gen.Char.AlphaNumeric, 0, 30]
        from city in Gen.String[Gen.Char.AlphaNumeric, 0, 30]
        from state in Gen.String[Gen.Char.AlphaNumeric, 0, 30]
        from zip in Gen.String[Gen.Char.AlphaNumeric, 0, 30]
        select new AddressDto(street, city, state, zip);

    private static readonly Gen<PhoneDto> GenPhone =
        from number in Gen.String[Gen.Char.AlphaNumeric, 0, 30]
        from type in Gen.String[Gen.Char.AlphaNumeric, 0, 30]
        select new PhoneDto(number, type);

    private static bool NamesField(ValidationResult result, string field) =>
        result.Errors.Any(e => e.PropertyName.Contains(field, StringComparison.Ordinal));

    // =====================================================================
    // Property 4: Invalid registration input is rejected without persistence
    // Validates: Requirements 1.4, 1.6
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 4: Invalid registration input is rejected without persistence
    ///
    /// A registration request whose email is absent/oversized/malformed, whose password is absent
    /// or outside 8–128 characters, or whose role is not an allowed role is rejected, with an error
    /// naming each offending field. (Rejection by the validator is the "no Person is created" guard,
    /// since validation runs before any persistence.)
    ///
    /// Validates: Requirements 1.4, 1.6
    /// </summary>
    [Fact]
    public void Property04_InvalidRegistrationInput_IsRejected()
    {
        // Each generated request deliberately violates at least one rule in a chosen dimension.
        var gen =
            from dimension in Gen.Int[0, 2]
            from goodEmail in GenWellFormedEmail
            from goodPassword in GenValidPassword
            from goodRole in GenKnownRole
            // bad email: empty, too long (>254), or malformed
            from badEmailChoice in Gen.Int[0, 2]
            from longEmailLocal in Gen.String[Gen.Char.AlphaNumeric, 250, 300]
            from badRole in GenUnknownRole
            // bad password: empty, too short (<8), or too long (>128)
            from badPwdChoice in Gen.Int[0, 2]
            from shortPwd in Gen.String[Gen.Char.AlphaNumeric, 0, 7]
            from longPwd in Gen.String[Gen.Char.AlphaNumeric, 129, 160]
            select (dimension, goodEmail, goodPassword, goodRole,
                    badEmailChoice, longEmailLocal, badRole, badPwdChoice, shortPwd, longPwd);

        gen.Sample(t =>
        {
            string email = t.goodEmail;
            string password = t.goodPassword;
            string role = t.goodRole;
            string field;

            switch (t.dimension)
            {
                case 0: // bad email
                    email = t.badEmailChoice switch
                    {
                        0 => "",
                        1 => $"{t.longEmailLocal}@host.com", // exceeds 254
                        _ => "not-an-email",                 // malformed (no '@'/'.')
                    };
                    field = nameof(RegisterRequest.Email);
                    break;
                case 1: // bad password
                    password = t.badPwdChoice switch
                    {
                        0 => "",
                        1 => t.shortPwd,
                        _ => t.longPwd,
                    };
                    field = nameof(RegisterRequest.Password);
                    break;
                default: // bad role
                    role = t.badRole;
                    field = nameof(RegisterRequest.Role);
                    break;
            }

            var request = new RegisterRequest(email, password, role, "Test");
            var result = RegisterValidator.Validate(request);

            Assert.False(result.IsValid, $"Expected rejection for {field}='{(t.dimension == 0 ? email : t.dimension == 1 ? password : role)}'.");
            Assert.True(NamesField(result, field),
                $"Expected an error naming '{field}'. Errors: {string.Join(", ", result.Errors.Select(e => e.PropertyName))}");
        }, iter: Iterations);
    }

    /// <summary>
    /// Companion to Property 4: a fully in-range registration request is accepted.
    /// Validates: Requirements 1.4, 1.6
    /// </summary>
    [Fact]
    public void Property04_ValidRegistrationInput_IsAccepted()
    {
        var gen =
            from email in GenWellFormedEmail.Where(e => e.Length is >= 5 and <= 254)
            from password in GenValidPassword
            from role in GenKnownRole
            select new RegisterRequest(email, password, role, "Test");

        gen.Sample(request =>
        {
            var result = RegisterValidator.Validate(request);
            Assert.True(result.IsValid,
                $"Expected acceptance. Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
        }, iter: Iterations);
    }

    // =====================================================================
    // Property 12: Person child-entity cardinality is enforced
    // Validates: Requirements 5.3, 5.4
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 12: Person child-entity cardinality is enforced
    ///
    /// A Person write request is accepted (w.r.t. cardinality) only if it carries at most 10
    /// addresses and at most 10 phones; a request exceeding either limit is rejected, naming the
    /// offending collection.
    ///
    /// Validates: Requirements 5.3, 5.4
    /// </summary>
    [Fact]
    public void Property12_PersonChildCardinality_IsEnforced()
    {
        var gen =
            from email in GenWellFormedEmail
            from role in GenKnownRole
            from addressCount in Gen.Int[0, 20]
            from phoneCount in Gen.Int[0, 20]
            from addresses in GenAddress.List[addressCount, addressCount]
            from phones in GenPhone.List[phoneCount, phoneCount]
            select (email, role, addressCount, phoneCount, addresses, phones);

        gen.Sample(t =>
        {
            var dto = new PersonWriteDto(
                Name: "Test",
                Email: t.email,
                Role: t.role,
                Password: null,
                Addresses: t.addresses,
                Phones: t.phones);

            var result = PersonValidator.Validate(dto);

            bool withinLimits = t.addressCount <= 10 && t.phoneCount <= 10;
            Assert.Equal(withinLimits, result.IsValid);

            if (t.addressCount > 10)
            {
                Assert.True(NamesField(result, nameof(PersonWriteDto.Addresses)),
                    "Expected an error naming Addresses when over 10.");
            }
            if (t.phoneCount > 10)
            {
                Assert.True(NamesField(result, nameof(PersonWriteDto.Phones)),
                    "Expected an error naming Phones when over 10.");
            }
        }, iter: Iterations);
    }

    // =====================================================================
    // Property 14: Person update rejects invalid roles without change
    // Validates: Requirements 5.7, 5.8
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 14: Person update rejects invalid roles without change
    ///
    /// A Person update whose role is not one of the four defined roles is rejected, naming Role;
    /// an otherwise-valid update whose role IS one of the four is accepted. (Rejection by the
    /// validator is the "record unchanged" guard, since validation precedes persistence.)
    ///
    /// Validates: Requirements 5.7, 5.8
    /// </summary>
    [Fact]
    public void Property14_PersonUpdate_RejectsInvalidRoles()
    {
        var gen =
            from valid in Gen.Bool
            from email in GenWellFormedEmail
            from knownRole in GenKnownRole
            from unknownRole in GenUnknownRole
            select (valid, email, knownRole, unknownRole);

        gen.Sample(t =>
        {
            var role = t.valid ? t.knownRole : t.unknownRole;
            var dto = new PersonWriteDto(
                Name: "Test",
                Email: t.email,
                Role: role,
                Password: null,
                Addresses: Array.Empty<AddressDto>(),
                Phones: Array.Empty<PhoneDto>());

            var result = PersonValidator.Validate(dto);

            Assert.Equal(t.valid, result.IsValid);
            if (!t.valid)
            {
                Assert.True(NamesField(result, nameof(PersonWriteDto.Role)),
                    $"Expected an error naming Role for unknown role '{role}'.");
            }
        }, iter: Iterations);
    }

    // =====================================================================
    // Property 15: Movie write validation accepts exactly in-range inputs
    // Validates: Requirements 6.3, 6.4, 6.5
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 15: Movie write validation accepts exactly in-range inputs
    ///
    /// A movie write is accepted iff Title is 1–200 chars, Studio is 0–200 chars, ReleaseYear is in
    /// [1888, 2100], and MainActors has 0–50 items each 1–200 chars; otherwise it is rejected,
    /// naming each offending field.
    ///
    /// Validates: Requirements 6.3, 6.4, 6.5
    /// </summary>
    [Fact]
    public void Property15_MovieWrite_AcceptsExactlyInRange()
    {
        var gen =
            // Title: generate across empty / in-range / oversized by varying length bounds.
            from title in Gen.String[Gen.Char.AlphaNumeric, 0, 220]
            from studioNull in Gen.Bool
            from studio in Gen.String[Gen.Char.AlphaNumeric, 0, 220]
            from year in Gen.Int[1700, 2300]
            from actorCount in Gen.Int[0, 60]
            from actors in Gen.String[Gen.Char.AlphaNumeric, 0, 220].List[actorCount, actorCount]
            select (title, studioNull, studio, year, actors);

        gen.Sample(t =>
        {
            string? studio = t.studioNull ? null : t.studio;
            var dto = new MovieWriteDto(t.title, studio, t.year, t.actors.ToList());
            var result = MovieValidator.Validate(dto);

            bool titleOk = t.title.Length is >= 1 and <= 200;
            bool studioOk = studio is null || studio.Length <= 200;
            bool yearOk = t.year is >= 1888 and <= 2100;
            bool actorsOk = t.actors.Count <= 50 && t.actors.All(a => a.Length is >= 1 and <= 200);
            bool expectedValid = titleOk && studioOk && yearOk && actorsOk;

            Assert.Equal(expectedValid, result.IsValid);

            if (!titleOk) Assert.True(NamesField(result, nameof(MovieWriteDto.Title)), "Expected Title error.");
            if (!studioOk) Assert.True(NamesField(result, nameof(MovieWriteDto.Studio)), "Expected Studio error.");
            if (!yearOk) Assert.True(NamesField(result, nameof(MovieWriteDto.ReleaseYear)), "Expected ReleaseYear error.");
            if (!actorsOk) Assert.True(NamesField(result, nameof(MovieWriteDto.MainActors)), "Expected MainActors error.");
        }, iter: Iterations);
    }

    // =====================================================================
    // Property 16: Album write validation accepts exactly in-range inputs
    // Validates: Requirements 7.5, 7.6
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 16: Album write validation accepts exactly in-range inputs
    ///
    /// An album write is accepted iff Title is 1–200 chars and ReleaseYear is in [1888, 2100]
    /// (with optional Band/Genre ≤200 chars); otherwise rejected, naming each offending field.
    ///
    /// Validates: Requirements 7.5, 7.6
    /// </summary>
    [Fact]
    public void Property16_AlbumWrite_AcceptsExactlyInRange()
    {
        var gen =
            from title in Gen.String[Gen.Char.AlphaNumeric, 0, 220]
            from bandNull in Gen.Bool
            from band in Gen.String[Gen.Char.AlphaNumeric, 0, 220]
            from genreNull in Gen.Bool
            from genre in Gen.String[Gen.Char.AlphaNumeric, 0, 220]
            from year in Gen.Int[1700, 2300]
            select (title, bandNull, band, genreNull, genre, year);

        gen.Sample(t =>
        {
            string? band = t.bandNull ? null : t.band;
            string? genre = t.genreNull ? null : t.genre;
            var dto = new AlbumWriteDto(t.title, band, t.year, genre);
            var result = AlbumValidator.Validate(dto);

            bool titleOk = t.title.Length is >= 1 and <= 200;
            bool bandOk = band is null || band.Length <= 200;
            bool genreOk = genre is null || genre.Length <= 200;
            bool yearOk = t.year is >= 1888 and <= 2100;
            bool expectedValid = titleOk && bandOk && genreOk && yearOk;

            Assert.Equal(expectedValid, result.IsValid);

            if (!titleOk) Assert.True(NamesField(result, nameof(AlbumWriteDto.Title)), "Expected Title error.");
            if (!bandOk) Assert.True(NamesField(result, nameof(AlbumWriteDto.Band)), "Expected Band error.");
            if (!genreOk) Assert.True(NamesField(result, nameof(AlbumWriteDto.Genre)), "Expected Genre error.");
            if (!yearOk) Assert.True(NamesField(result, nameof(AlbumWriteDto.ReleaseYear)), "Expected ReleaseYear error.");
        }, iter: Iterations);
    }

    // =====================================================================
    // Property 23: Required and length validation rejects blank and oversized fields
    // Validates: Requirements 10.1, 10.4
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 23: Required and length validation rejects blank and oversized fields
    ///
    /// For a Person write DTO, a required field that is blank (empty/whitespace) or any string
    /// field exceeding its 500-char cap is rejected with an error naming that field.
    ///
    /// Validates: Requirements 10.1, 10.4
    /// </summary>
    [Fact]
    public void Property23_RequiredAndLength_RejectsBlankAndOversized()
    {
        var gen =
            from dimension in Gen.Int[0, 2]  // 0: blank name, 1: blank email, 2: oversized name
            from blank in Gen.OneOfConst("", "   ", "\t", "\n ")
            from oversized in Gen.String[Gen.Char.AlphaNumeric, 501, 600]
            from email in GenWellFormedEmail
            from role in GenKnownRole
            select (dimension, blank, oversized, email, role);

        gen.Sample(t =>
        {
            string name = "Valid Name";
            string email = t.email;
            string field;

            switch (t.dimension)
            {
                case 0:
                    name = t.blank;
                    field = nameof(PersonWriteDto.Name);
                    break;
                case 1:
                    email = t.blank;
                    field = nameof(PersonWriteDto.Email);
                    break;
                default:
                    name = t.oversized; // > 500 chars
                    field = nameof(PersonWriteDto.Name);
                    break;
            }

            var dto = new PersonWriteDto(
                Name: name,
                Email: email,
                Role: t.role,
                Password: null,
                Addresses: Array.Empty<AddressDto>(),
                Phones: Array.Empty<PhoneDto>());

            var result = PersonValidator.Validate(dto);

            Assert.False(result.IsValid, $"Expected rejection for {field}.");
            Assert.True(NamesField(result, field),
                $"Expected an error naming '{field}'. Errors: {string.Join(", ", result.Errors.Select(e => e.PropertyName))}");
        }, iter: Iterations);
    }

    // =====================================================================
    // Property 24: Email validation accepts exactly well-formed addresses
    // Validates: Requirements 10.2
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 24: Email validation accepts exactly well-formed addresses
    ///
    /// Via the RegisterRequest validator, an email is accepted iff it has exactly one '@' with a
    /// non-empty local part and a dotted domain (interior '.'), and length 5–254. Malformed emails
    /// are rejected, naming Email.
    ///
    /// Validates: Requirements 10.2
    /// </summary>
    [Fact]
    public void Property24_EmailValidation_AcceptsExactlyWellFormed()
    {
        var gen =
            from wellFormed in Gen.Bool
            from good in GenWellFormedEmail.Where(e => e.Length is >= 5 and <= 254)
            // Each of these violates the spec's well-formed definition (R10.2): exactly one '@'
            // with a non-empty local part and a domain whose '.' is interior. Note the spec's
            // definition is deliberately permissive — it does NOT forbid interior spaces — so a
            // string like "spaces in@email.com" is "well-formed" by this rule and is not listed here.
            from malformed in Gen.OneOfConst(
                "plainaddress",
                "no-at-sign.com",
                "@missinglocal.com",
                "missingdomain@",
                "two@@at.com",
                "nodot@domain",
                "trailingdot@domain.",
                ".leadingdot@nodot")
            from password in GenValidPassword
            from role in GenKnownRole
            select (wellFormed, good, malformed, password, role);

        gen.Sample(t =>
        {
            var email = t.wellFormed ? t.good : t.malformed;
            var request = new RegisterRequest(email, t.password, t.role, "Test");
            var result = RegisterValidator.Validate(request);

            Assert.Equal(t.wellFormed, result.IsValid);
            if (!t.wellFormed)
            {
                Assert.True(NamesField(result, nameof(RegisterRequest.Email)),
                    $"Expected an Email error for malformed address '{email}'.");
            }
        }, iter: Iterations);
    }

    // =====================================================================
    // Property 25: ReleaseYear validation accepts exactly the allowed range
    // Validates: Requirements 10.3
    // =====================================================================

    /// <summary>
    /// Feature: streaming-panel, Property 25: ReleaseYear validation accepts exactly the allowed range
    ///
    /// A ReleaseYear is accepted iff it is an integer in [1888, 2100]; otherwise rejected, naming
    /// ReleaseYear. Checked through both the Movie and Album write validators (with all other
    /// fields held valid).
    ///
    /// Validates: Requirements 10.3
    /// </summary>
    [Fact]
    public void Property25_ReleaseYear_AcceptsExactlyAllowedRange()
    {
        var gen =
            from year in Gen.Int[1500, 2500]
            from title in Gen.String[Gen.Char.AlphaNumeric, 1, 100]
            select (year, title);

        gen.Sample(t =>
        {
            bool yearOk = t.year is >= 1888 and <= 2100;

            var movie = new MovieWriteDto(t.title, null, t.year, new List<string>());
            var movieResult = MovieValidator.Validate(movie);
            Assert.Equal(yearOk, movieResult.IsValid);
            if (!yearOk)
            {
                Assert.True(NamesField(movieResult, nameof(MovieWriteDto.ReleaseYear)),
                    $"Expected a ReleaseYear error on Movie for year {t.year}.");
            }

            var album = new AlbumWriteDto(t.title, null, t.year, null);
            var albumResult = AlbumValidator.Validate(album);
            Assert.Equal(yearOk, albumResult.IsValid);
            if (!yearOk)
            {
                Assert.True(NamesField(albumResult, nameof(AlbumWriteDto.ReleaseYear)),
                    $"Expected a ReleaseYear error on Album for year {t.year}.");
            }
        }, iter: Iterations);
    }
}
