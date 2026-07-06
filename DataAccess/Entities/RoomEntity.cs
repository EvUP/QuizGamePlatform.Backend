
using testBdControllers.Application.Enums;

namespace testBdControllers.DataAccess.Entities
{
    public class RoomEntity
    {
        public Guid Id { get; set; }
        public Guid RoomCode { get; set; }
        public RoomStatus Status { get; set; } = RoomStatus.Waiting;
        public DateTime CreatedAt { get; set; }
        public ICollection<RoomPlayerEntity> Players { get; set; } = new List<RoomPlayerEntity>();
        public ICollection<QuestionEntity> Questions { get; set; } = new List<QuestionEntity>();
    }
}
