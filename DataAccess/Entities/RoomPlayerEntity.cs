using testBdControllers.Application.Enums;

namespace testBdControllers.DataAccess.Entities
{
    public class RoomPlayerEntity
    {
        public Guid Id { get; set; }

        public Guid RoomId { get; set; }
        public RoomEntity Room { get; set; } = null!;

        public Guid PlayerId { get; set; }
        public PlayerEntity Player { get; set; } = null!;

        public DateTime JoinedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public int Score { get; set; }
        public bool IsActive { get; set; } = true;
        public ExitReason? ExitReason { get; set; }
    }
}
