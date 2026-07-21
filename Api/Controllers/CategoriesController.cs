using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Core.Extensions;

namespace QuizGamePlatform.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CategoriesController(IQuizContentService quizContentService) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<List<CategoryDto>>> GetCategories(CancellationToken ct)
        {
            var categories = await quizContentService.GetCategoriesAsync(ct);
            return Ok(categories);
        }

        [HttpGet("{categoryId}/questions")]
        public async Task<ActionResult<List<QuestionAdminDto>>> GetQuestions(Guid categoryId, CancellationToken ct)
        {
            var questions = await quizContentService.GetQuestionsByCategoryAsync(categoryId, ct);

            if (questions is null)
                return NotFound(new CommonErrorResponse(
                    message: $"Category {categoryId} not found",
                    method: HttpContext.GetMethodWithPath()));

            return Ok(questions);
        }
    }
}
