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

        // когда вопрос показали игрокам, от него считаем время ответа; у старых матчей null
        public DateTime? OpenedAt { get; set; }

        public DateTime? ClosedAt { get; set; }

        // кто играл на вопросе: по ним очки команды
        public List<Guid> InGamePlayerIds { get; set; } = [];

        public ICollection<PlayerAnswerEntity> Answers { get; set; } = new List<PlayerAnswerEntity>();
    }
}
