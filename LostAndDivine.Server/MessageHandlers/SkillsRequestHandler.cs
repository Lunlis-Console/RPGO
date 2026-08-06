using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

public class SkillsRequestHandler : BaseHandler
{
    public SkillsRequestHandler(GameServices svc) : base(svc) { }

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
                    s.MinLevel,
                    s.SkillPointCost,
                    s.ParentId,
                    s.Tier,
                    s.IconName,
                    s.MaxRank
                }).ToList(),
                LearnedSkills = player?.LearnedSkills ?? new(),
                SkillRanks = player?.SkillRanks ?? new(),
                SkillPoints = player?.SkillPoints ?? 0
            }
        });
    }
}
