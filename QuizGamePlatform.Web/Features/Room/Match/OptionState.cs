namespace QuizGamePlatform.Web.Features.Room.Match;

public enum OptionState
{
    Idle,       // можно выбрать
    Selected,   // мой ответ, ждём остальных
    Dimmed,     // не мой / не верный
    Correct,    // показ ответа: верный
    Wrong       // показ ответа: мой, но неверный
}
