using Microsoft.EntityFrameworkCore;
using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.DataAccess.Repositories
{
    public class UserRepository(ApplicationDbContext context) : IUserRepository
    {
        private readonly ApplicationDbContext _context = context;
        public async Task<List<UserEntity>> GetAllAsync(int? limit)
        {
            var baseQuery = _context.Users
                .AsNoTracking()
                .OrderBy(u => u.Id);

            IQueryable<UserEntity> query = baseQuery;

            if (limit.HasValue && limit.Value > 0)
            {
                query = query.Take(limit.Value);
            }

            return await query.ToListAsync();
        }

        public async Task<UserEntity> AddAsync(UserEntity entity)
        {
            _context.Users.Add(entity);
            await _context.SaveChangesAsync();

            return entity;
        }

        public async Task<bool> RemoveAsync(string id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
            {
                return false;
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return true;
        }
    }
}