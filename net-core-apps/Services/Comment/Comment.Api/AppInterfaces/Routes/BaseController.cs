using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StarterEntities.Storage;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Comment.API.AppInterfaces.Routes
{
    public class BaseController : Controller
    {
        protected IActionResult OkRes<T>(T objRetorno) => Ok(new Response<T>(objRetorno));
        protected async Task<IActionResult> OkRes(Func<Task> action)
        {
            await action();
            return Ok();
        }
        protected IActionResult OkRes<T>((IEnumerable<T> objRetorno, int totalRecords) tup, CommomFilter criteria) =>
            Ok(PaginationFactory.CreatePagedReponse(
                tup.objRetorno,
                criteria.PageNumber ?? 1,
                criteria.PageSize ?? 10,
                tup.totalRecords));
        protected IActionResult CreatedRes<T>(T objRetorno) => CreatedAtAction(nameof(objRetorno), new Response<T>(objRetorno));

        public async Task<IActionResult> OkResWithMultipleMedia<TDto, TReturnType>(
             Action<TDto, IEnumerable<IFormFile>> fileProcessor,
             Func<TDto, Task<TReturnType>> taskParam,
             IEnumerable<MediaFileWrapperDto> files,
             TDto dto)
        {
            ValidateMediaFiles(files);
            fileProcessor(dto, files.Select(fw => fw.File));
            return OkRes(await taskParam(dto));
        }

        private void ValidateMediaFiles(IEnumerable<MediaFileWrapperDto> mediaFiles)
        {
            if (mediaFiles == null) return;
            foreach (var mediaFile in mediaFiles)
            {
                if (mediaFile.File == null) continue;
                var contentType = mediaFile.File.ContentType.ToLower();
                var fileSize = mediaFile.File.Length;
                if (mediaFile.MediaType == MediaType.Image)
                {
                    if (!contentType.StartsWith("image/"))
                        throw new InvalidOperationException($"Invalid image file: {mediaFile.File.FileName}");
                    if (fileSize > 10 * 1024 * 1024)
                        throw new InvalidOperationException($"Image file too large: {mediaFile.File.FileName}");
                }
                else if (mediaFile.MediaType == MediaType.Video)
                {
                    if (!contentType.StartsWith("video/"))
                        throw new InvalidOperationException($"Invalid video file: {mediaFile.File.FileName}");
                    if (fileSize > 100 * 1024 * 1024)
                        throw new InvalidOperationException($"Video file too large: {mediaFile.File.FileName}");
                }
            }
        }
    }
}
