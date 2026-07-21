namespace QuizGamePlatform.Backend.Application.Contracts
{

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
    }
}
