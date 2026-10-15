namespace QuizGamePlatform.Backend.DataAccess.Entities
{
    // команда живёт только внутри своей комнаты
    public class RoomTeamEntity
    {
        public Guid RoomId { get; set; }
        public RoomEntity Room { get; set; } = null!;

        public int Number { get; set; }
        public string Name { get; set; } = string.Empty;

        // код из ссылки команды, по нему в неё входят
        public string JoinCode { get; set; } = string.Empty;

        // сколько человек ждёт ведущий, только для подсказки
        public int? ExpectedPlayers { get; set; }
    }
}
