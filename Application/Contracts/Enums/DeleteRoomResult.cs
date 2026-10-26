namespace QuizGamePlatform.Backend.Application.Enums
{
    public enum DeleteRoomResult
    {
        Deleted,
        NotFound,
        NotWaiting,    // игра уже началась или прошла
        Busy           // комнату меняли одновременно, можно повторить
    }
}
