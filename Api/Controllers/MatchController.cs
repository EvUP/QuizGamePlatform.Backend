using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Application.Contracts.Match;
using QuizGamePlatform.Backend.Core.Extensions;

namespace QuizGamePlatform.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MatchController(IMatchService matchService) : ControllerBase
    {
        [HttpPost("start")]
        public async Task<IActionResult> StartMatch([FromBody] StartMatchRequest request, CancellationToken ct)
        {
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
        public async Task<IActionResult> GetCurrentQuestion(Guid matchId, CancellationToken ct)
        {
            var question = await matchService.GetCurrentQuestionAsync(matchId, ct);

            if (question is null)
            {
                return NotFound(new CommonErrorResponse(
                    message: $"Матч {matchId} не найден",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(question);
        }

        [HttpPost("{matchId}/answer")]
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
        public async Task<IActionResult> CloseQuestion(Guid matchId, CancellationToken ct)
        {
            var match = await matchService.CloseQuestionAsync(matchId, ct);

            if (match is null)
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Нельзя закрыть вопрос",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(match);
        }

        [HttpPost("{matchId}/next")]
        public async Task<IActionResult> NextQuestion(Guid matchId, CancellationToken ct)
        {
            var match = await matchService.NextQuestionAsync(matchId, ct);

            if (match is null)
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Нельзя перейти к следующему вопросу",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(match);
        }
    }
}
