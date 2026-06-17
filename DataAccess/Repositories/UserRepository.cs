using Microsoft.EntityFrameworkCore;
using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.DataAccess.Repositories
{
    public class UserRepository(ApplicationDbContext context) : IUserRepository
    {
        private readonly ApplicationDbContext _context = context;

        public async Task<List<UserEntity>> GetAllAsync()
        {
            return await _context.Users.ToListAsync();
        }

        public async Task<UserEntity> AddAsync(UserDto dto)
        {
            var entity = new UserEntity
            {
                Id = dto.Id,
                Name = dto.Name,
                Surname = dto.Surname,
            };

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