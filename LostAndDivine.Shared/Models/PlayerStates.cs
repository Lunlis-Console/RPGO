namespace LostAndDivine.Shared.Models;

/// <summary>
/// Состояние перемещения игрока: текущая позиция задаётся в Player (X/Y),
/// здесь — путь и флаг ручного управления.
/// </summary>
public class MovementState
{
    public List<(int X, int Y)> Path { get; set; } = new();
    public DateTime LastMoveTime { get; set; } = DateTime.MinValue;

    public bool HasPath => Path.Count > 0;

    public void Stop()
    {
        Path.Clear();
    }

    public void SetPath(List<(int X, int Y)> path)
    {
        Path = path ?? new List<(int X, int Y)>();
    }
}

/// <summary>
/// Состояние боя: цель, флаг в бою, таймер последней атаки.
/// Вход в бой явно останавливает перемещение.
/// </summary>
public class CombatState
{
    public bool InCombat { get; set; }
    public Guid? TargetMonsterId { get; set; }
    public Guid? TargetPlayerId { get; set; }
    public DateTime LastAttackTime { get; set; } = DateTime.MinValue;
    public DateTime OffHandLastAttackTime { get; set; } = DateTime.MinValue;

    // Комбо-навыки: отложенные удары
    public int PendingSkillHitsRemaining { get; set; }
    public DateTime PendingSkillLastHitTime { get; set; } = DateTime.MinValue;
    public Guid? PendingSkillTargetId { get; set; }
    public string? PendingSkillId { get; set; }

    // Каст навыка с временем применения (не блокирует глобальный тик)
    public string? CastingSkillId { get; set; }
    public DateTime CastEndTime { get; set; } = DateTime.MinValue;
    public Guid? CastTargetId { get; set; }

    // «ЭТО ДУЭЛЬ!»: наказание за смену таргета целью «армировано»,
    // только если цель сама атаковала игрока на момент первого удара.
    public bool DuelPunishArmed { get; set; }

    public bool HasTarget => TargetMonsterId != null || TargetPlayerId != null;
    public bool IsPvPTarget => TargetPlayerId != null;

    public void EnterMonster(Guid monsterId, MovementState movement)
    {
        TargetMonsterId = monsterId;
        TargetPlayerId = null;
        InCombat = true;
        movement.Stop();
        OffHandLastAttackTime = DateTime.MinValue;
        PendingSkillHitsRemaining = 0;
        PendingSkillId = null;
        CastingSkillId = null;
        CastTargetId = null;
    }

    public void EnterPlayer(Guid playerId, MovementState movement)
    {
        TargetPlayerId = playerId;
        TargetMonsterId = null;
        InCombat = true;
        movement.Stop();
        OffHandLastAttackTime = DateTime.MinValue;
        CastingSkillId = null;
        CastTargetId = null;
    }

    // Совместимость
    public void Enter(Guid monsterId, MovementState movement) => EnterMonster(monsterId, movement);

    public void Cancel()
    {
        TargetMonsterId = null;
        TargetPlayerId = null;
        InCombat = false;
        PendingSkillHitsRemaining = 0;
        PendingSkillId = null;
        CastingSkillId = null;
        CastTargetId = null;
        DuelPunishArmed = false;
    }
}

/// <summary>
/// Отложенное взаимодействие: игрок дошёл (или идёт) к точке и по прибытии
/// должен выполнить действие (магазин, доска, сбор, бой).
/// </summary>
    public class InteractionState
    {
        public string? Type { get; set; } // "monster", "merchant", "board", "collectible", "loot_corpse", "take_loot"
        public int X { get; set; }
        public int Y { get; set; }
        public Guid? MonsterId { get; set; }
        public Guid? CorpseId { get; set; }

        // Параметры отложенного поднятия лута (take_loot)
        public bool TakeAll { get; set; }
        public bool TakeGold { get; set; }
        public List<string> ItemIds { get; set; } = new();

        public bool IsPending => Type != null;

        public void Begin(string type, int x, int y, Guid? monsterId)
        {
            Type = type;
            X = x;
            Y = y;
            MonsterId = monsterId;
        }

        public void SetPending(string type)
        {
            Type = type;
        }

        public void Clear()
        {
            Type = null;
            MonsterId = null;
            CorpseId = null;
            X = 0;
            Y = 0;
            TakeAll = false;
            TakeGold = false;
            ItemIds.Clear();
        }
    }

/// <summary>
/// Состояние диалога: текущий NPC и текущий узел диалога.
/// </summary>
public class DialogueState
{
    public string? NpcId { get; set; }
    public string? CurrentNodeId { get; set; }
    public bool IsActive => NpcId != null && CurrentNodeId != null;

    public void Start(string npcId, string nodeId)
    {
        NpcId = npcId;
        CurrentNodeId = nodeId;
    }

    public void Clear()
    {
        NpcId = null;
        CurrentNodeId = null;
    }
}
