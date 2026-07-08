namespace QuizGamePlatform.Backend.DataAccess.Entities
{
    public class QuestionEntity
    {
        public Guid Id { get; set; }

        // Вопрос принадлежит теме
        public Guid CategoryId { get; set; }
        public CategoryEntity Category { get; set; } = null!;

        public string Text { get; set; } = string.Empty;
        public string? Source { get; set; }

        public ICollection<AnswerOptionEntity> AnswerOptions { get; set; } = new List<AnswerOptionEntity>();
    }
}
