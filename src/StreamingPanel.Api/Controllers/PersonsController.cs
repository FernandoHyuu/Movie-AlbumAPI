using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamingPanel.Api.Extensions;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Interfaces;

namespace StreamingPanel.Api.Controllers;

/// <summary>
/// Person administration endpoints, all guarded by the <c>AdminOnly</c> policy. Listing
/// returns a paged envelope (up to 50 per page); unknown ids return 404.
/// </summary>
[Authorize(Policy = AuthenticationSetup.AdminOnlyPolicy)]
[Route("api/persons")]
[Produces("application/json")]
public sealed class PersonsController : ApiControllerBase
{
    private readonly IPersonService _personService;
    private readonly IValidator<PersonWriteDto> _personValidator;

    public PersonsController(IPersonService personService, IValidator<PersonWriteDto> personValidator)
    {
        _personService = personService;
        _personValidator = personValidator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PersonDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await _personService.GetPagedAsync(page, pageSize);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}", Name = "GetPersonById")]
    [ProducesResponseType(typeof(PersonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _personService.GetByIdAsync(id);
        return ToActionResult(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(PersonDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] PersonWriteDto dto)
    {
        await ValidateAsync(_personValidator, dto);

        var result = await _personService.CreateAsync(dto);
        return ToCreatedResult(result, nameof(GetById), person => new { id = person.Id });
    }

    /// <summary>Updates a person, including its role; unknown id returns 404.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PersonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] PersonWriteDto dto)
    {
        await ValidateAsync(_personValidator, dto);

        var result = await _personService.UpdateAsync(id, dto);
        return ToActionResult(result);
    }

    /// <summary>Deletes a person and cascades its children; unknown id returns 404.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _personService.DeleteAsync(id);
        return ToActionResult(result);
    }
}
