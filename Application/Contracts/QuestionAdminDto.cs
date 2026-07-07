namespace testBdControllers.Application.Contracts
{
    // Полный вид вопроса с пометкой правильного ответа (IsCorrect).

    public class QuestionAdminDto
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? Source { get; set; }
        public List<AnswerOptionAdminDto> Options { get; set; } = new();
    }

    public class AnswerOptionAdminDto
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public int Position { get; set; }
        public bool IsCorrect { get; set; }
    }
}
