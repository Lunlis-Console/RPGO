namespace RPGGame.Shared.Network;

/// <summary>
/// Информация об одном друге для отображения в списке.
/// </summary>
public sealed class FriendInfo
{
    public string Name { get; set; } = "";
    public bool Online { get; set; }
    public int Level { get; set; }
    public string Class { get; set; } = "";
}

/// <summary>
/// Полный список друзей (сервер -> клиент).
/// </summary>
public sealed class FriendListData
{
    public List<FriendInfo> Friends { get; set; } = new();
}

/// <summary>
/// Запрос клиента: добавить/удалить друга. Data = имя цели.
/// </summary>
public sealed class FriendRequest
{
    public string TargetName { get; set; } = "";
}

/// <summary>
/// Результат операции со списком друзей (сервер -> клиент).
/// </summary>
public sealed class FriendResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
