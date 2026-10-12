using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Auth;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Application.Contracts.Match;
using QuizGamePlatform.Backend.Core.Extensions;
using QuizGamePlatform.Backend.Core.Helpers;

namespace QuizGamePlatform.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MatchController(IMatchService matchService) : ControllerBase
    {
        [HttpPost("start")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> StartMatch([FromBody] StartMatchRequest request, CancellationToken ct)
        {
            if (request.QuestionCount < 1 || request.QuestionCount > MatchHelper.MaxQuestionCount)
            {
                return BadRequest(new CommonErrorResponse(
                    message: $"Количество вопросов от 1 до {MatchHelper.MaxQuestionCount}",
                    method: HttpContext.GetMethodWithPath()));
            }

            if (!await matchService.IsRoomHostAsync(request.RoomId, ct))
            {
                return NotHost();
            }

            var match = await matchService.StartMatchAsync(request, ct);

            if (match is null)
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Не удалось начать матч.",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(match);
        }

        [HttpGet("{matchId}/current-question")]
        [Authorize]
        public async Task<IActionResult> GetCurrentQuestion(Guid matchId, CancellationToken ct)
        {
            var question = await matchService.GetCurrentQuestionAsync(matchId, ct);

            if (question is null)
            {
                return NotFound(new CommonErrorResponse(
                    message: $"Матч {matchId} не найден или вы в нём не участвуете",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(question);
        }

        [HttpPost("{matchId}/answer")]
        [Authorize]
        public async Task<IActionResult> SubmitAnswer(Guid matchId, [FromBody] SubmitAnswerRequest request, CancellationToken ct)
        {
            var accepted = await matchService.SubmitAnswerAsync(matchId, request, ct);

            if (!accepted)
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Ответ не принят",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok();
        }

        [HttpPost("{matchId}/close-question")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> CloseQuestion(Guid matchId, CancellationToken ct)
        {
            if (!await matchService.IsMatchHostAsync(matchId, ct))
            {
                return NotHost();
            }

            var match = await matchService.CloseQuestionAsync(matchId, ct);

            if (match is null)
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Закрыть вопрос досрочно можно, когда ответили все игроки",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(match);
        }

        [HttpPost("{matchId}/next")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> NextQuestion(Guid matchId, CancellationToken ct)
        {
            if (!await matchService.IsMatchHostAsync(matchId, ct))
            {
                return NotHost();
            }

            var match = await matchService.NextQuestionAsync(matchId, ct);

            if (match is null)
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Нельзя перейти к следующему вопросу",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(match);
        }

        private ObjectResult NotHost() => StatusCode(StatusCodes.Status403Forbidden, new CommonErrorResponse(
            message: "Управлять матчем может только владелец комнаты",
            method: HttpContext.GetMethodWithPath()));
    }
}
