using Microsoft.EntityFrameworkCore;
using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.DataAccess.Repositories
{
    public class UserRepository(ApplicationDbContext context) : IUserRepository
    {
        public async Task<List<UserEntity>> GetAllAsync(int? limit, CancellationToken ct)
        {
            var baseQuery = context.Users
                .AsNoTracking()
                .OrderBy(u => u.Id);

            IQueryable<UserEntity> query = baseQuery;

            if (limit.HasValue && limit.Value > 0)
            {
                query = query.Take(limit.Value);
            }

            return await query.ToListAsync(ct);
        }

        public async Task<UserEntity> AddAsync(UserEntity entity, CancellationToken ct)
        {
            context.Users.Add(entity);

            await context.SaveChangesAsync(ct);

            return entity;
        }

        public async Task<bool> RemoveAsync(string id, CancellationToken ct)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

            if (user == null)
            {
                return false;
            }

            context.Users.Remove(user);

            await context.SaveChangesAsync(ct);

            return true;
        }

        public async Task<UserEntity?> UpdateAsync(string id, UpdateUserDto dto, CancellationToken ct)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

            if (user == null)
                return null;

            if (!string.IsNullOrEmpty(dto.Name))
                user.Name = dto.Name;

            if (!string.IsNullOrEmpty(dto.Surname))
                user.Surname = dto.Surname;

            await context.SaveChangesAsync(ct);
            
            return user;
        }
    }
}

