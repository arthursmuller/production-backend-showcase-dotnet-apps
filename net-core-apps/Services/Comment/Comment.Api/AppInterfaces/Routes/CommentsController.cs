using Comment.Application.Dtos;
using Comment.Application.Features.Comments.Commands;
using Comment.Domain.AggregatesModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using CommomStartupExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Routes
{
    /// <summary>
    /// Canonical multipart Comment contract with explicit status and error metadata.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/comments")]
    public sealed class CommentsController : ControllerBase
    {
        private readonly AddCommentCommand _add;
        private readonly UpdateCommentCommand _update;

        public CommentsController(AddCommentCommand add, UpdateCommentCommand update)
        {
            _add = add;
            _update = update;
        }

        [HttpPost]
        [EnableRateLimiting(TrafficProtectionPolicies.AuthenticatedWrite)]
        [FormDataEndpoint<CommentWriteResultDto, MaxMultipartBodyBytes>(
            SuccessStatusCode = StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
        [ProducesResponseType(
            typeof(ProblemDetails),
            StatusCodes.Status429TooManyRequests,
            "application/problem+json")]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Add(
            [FromForm] AddCommentFormDto dto,
            [FromForm] List<MediaFileWrapperDto>? files,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            var errors = CommentMediaFormValidator.Validate(files);
            if (errors.Count > 0)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "comment-request-invalid",
                    detail: string.Join(" ", errors.Where(error =>
                        !string.IsNullOrWhiteSpace(error))));
            }

            var comment = await _add.Handle(
                dto.ToApplication(files, idempotencyKey),
                cancellationToken);
            return CreatedAtRoute(
                "GetCommentById",
                new { id = comment.Id },
                new Response<CommentWriteResultDto>(
                    CommentWriteResultDtoMapper.ToDto(comment)));
        }

        [HttpPatch]
        [EnableRateLimiting(TrafficProtectionPolicies.AuthenticatedWrite)]
        [FormDataEndpoint<CommentWriteResultDto, MaxMultipartBodyBytes>]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
        [ProducesResponseType(
            typeof(ProblemDetails),
            StatusCodes.Status429TooManyRequests,
            "application/problem+json")]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Update(
            [FromForm] UpdateCommentFormDto dto,
            [FromForm] List<MediaFileWrapperDto>? files,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            var errors = CommentMediaFormValidator.Validate(files);
            if (errors.Count > 0)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "comment-request-invalid",
                    detail: string.Join(" ", errors.Where(error =>
                        !string.IsNullOrWhiteSpace(error))));
            }

            var comment = await _update.Handle(
                dto.ToApplication(files, idempotencyKey),
                cancellationToken);
            return comment is null
                ? Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "comment-not-found",
                    detail: "The comment was not found.")
                : Ok(new Response<CommentWriteResultDto>(
                    CommentWriteResultDtoMapper.ToDto(comment)));
        }
    }
}
