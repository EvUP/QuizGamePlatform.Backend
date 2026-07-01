using Microsoft.Extensions.Logging;
using Moq;
using testBdControllers.Application.Contracts;
using testBdControllers.Application.Services;
using testBdControllers.Core.Abstractions;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.Tests
{
    public class UserServiceTests
    {
        private readonly Mock<IUserRepository> _repository = new();
        private readonly UserService _sut;

        public UserServiceTests()
        {
            _sut = new UserService(_repository.Object, Mock.Of<ILogger<UserService>>());
        }

        [Fact]
        public async Task GetUserByIdAsync_WhenExists_ReturnsDto()
        {
            var entity = new UserEntity { Id = "1", Name = "Иван", Surname = "Петров" };
            _repository
                .Setup(r => r.GetByIdAsync("1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(entity);

            var result = await _sut.GetUserByIdAsync("1", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("1", result!.Id);
            Assert.Equal("Иван", result.Name);
            Assert.Equal("Петров", result.Surname);
        }

        [Fact]
        public async Task GetUserByIdAsync_WhenNotFound_ReturnsNull()
        {
            _repository
                .Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserEntity?)null);

            var result = await _sut.GetUserByIdAsync("missing", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task AddUserAsync_MapsDto_AndPassesEntityToRepository()
        {
            var dto = new CreateUserDto { Name = "Анна", Surname = "Смирнова" };
            _repository
                .Setup(r => r.AddAsync(It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((UserEntity e, CancellationToken _) => e);

            var result = await _sut.AddUserAsync(dto, CancellationToken.None);

            Assert.Equal("Анна", result.Name);
            Assert.Equal("Смирнова", result.Surname);
            Assert.False(string.IsNullOrEmpty(result.Id));
            _repository.Verify(
                r => r.AddAsync(
                    It.Is<UserEntity>(e => e.Name == "Анна" && e.Surname == "Смирнова"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task RemoveUserAsync_WhenIdEmpty_ReturnsFalse_WithoutRepositoryCall()
        {
            var result = await _sut.RemoveUserAsync("", CancellationToken.None);

            Assert.False(result);
            _repository.Verify(
                r => r.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task RemoveUserAsync_WhenIdValid_DelegatesToRepository()
        {
            _repository
                .Setup(r => r.RemoveAsync("1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await _sut.RemoveUserAsync("1", CancellationToken.None);

            Assert.True(result);
            _repository.Verify(r => r.RemoveAsync("1", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
