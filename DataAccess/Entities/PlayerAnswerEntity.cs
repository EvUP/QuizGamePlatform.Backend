namespace QuizGamePlatform.Backend.DataAccess.Entities
{
    public class PlayerAnswerEntity
    {
        public Guid Id { get; set; }

        public Guid MatchQuestionId { get; set; }
        public MatchQuestionEntity MatchQuestion { get; set; } = null!;

        public Guid PlayerId { get; set; }
        public PlayerEntity Player { get; set; } = null!;

        public Guid SelectedOptionId { get; set; }
        public AnswerOptionEntity SelectedOption { get; set; } = null!;

        public DateTime AnsweredAt { get; set; }
    }
}
