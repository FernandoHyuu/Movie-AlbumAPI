using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Interfaces;

namespace StreamingPanel.Api.Controllers;

/// <summary>
/// Anonymous authentication endpoints: register, log in, and rotate a refresh token.
/// Each action binds the request, runs its validator, calls <see cref="IAuthService"/>,
/// and maps the resulting <see cref="Result{T}"/> to an HTTP status.
/// </summary>
[AllowAnonymous]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;

    public AuthController(
        IAuthService authService,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator)
    {
        _authService = authService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
    }

    /// <summary>Registers a new Person and issues a token pair (201); duplicate email returns 409.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        await ValidateAsync(_registerValidator, request);

        // On success we return 201 with the body directly rather than through ToActionResult,
        // which would emit a 200.
        var result = await _authService.RegisterAsync(request);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ToActionResult(result);
    }

    /// <summary>Authenticates by email and password, issuing a token pair (200); mismatch returns 401.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        await ValidateAsync(_loginValidator, request);

        var result = await _authService.LoginAsync(request);
        return ToActionResult(result);
    }

    /// <summary>Rotates the presented refresh token into a fresh token pair (200); invalid returns 401.</summary>
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshRequest request)
    {
        var result = await _authService.RefreshAsync(request.RefreshToken);
        return ToActionResult(result);
    }
}
