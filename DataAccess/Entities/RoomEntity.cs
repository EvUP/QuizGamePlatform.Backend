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

        // режим задаётся при создании и потом не меняется
        public bool IsTeamGame { get; set; }

        // «Сыграть ещё»: прошлая игра серии, у первой null
        public Guid? PreviousRoomId { get; set; }

        // Id первой игры серии, null пока не переигрывали
        public Guid? SeriesId { get; set; }

        public int GameNumber { get; set; } = 1;

        // системный xmin postgres, меняется при каждом обновлении строки
        public uint Version { get; set; }

        public ICollection<RoomPlayerEntity> Players { get; set; } = new List<RoomPlayerEntity>();
        public ICollection<RoomTeamEntity> Teams { get; set; } = new List<RoomTeamEntity>();
    }
}
