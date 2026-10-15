using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Core.Extensions;

namespace QuizGamePlatform.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController(ICurrentUserContext currentUser) : ControllerBase
    {
        /// <summary>Текущий пользователь</summary>
        [HttpGet("me")]
        [Authorize]
        public ActionResult<CurrentUserResponse> GetCurrentUser()
        {
            if (currentUser.IsGuest)
            {
                if (currentUser.RoomPlayerLinkId is not { } roomPlayerLinkId)
                {
                    return Unauthorized(new CommonErrorResponse(
                        message: "Вход гостя устарел — откройте ссылку ещё раз",
                        method: HttpContext.GetMethodWithPath()));
                }

                return Ok(CurrentUserResponse.Guest(
                    roomPlayerLinkId,
                    currentUser.RoomId,
                    currentUser.Nickname));
            }

            var subject = currentUser.KeycloakSubject;
            if (subject is null)
            {
                return Unauthorized(new CommonErrorResponse(
                    message: "Вход устарел — войдите ещё раз",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(CurrentUserResponse.Registered(
                subject,
                currentUser.DisplayName,
                currentUser.Email));
        }
    }
}
