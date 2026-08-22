using System.Reflection;

namespace LostAndDivine.Shared;

/// <summary>
/// Определяет, «принадлежит» ли обработчик события заданному подписчику, чтобы
/// его можно было корректно отписать.
///
/// Обычная проверка <c>handler.Target == target</c> не срабатывает для лямбда-подписок:
/// компилятор упаковывает захваченный <c>this</c> в сгенерированный closure-класс,
/// поэтому <c>handler.Target</c> — это closure, а не сам подписчик. Из-за этого
/// <c>GameClient.UnsubscribeAll(target)</c> не мог отписать лямбды и singleton-клиент
/// удерживал экран в памяти (утечка при каждом входе в мир, P0-2).
///
/// Здесь мы рекурсивно заглядываем в поля closure-класса и ищем среди них ссылку
/// на <paramref name="target"/> (прямую или через вложенный делегат).
/// </summary>
public static class SubscriptionMatcher
{
    public static bool IsOwnedBy(Delegate? handler, object? target)
    {
        if (handler == null || target == null)
            return false;

        if (ReferenceEquals(handler.Target, target))
            return true;

        var captured = handler.Target;
        if (captured == null)
            return false;

        // Компилятор-generated closure-классы содержат '<' в имени (например,
        // <>c__DisplayClass). Обычные объекты (включая методы-группы) здесь не нужны:
        // для них сработала первая проверка handler.Target == target.
        if (captured.GetType().Name.IndexOf('<') < 0)
            return false;

        foreach (var field in captured.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            object? value;
            try { value = field.GetValue(captured); }
            catch { continue; }

            if (ReferenceEquals(value, target))
                return true;

            if (value is Delegate inner)
                return IsOwnedBy(inner, target);
        }

        return false;
    }
}
