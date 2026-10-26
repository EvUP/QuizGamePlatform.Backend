namespace QuizGamePlatform.Bots;

// новый сотрудник, который сам регистрируется: имя и фамилия по-русски, логин как у остальных аккаунтов
public sealed record NewPerson(string FirstName, string LastName, string Login)
{
    private static readonly (string Ru, string Lat, bool Female)[] FirstNames =
    [
        ("Олег", "oleg", false), ("Павел", "pavel", false), ("Денис", "denis", false), ("Артём", "artem", false),
        ("Кирилл", "kirill", false), ("Роман", "roman", false), ("Глеб", "gleb", false), ("Тимур", "timur", false),
        ("Марина", "marina", true), ("Дарья", "darya", true), ("Ольга", "olga", true), ("Ксения", "ksenia", true),
        ("Полина", "polina", true), ("Елена", "elena", true), ("Вера", "vera", true), ("Софья", "sofya", true),
    ];

    // только -ов/-ин/-ев: женская фамилия получается добавлением «а»
    private static readonly (string Ru, string Lat)[] LastNames =
    [
        ("Орлов", "orlov"), ("Волков", "volkov"), ("Зайцев", "zaitsev"), ("Белов", "belov"),
        ("Комаров", "komarov"), ("Лебедев", "lebedev"), ("Носов", "nosov"), ("Гусев", "gusev"),
        ("Медведев", "medvedev"), ("Соколов", "sokolov"), ("Фомин", "fomin"), ("Ильин", "ilyin"),
    ];

    public string Email => $"{Login}@corp.local";

    // цифры в логине, чтобы не совпасть с уже заведёнными anna.smirnova и прочими
    public static NewPerson Random()
    {
        var first = FirstNames[System.Random.Shared.Next(FirstNames.Length)];
        var last = LastNames[System.Random.Shared.Next(LastNames.Length)];
        var suffix = first.Female ? "а" : "";
        var latSuffix = first.Female ? "a" : "";

        return new(first.Ru, last.Ru + suffix, $"{first.Lat}.{last.Lat}{latSuffix}{System.Random.Shared.Next(10, 100)}");
    }

    public override string ToString() => $"{FirstName} {LastName}";
}
