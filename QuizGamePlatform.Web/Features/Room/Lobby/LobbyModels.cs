using QuizGamePlatform.Web.Api.Contracts;

namespace QuizGamePlatform.Web.Features.Room.Lobby;

public sealed record PlayerMove(Guid LinkId, int TeamNumber);

public sealed record TeamOption(int Number, string Name);

/// <summary>Условие старта: выполнено или что мешает</summary>
public sealed record ReadinessCheck(string Text, bool IsMet);

public sealed record MatchSettingsChoice(IReadOnlyList<Guid> CategoryIds, int QuestionCount, RoomMode Mode);
