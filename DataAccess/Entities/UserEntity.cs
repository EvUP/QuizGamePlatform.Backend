namespace QuizGamePlatform.Backend.DataAccess.Entities
{
    // зарегистрированный пользователь
    public class UserEntity
    {
        public Guid Id { get; set; }
        public string KeycloakSubject { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public ICollection<RoomPlayerEntity> RoomParticipations { get; set; } = new List<RoomPlayerEntity>();
    }
}
