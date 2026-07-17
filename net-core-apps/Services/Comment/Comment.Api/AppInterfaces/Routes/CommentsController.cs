using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comment.Application.Dtos;
using Comment.Application.Features.Comments.Commands;
using Comment.Domain.AggregatesModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StarterEntities;
using StarterEntities.Storage;

namespace Comment.API.AppInterfaces.Routes
{
    /// <summary>
    /// Hosts the Comment endpoints that consume <c>multipart/form-data</c> with complex
    /// <c>[FromForm]</c> DTOs. Minimal-API binders cannot deserialise these reliably
    /// (mixed file + nested object payloads), so they remain attribute-routed
    /// MVC actions. All other Comment endpoints live in <see cref="CommentsApiEndpoints"/>.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/comments/v1")]
    public class CommentsController : BaseController
    {
        private readonly AddCommentCommand _add;
        private readonly UpdateCommentCommand _update;

        public CommentsController(AddCommentCommand add, UpdateCommentCommand update)
        {
            _add = add;
            _update = update;
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        [ProducesResponseType(typeof(Response<CommentPost>), 200)]
        [ProducesResponseType(typeof(Response<string>), 500)]
        public async Task<IActionResult> Add([FromForm] AddCommentPostDto dto, [FromForm] List<MediaFileWrapperDto> files)
            => await OkResWithMultipleMedia(
                (val, a) => { val.MediaItems = files.ToList(); },
                _add.Handle,
                files,
                dto
            );

        [HttpPatch]
        [Consumes("multipart/form-data")]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
        [ProducesResponseType(typeof(Response<CommentPost>), 200)]
        [ProducesResponseType(typeof(Response<string>), 500)]
        public async Task<IActionResult> Update([FromForm] UpdateCommentDto dto, [FromForm] List<MediaFileWrapperDto> files)
            => await OkResWithMultipleMedia(
                (val, a) =>
                {
                    val.MediaItems ??= new CommomUpdateDomainDto<MediaFileWrapperDto, UpdateMediaItemDomainDto>();
                    val.MediaItems.ToAdd = files.ToList();
                },
                _update.Handle,
                files,
                dto
            );
    }
}
