using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.DataAccess.Entities
{
    public class RoomPlayerEntity
    {
        public Guid Id { get; set; }

        public Guid RoomId { get; set; }
        public RoomEntity Room { get; set; } = null!;

        public string Nickname { get; set; } = string.Empty;

        public Guid? UserId { get; set; }
        public UserEntity? User { get; set; }

        public DateTime JoinedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public int Score { get; set; }
        public bool IsActive { get; set; } = true;
        public ExitReason? ExitReason { get; set; }

        // null в обычной комнате, у ушедших и убранных до старта
        public int? TeamNumber { get; set; }

        // капитан тот, кто раньше всех в команде
        public DateTime? TeamJoinedAt { get; set; }

        // первое участие человека в серии, следующие игры ссылаются на него; по нему гость находит себя в новой игре
        public Guid? OriginRoomPlayerId { get; set; }
    }
}
