namespace QuizGamePlatform.Backend.DataAccess.Entities
{
    public class CategoryEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ICollection<QuestionEntity> Questions { get; set; } = new List<QuestionEntity>();
    }
}
