using StackExchange.Redis;

namespace QuizGamePlatform.Backend.Api.Hubs
{
    // живые соединения участника со всех инстансов: переподключиться он может к другому
    public class ConnectionPresence(string redisConnection, ILogger<ConnectionPresence> logger)
    {
        // упавший инстанс не уберёт свои соединения, ключ истечёт сам
        private static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

        // подключаемся при первом обращении, старт приложения Redis не ждёт
        private readonly Lazy<Task<ConnectionMultiplexer>> _redis = new(() =>
        {
            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = false;

            return ConnectionMultiplexer.ConnectAsync(options);
        });

        private static RedisKey Key(Guid roomPlayerId) => $"quiz-platform-presence:{roomPlayerId}";

        public async Task AddAsync(Guid roomPlayerId, string connectionId)
        {
            try
            {
                var db = (await _redis.Value).GetDatabase();
                await db.SetAddAsync(Key(roomPlayerId), connectionId);
                await db.KeyExpireAsync(Key(roomPlayerId), Lifetime);
            }
            catch (RedisException ex)
            {
                logger.LogWarning(ex, "Failed to track connection of participant {RoomPlayerId}", roomPlayerId);
            }
        }

        public async Task RemoveAsync(Guid roomPlayerId, string connectionId)
        {
            try
            {
                await (await _redis.Value).GetDatabase().SetRemoveAsync(Key(roomPlayerId), connectionId);
            }
            catch (RedisException ex)
            {
                logger.LogWarning(ex, "Failed to untrack connection of participant {RoomPlayerId}", roomPlayerId);
            }
        }

        // без Redis не знаем точно, поэтому считаем на связи: лучше не выкинуть живого
        public async Task<bool> IsOnlineAsync(Guid roomPlayerId)
        {
            try
            {
                return await (await _redis.Value).GetDatabase().SetLengthAsync(Key(roomPlayerId)) > 0;
            }
            catch (RedisException ex)
            {
                logger.LogWarning(ex, "Failed to check connections of participant {RoomPlayerId}", roomPlayerId);

                return true;
            }
        }
    }
}
