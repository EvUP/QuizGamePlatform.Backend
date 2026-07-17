namespace QuizGamePlatform.Backend.DataAccess.Entities
{
    public class MatchQuestionEntity
    {
        public Guid Id { get; set; }

        public Guid MatchId { get; set; }
        public MatchEntity Match { get; set; } = null!;

        public Guid QuestionId { get; set; }
        public QuestionEntity Question { get; set; } = null!;

        public int Order { get; set; }

        public ICollection<PlayerAnswerEntity> Answers { get; set; } = new List<PlayerAnswerEntity>();
    }
}
