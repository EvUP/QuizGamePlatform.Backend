namespace QuizGamePlatform.Backend.Application.Auth
{
    public static class AuthorizationPolicies
    {
        // только вход через Keycloak, гостю 403
        public const string Registered = "Registered";
    }
}
