using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class UserRepository(ApplicationDbContext context) : IUserRepository
    {
        public async Task<UserEntity> GetOrCreateUserAsync(string keycloakSubject, string userName, CancellationToken ct)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.KeycloakSubject == keycloakSubject, ct);

            if (user is not null)
            {
                return user;
            }

            var newUser = new UserEntity
            {
                Id = Guid.NewGuid(),
                KeycloakSubject = keycloakSubject,
                UserName = userName
            };

            await context.Users.AddAsync(newUser, ct);
            // сохраняет RoomService
            return newUser;
        }
    }
}
