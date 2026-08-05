using RPGGame.Server.Instances;
using RPGGame.Server.Services;

namespace RPGGame.Server.Dependencies;

public interface IGameWorldDeps
{
    MerchantManager Merchant { get; }
    QuestManager Quests { get; }
    DialogueManager Dialogue { get; }
    CollectibleManager Collectibles { get; }
    InstanceManager Instances { get; }
    StorageService Storage { get; }
    TradeManager Trade { get; }
    InteractionService Interactions { get; }
    MonsterCombatCalculator MonsterCombat { get; }
    MonsterAttackService MonsterAttacks { get; }
}
