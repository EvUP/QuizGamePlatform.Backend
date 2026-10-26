using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Auth;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Application.Contracts.Match;
using QuizGamePlatform.Backend.Application.Enums;
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
                    message: $"Вопросов может быть от 1 до {MatchHelper.MaxQuestionCount}",
                    method: HttpContext.GetMethodWithPath()));
            }

            if (request.CategoryIds is null || request.CategoryIds.Count == 0)
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Выберите хотя бы одну тему",
                    method: HttpContext.GetMethodWithPath()));
            }

            if (!await matchService.IsRoomHostAsync(request.RoomId, ct))
            {
                return NotHost();
            }

            var (match, refusal) = await matchService.StartMatchAsync(request, ct);

            if (match is not null)
            {
                return Ok(match);
            }

            var method = HttpContext.GetMethodWithPath();

            return refusal switch
            {
                // одновременно вошли, пересели или уже стартовали
                StartRefusal.Concurrent => Conflict(new CommonErrorResponse(
                    message: "Кто-то успел изменить комнату раньше вас. Попробуйте ещё раз",
                    method: method)),
                StartRefusal.Started => Conflict(new CommonErrorResponse(
                    message: "Игра в этой комнате уже идёт",
                    method: method)),
                StartRefusal.Finished => Conflict(new CommonErrorResponse(
                    message: "Эта игра уже закончилась. Чтобы сыграть снова, нажмите «Сыграть ещё» на экране итогов",
                    method: method)),
                StartRefusal.TooFewPlayers => BadRequest(new CommonErrorResponse(
                    message: "Чтобы начать, нужно хотя бы 2 игрока",
                    method: method)),
                StartRefusal.TooFewTeams => BadRequest(new CommonErrorResponse(
                    message: $"Чтобы начать, нужно хотя бы {MatchHelper.MinTeams} команды с игроками",
                    method: method)),
                StartRefusal.NoQuestions => BadRequest(new CommonErrorResponse(
                    message: "В выбранных темах пока нет вопросов. Выберите другие темы",
                    method: method)),
                _ => NotFound(new CommonErrorResponse(
                    message: "Комната не найдена",
                    method: method)),
            };
        }

        [HttpGet("{matchId}/current-question")]
        [Authorize]
        public async Task<IActionResult> GetCurrentQuestion(Guid matchId, CancellationToken ct)
        {
            var question = await matchService.GetCurrentQuestionAsync(matchId, ct);

            if (question is null)
            {
                return NotFound(new CommonErrorResponse(
                    message: "Игра не найдена или вы в ней не играете",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(question);
        }

        // разбор вопросов на экране итогов
        [HttpGet("{matchId}/review")]
        [Authorize]
        public async Task<IActionResult> GetReview(Guid matchId, CancellationToken ct)
        {
            var (review, notFinished) = await matchService.GetReviewAsync(matchId, ct);

            if (notFinished)
            {
                return Conflict(new CommonErrorResponse(
                    message: "Разбор откроется после финала",
                    method: HttpContext.GetMethodWithPath()));
            }

            if (review is null)
            {
                return NotFound(new CommonErrorResponse(
                    message: "Игра не найдена или вы в ней не играли",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(review);
        }

        [HttpPost("{matchId}/answer")]
        [Authorize]
        public async Task<IActionResult> SubmitAnswer(Guid matchId, [FromBody] SubmitAnswerRequest request, CancellationToken ct)
        {
            var accepted = await matchService.SubmitAnswerAsync(matchId, request, ct);

            if (!accepted)
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Ответ не принят: время вышло или вы уже ответили",
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
                    message: "Вопрос уже закрыт",
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
            message: "Управлять игрой может только ведущий",
            method: HttpContext.GetMethodWithPath()));
    }
}
