using System.Text.Json;
using System.Text.Json.Serialization;
using QuizGamePlatform.Web.Api.Contracts;

namespace QuizGamePlatform.Web.Api;

// Сериализация без рефлексии: не ломается при обрезке сборок (trimming) в WebAssembly.
// Web-настройки: camelCase как у бэка, enum'ы строками.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(CreateRoomRequest))]
[JsonSerializable(typeof(CreateRoomResponse))]
[JsonSerializable(typeof(List<MyRoomResponse>))]
[JsonSerializable(typeof(InviteResponse))]
[JsonSerializable(typeof(JoinToRoomRequest))]
[JsonSerializable(typeof(JoinToRoomResponse))]
[JsonSerializable(typeof(RoomStateResponse))]
[JsonSerializable(typeof(PreviousMatchSettings))]
[JsonSerializable(typeof(LeaveRoomRequest))]
[JsonSerializable(typeof(LeaveRoomResponse))]
[JsonSerializable(typeof(ChangeTeamRequest))]
[JsonSerializable(typeof(TeamNameRequest))]
[JsonSerializable(typeof(StartMatchRequest))]
[JsonSerializable(typeof(SubmitAnswerRequest))]
[JsonSerializable(typeof(MatchStateResponse))]
[JsonSerializable(typeof(PlayerAnsweredEvent))]
[JsonSerializable(typeof(CurrentQuestionResponse))]
[JsonSerializable(typeof(MatchReviewResponse))]
[JsonSerializable(typeof(List<CategoryDto>))]
[JsonSerializable(typeof(List<GlobalLeaderboardEntry>))]
[JsonSerializable(typeof(CurrentUserResponse))]
[JsonSerializable(typeof(CommonErrorResponse))]
internal partial class ApiJsonContext : JsonSerializerContext
{
}
