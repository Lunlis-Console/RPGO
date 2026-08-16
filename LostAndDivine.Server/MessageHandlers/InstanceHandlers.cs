using System.Text.Json;
using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

/// <summary>Список инстансов для окна выбора.</summary>
public class InstanceListRequestHandler : BaseHandler
{
    public InstanceListRequestHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        var list = Svc.Instances.GetInstanceList()
            .Select(i => new { Id = i.Id, Name = i.Name, MinLevel = i.MinLevel, MaxLevel = i.MaxLevel })
            .ToList();
        await SendToClient(connection, new GameMessage
        {
            Type = "instance_list",
            Data = new { Instances = list }
        });
    }
}

/// <summary>Соло-вход в инстанс из окна.</summary>
public class InstanceEnterSoloHandler : BaseHandler
{
    public InstanceEnterSoloHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        string templateId = "";
        if (message.Data is JsonElement el && el.TryGetProperty("TemplateId", out var t))
            templateId = t.GetString() ?? "";
        if (string.IsNullOrEmpty(templateId)) return;
        await Svc.Instances.TryEnter(player, templateId, connection, InstanceMode.Solo);
    }
}

/// <summary>Лидер приглашает группу в групповой инстанс.</summary>
public class InstanceInviteHandler : BaseHandler
{
    public InstanceInviteHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        string templateId = "";
        if (message.Data is JsonElement el && el.TryGetProperty("TemplateId", out var t))
            templateId = t.GetString() ?? "";
        if (string.IsNullOrEmpty(templateId)) return;
        await Svc.Instances.InviteParty(player, templateId, connection);
    }
}

/// <summary>Ответ члена группы на приглашение (готов/отказ).</summary>
public class InstanceInviteResponseHandler : BaseHandler
{
    public InstanceInviteResponseHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        bool ready = false;
        if (message.Data is JsonElement el && el.TryGetProperty("Ready", out var r))
            ready = r.GetBoolean();
        await Svc.Instances.RespondInvite(player, ready, connection);
    }
}

/// <summary>Ручной запуск группового инстанса лидером.</summary>
public class InstanceStartHandler : BaseHandler
{
    public InstanceStartHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        await Svc.Instances.StartGroup(player, connection);
    }
}