namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Исключение, бросаемое мигратором при ситуации, чреватой потерей данных
/// (например, БД содержит таблицы, но без истории миграций). Стартуемый
/// процесс должен остановиться вместо того, чтобы молча сбрасывать данные.
/// </summary>
public sealed class MigrationException : Exception
{
    public MigrationException(string message) : base(message) { }
    public MigrationException(string message, Exception inner) : base(message, inner) { }
}
