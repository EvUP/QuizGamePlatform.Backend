using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Application.Mappers;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.Core.Helpers;
using QuizGamePlatform.Backend.DataAccess;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class RoomService(
        IRoomRepository roomRepository,
        IRoomParticipationRepository roomParticipationRepository,
        IUserRepository userRepository,
        IMatchRepository matchRepository,
        ICurrentUserContext currentUser,

        IRoomHelper roomHelper,
        ApplicationDbContext context,
        ILogger<RoomService> logger,
        TimeProvider timeProvider) : IRoomService
    {
        private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

        public async Task<CreateRoomResponse> CreateRoomAsync(CancellationToken ct)
        {
            logger.LogInformation("Creating new Room");

            // владелец сразу игрок под своим логином
            var host = await GetOrCreateCurrentUserAsync(ct);
            var room = await roomRepository.CreateRoomAsync(roomHelper.GenerateRoomCode(), host, ct);
            await roomParticipationRepository.CreateRoomPlayer(room, host.UserName, host, ct);

            await context.SaveChangesAsync(ct);

            logger.LogInformation("Room with id: {RoomId} successfully created", room.Id);

            return room.ToCreateRoomResponse();
        }

        public async Task<bool> IsRoomHostAsync(Guid roomId, CancellationToken ct)
            => currentUser.KeycloakSubject is { } subject
                && await roomRepository.IsHost(roomId, subject, ct);

        public async Task<bool> DeleteExistingRoomByIdAsync(Guid id, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(id, ct);

            // после старта комната уже история и держит очки рейтинга
            if (room is null || room.Status != RoomStatus.Waiting)
            {
                return false;
            }

            bool isDeletedRoom = await roomRepository.DeleteExistingRoom(id, ct);

            if (isDeletedRoom)
            {
                logger.LogInformation("Room with {id} sucessfully deleted", id);
            }

            return isDeletedRoom;
        }

        public async Task<RoomStateResponse?> GetRoomStateAsync(Guid roomId, CancellationToken ct)
        {
            // комнату видят только её участники
            var me = await FindMeAsync(roomId, ct);

            if (me is null)
            {
                return null;
            }

            var room = (await roomRepository.GetRoomByIdAsync(roomId, ct))!;
            var players = await roomParticipationRepository.GetParticipationsByRoomId(roomId, ct);
            var match = await matchRepository.GetMatchByRoomId(roomId, ct);

            return new RoomStateResponse(
                room.Id,
                room.RoomCode,
                room.Status,
                match?.Id,
                me.Id,
                players.Select(p => p.ToRoomParticipationResponse(room.HostUserId)).ToList());
        }

        public async Task<List<CreateRoomResponse>> GetMyRoomsAsync(CancellationToken ct)
        {
            var rooms = await roomRepository.GetRoomsOfUser(currentUser.KeycloakSubject!, ct);

            return rooms.Select(r => r.ToCreateRoomResponse()).ToList();
        }

        public async Task<(RoomResponse? Room, bool NicknameTaken)> JoinToRoomByRoomCodeAsync(
            string? nickname,
            string roomCode,
            CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByRoomCodeAsync(roomCode.Trim().ToUpperInvariant(), ct);

            if (room == null || room.Status == RoomStatus.Finished)
            {
                logger.LogInformation("Room with roomcode {roomcode} is not found or finished", roomCode);

                return (null, false);
            }

            // своё участие только по токену
            var roomPlayer = await FindMeAsync(room.Id, ct);

            if (roomPlayer != null)
            {
                return (await RejoinAsync(roomPlayer, room, ct), false);
            }

            // новый игрок заходит только до старта матча
            if (room.Status != RoomStatus.Waiting)
            {
                return (null, false);
            }

            // аккаунт играет под логином, гость под своим ником
            var user = currentUser.KeycloakSubject is not null ? await GetOrCreateCurrentUserAsync(ct) : null;
            var name = user?.UserName ?? nickname!.Trim().ToLowerInvariant();

            // логин аккаунта гостю не отдаём
            if (await roomParticipationRepository.IsNicknameTaken(room.Id, name, ct)
                || user is null && await userRepository.IsUserNameTaken(name, ct))
            {
                return (null, true);
            }

            if (await IsRoomFullAsync(room.Id, ct))
            {
                logger.LogInformation("Room with roomcode {roomcode} is full", roomCode);

                return (null, false);
            }

            var link = await roomParticipationRepository.CreateRoomPlayer(room, name, user, ct);

            logger.LogInformation("Player {name} joined room {roomCode}", name, roomCode);
            await context.SaveChangesAsync(ct);

            return (link.ToJoinRoomResponse(), false);
        }

        private async Task<RoomResponse?> RejoinAsync(RoomPlayerEntity roomPlayer, RoomEntity room, CancellationToken ct)
        {
            if (roomPlayer.IsActive)
            {
                return roomPlayer.ToJoinRoomResponse();
            }

            // до старта мешает только полная комната, место владельца за ним, в матче окно реконнекта
            var isHost = room.HostUserId is not null && roomPlayer.UserId == room.HostUserId;
            var canReturn = room.Status == RoomStatus.Waiting
                ? isHost || !await IsRoomFullAsync(room.Id, ct)
                : await CanReconnectAsync(roomPlayer, room.Id, ct);

            if (!canReturn)
            {
                logger.LogInformation("Participant {RoomPlayerId} can't return to room {RoomCode}", roomPlayer.Id, room.RoomCode);

                return null;
            }

            roomPlayer.IsActive = true;
            roomPlayer.FinishedAt = null;
            roomPlayer.ExitReason = null;

            await context.SaveChangesAsync(ct);

            return roomPlayer.ToJoinRoomResponse();
        }

        public async Task<LeaveRoomResponse?> LeaveRoom(Guid roomId, ExitReason exitReason, CancellationToken ct)
        {
            var roomPlayer = await FindMeAsync(roomId, ct);

            if (roomPlayer == null)
            {
                logger.LogWarning("Attempted to leave room {RoomId} by user who is not in it", roomId);

                return null;
            }

            if (!roomPlayer.IsActive)
            {
                logger.LogInformation("Participant {RoomPlayerId} is already inactive in room {RoomId}", roomPlayer.Id, roomId);

                return null;
            }

            roomPlayer.FinishedAt = UtcNow;
            roomPlayer.IsActive = false;
            roomPlayer.ExitReason = exitReason;

            logger.LogInformation(
            "Participant {RoomPlayerId} left room {RoomId} due to {ExitReason}",
            roomPlayer.Id, roomId, exitReason);

            await context.SaveChangesAsync(ct);

            return roomPlayer.ToLeaveRoomResponse();
        }

        public async Task<List<GlobalLeaderboardEntry>> GetGlobalLeaderboardAsync(CancellationToken ct)
            => await roomParticipationRepository.GetGlobalLeaderboard(ct);

        private Task<RoomPlayerEntity?> FindMeAsync(Guid roomId, CancellationToken ct)
            => roomParticipationRepository.FindParticipant(
                roomId, currentUser.RoomPlayerLinkId, currentUser.KeycloakSubject, ct);

        private Task<UserEntity> GetOrCreateCurrentUserAsync(CancellationToken ct)
            => userRepository.GetOrCreateUserAsync(currentUser.KeycloakSubject!, currentUser.DisplayName!, ct);

        // слоты держат активные и владелец, даже если вышел
        private async Task<bool> IsRoomFullAsync(Guid roomId, CancellationToken ct)
            => await roomParticipationRepository.CountTakenSlots(roomId, ct) >= MatchHelper.MaxMatchPlayer;

        // в идущий матч возвращается только вышедший после старта и пока не истекло окно
        private async Task<bool> CanReconnectAsync(RoomPlayerEntity roomPlayer, Guid roomId, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchByRoomId(roomId, ct);

            return match is not null
                && roomPlayer.FinishedAt is { } finishedAt
                && finishedAt >= match.StartedAt
                && UtcNow - finishedAt <= TimeSpan.FromSeconds(RoomHelper.ReconnectWindowSeconds);
        }
    };
}
