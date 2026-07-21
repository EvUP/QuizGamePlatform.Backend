namespace QuizGamePlatform.Backend.DataAccess.Caching
{
    public static class RedisKeysFactory
    {
        public const string CategoriesListKey = "categories:list";
        public static string QuestionsByCategory(Guid categoryId) => $"questions:category:{categoryId}";
    }
}