namespace testBdControllers.DataAccess.Entities
{
    public class QuestionEntity
    {
        public Guid Id { get; set; }
        public Guid RoomId { get; set; }
        public RoomEntity Room { get; set; } = null!;
        public string Text { get; set; } = string.Empty;
        
        // Порядок вопроса в раунде (чтобы показывать по порядку)
        public int Order { get; set; }
        public ICollection<AnswerOptionEntity> AnswerOptions { get; set; } = new List<AnswerOptionEntity>();
    }
}