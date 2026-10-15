namespace QuizGamePlatform.Backend.DataAccess.Entities
{
    public class CategoryEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // тема убрана из сида, но по её вопросам уже играли: в выбор тем не попадает, история игр цела
        public bool IsHidden { get; set; }

        public ICollection<QuestionEntity> Questions { get; set; } = new List<QuestionEntity>();
    }
}
