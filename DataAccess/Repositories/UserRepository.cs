using Microsoft.EntityFrameworkCore;
using testBdControllers.Core.Abstractions;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.DataAccess.Repositories
{
    public class UserRepository(ApplicationDbContext context) : IUserRepository
    {
        private readonly ApplicationDbContext _context = context;

        // Добавлен CancellationToken ct
        public async Task<List<UserEntity>> GetAllAsync(int? limit, CancellationToken ct)
        {
            var baseQuery = _context.Users
                .AsNoTracking()
                .OrderBy(u => u.Id);

            IQueryable<UserEntity> query = baseQuery;

            if (limit.HasValue && limit.Value > 0)
            {
                query = query.Take(limit.Value);
            }

            // Передаем токен в ToListAsync
            return await query.ToListAsync(ct);
        }

        // Добавлен CancellationToken ct
        public async Task<UserEntity> AddAsync(UserEntity entity, CancellationToken ct)
        {
            _context.Users.Add(entity);

            // Передаем токен в SaveChangesAsync
            await _context.SaveChangesAsync(ct);

            return entity;
        }

        // Добавлен CancellationToken ct
        public async Task<bool> RemoveAsync(string id, CancellationToken ct)
        {
            // Передаем токен в FirstOrDefaultAsync
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

            if (user == null)
            {
                return false;
            }

            _context.Users.Remove(user);

            // Передаем токен в SaveChangesAsync
            await _context.SaveChangesAsync(ct);

            return true;
        }
    }
}
