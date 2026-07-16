using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.DataAccess.Entities
{
    public class MatchEntity
    {
        public Guid Id { get; set; }

        public Guid RoomId { get; set; }
        public RoomEntity Room { get; set; } = null!;

        public MatchStatus Status { get; set; } = MatchStatus.QuestionActive;
        public int CurrentQuestionIndex { get; set; }

        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }

        public ICollection<MatchQuestionEntity> Questions { get; set; } = new List<MatchQuestionEntity>();
    }
}
