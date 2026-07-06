namespace testBdControllers.DataAccess.Entities
{
    public class AnswerOptionEntity
    {
        public Guid Id { get; set; }

        public Guid QuestionId { get; set; }
        public QuestionEntity Question { get; set; } = null!;

        public string Text { get; set; } = string.Empty;

        // Позиция варианта (1, 2, 3, 4)
        public int Position { get; set; }
        public bool IsCorrect { get; set; }
    }
}