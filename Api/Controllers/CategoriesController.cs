using Microsoft.AspNetCore.Mvc;
using testBdControllers.Application.Abstractions;
using testBdControllers.Application.Contracts;

namespace testBdControllers.Api.Controllers
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

        // TODO в дальнейшем исправьить или убрать вообще поле IsCorrect чтобы оно не передавалось в игровое API, а только в админское. Сейчас оно передается везде.
        [HttpGet("{categoryId}/questions")]
        public async Task<ActionResult<List<QuestionAdminDto>>> GetQuestions(Guid categoryId, CancellationToken ct)
        {
            var questions = await quizContentService.GetQuestionsByCategoryAsync(categoryId, ct);

            if (questions is null)
                return NotFound(new { message = $"Category {categoryId} not found." });

            return Ok(questions);
        }
    }
}
