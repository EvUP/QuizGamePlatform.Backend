
using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.DataAccess.Entities
{
    public class RoomEntity
    {
        public Guid Id { get; set; }
        public string RoomCode { get; set; } = string.Empty;
        public RoomStatus Status { get; set; } = RoomStatus.Waiting;
        public DateTime CreatedAt { get; set; }

        // у старых комнат владельца нет
        public Guid? HostUserId { get; set; }
        public UserEntity? HostUser { get; set; }

        // системный xmin postgres, меняется при каждом обновлении строки
        public uint Version { get; set; }

        public ICollection<RoomPlayerEntity> Players { get; set; } = new List<RoomPlayerEntity>();
    }
}
