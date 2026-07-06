namespace testBdControllers.DataAccess.Entities
{
    public class PlayerEntity
    {
        public Guid Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public ICollection<RoomPlayerEntity> RoomParticipations { get; set; } = new List<RoomPlayerEntity>();
    }
}