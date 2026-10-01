using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamingPanel.Api.Extensions;
using StreamingPanel.Core.Dtos;
using StreamingPanel.Core.Interfaces;

namespace StreamingPanel.Api.Controllers;

/// <summary>
/// Album catalog endpoints. Reads are guarded by the <c>AlbumAccess</c> policy, writes
/// (including cover upload) by <c>AdminOnly</c>. Listing returns a paged envelope (up to
/// 50 per page); the cover GET streams the stored bytes with their <c>Content-Type</c>.
/// </summary>
[Route("api/albums")]
[Produces("application/json")]
public sealed class AlbumsController : ApiControllerBase
{
    private readonly IAlbumService _albumService;
    private readonly IValidator<AlbumWriteDto> _albumValidator;

    public AlbumsController(IAlbumService albumService, IValidator<AlbumWriteDto> albumValidator)
    {
        _albumService = albumService;
        _albumValidator = albumValidator;
    }

    [HttpGet]
    [Authorize(Policy = AuthenticationSetup.AlbumAccessPolicy)]
    [ProducesResponseType(typeof(PagedResult<AlbumDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await _albumService.GetPagedAsync(page, pageSize);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}", Name = "GetAlbumById")]
    [Authorize(Policy = AuthenticationSetup.AlbumAccessPolicy)]
    [ProducesResponseType(typeof(AlbumDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _albumService.GetByIdAsync(id);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = AuthenticationSetup.AdminOnlyPolicy)]
    [ProducesResponseType(typeof(AlbumDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] AlbumWriteDto dto)
    {
        await ValidateAsync(_albumValidator, dto);

        var result = await _albumService.CreateAsync(dto);
        return ToCreatedResult(result, nameof(GetById), album => new { id = album.Id });
    }

    /// <summary>Updates an album; unknown id returns 404.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.AdminOnlyPolicy)]
    [ProducesResponseType(typeof(AlbumDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] AlbumWriteDto dto)
    {
        await ValidateAsync(_albumValidator, dto);

        var result = await _albumService.UpdateAsync(id, dto);
        return ToActionResult(result);
    }

    /// <summary>Deletes an album; unknown id returns 404.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.AdminOnlyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _albumService.DeleteAsync(id);
        return ToActionResult(result);
    }

    /// <summary>Streams the stored cover bytes with their <c>Content-Type</c>; missing returns 404.</summary>
    [HttpGet("{id:guid}/cover")]
    [Authorize(Policy = AuthenticationSetup.AlbumAccessPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCover(Guid id)
    {
        var result = await _albumService.GetCoverAsync(id);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        var cover = result.Value;
        return File(cover.Content, cover.ContentType);
    }

    /// <summary>Stores (replaces) the cover from a multipart/form-data upload; missing record returns 404.</summary>
    [HttpPost("{id:guid}/cover")]
    [Authorize(Policy = AuthenticationSetup.AdminOnlyPolicy)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadCover(Guid id, IFormFile file)
    {
        // Reject an absent or empty upload here so we never store a zero-byte cover that would
        // later stream back as a broken image.
        if (file is null || file.Length == 0)
        {
            return ToActionResult(Result.Failure(ErrorCode.Validation, "A non-empty cover file is required."));
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer);
            bytes = buffer.ToArray();
        }

        var result = await _albumService.StoreCoverAsync(id, bytes, file.ContentType);
        return ToActionResult(result);
    }
}
