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
    private const int MaxDepth = 4;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, FieldInfo[]> _fieldCache = new();

    public static bool IsOwnedBy(Delegate? handler, object? target)
    {
        if (handler == null || target == null)
            return false;

        // Мультикаст-делегат может содержать несколько invocation, проверяем каждый (P1-12 fix)
        foreach (var d in handler.GetInvocationList())
        {
            if (ReferenceEquals(d.Target, target))
                return true;
            var captured = d.Target;
            if (captured == null) continue;
            if (IsCapturedBy(captured, target, 0))
                return true;
        }
        return false;
    }

    private static bool IsCapturedBy(object captured, object target, int depth)
    {
        if (depth > MaxDepth) return false;
        var type = captured.GetType();
        // Только closure-классы, обычные target уже проверены выше
        if (type.Name.IndexOf('<') < 0)
            return false;

        var fields = _fieldCache.GetOrAdd(type, t => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        foreach (var field in fields)
        {
            object? value;
            try { value = field.GetValue(captured); }
            catch { continue; }

            if (ReferenceEquals(value, target))
                return true;

            if (value is Delegate inner)
            {
                if (IsOwnedBy(inner, target))
                    return true;
                continue;
            }

            // Вложенный closure (depth limit + cache)
            if (value != null && depth + 1 <= MaxDepth && value.GetType().Name.IndexOf('<') >= 0)
            {
                if (IsCapturedBy(value, target, depth + 1))
                    return true;
            }
        }
        return false;
    }
}
