using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public class SkillsRequestHandler : BaseHandler
{
    public SkillsRequestHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        var skills = DatabaseManager.LoadSkills();

        await SendToClient(connection, new GameMessage
        {
            Type = "skills_response",
            Data = new
            {
                Skills = skills.Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Description,
                    s.Type,
                    s.MpCost,
                    s.CooldownMs,
                    s.DamageMultiplier,
                    s.MinLevel
                }).ToList()
            }
        });
    }
}
