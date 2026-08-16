using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.ClientMonoGame.Input;
using LostAndDivine.ClientMonoGame.Networking;
using LostAndDivine.ClientMonoGame.Windows;
using LostAndDivine.Shared;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.ClientMonoGame.Screens;

public class GameScreen : IScreen
{
    private readonly MapRenderer _mapRenderer;
    private readonly MinimapRenderer _minimap = new();
    private readonly HudRenderer _hudRenderer;
    private readonly ChatRenderer _chatRenderer;
    private readonly InputManager _inputManager;
    private readonly WindowManager _windows = new();
    private readonly GameInputHandler _input;
    private readonly GameHudRenderer _hudDraw;

    // Windows
    private readonly InventoryWindow _inventoryWindow = new();
    private readonly StatusWindow _statusWindow = new();
    private readonly SkillsWindow _skillsWindow = new();
    private readonly EquipmentWindow _equipmentWindow = new();
    private readonly QuestLogWindow _questLogWindow = new();
    private List<QuestInfo> _activeQuests = new();
    private readonly ShopWindow _shopWindow = new();
    private readonly LootWindow _lootWindow = new();
    private readonly QuestBoardWindow _questBoardWindow = new();
    private readonly TradeWindow _tradeWindow = new();
    private readonly QuantityDialog _quantityDialog = new();
    private readonly EntityPickDialog _entityPickDialog = new();
    private readonly SettingsWindow _settingsWindow = new();
    private readonly LogoutConfirmWindow _logoutConfirmWindow = new();
    private readonly PartyInviteWindow _partyInviteWindow = new();
    private readonly TradeRequestWindow _tradeRequestWindow = new();
    private readonly SocialWindow _socialWindow;
    private readonly DeathWindow _deathWindow = new();
    private readonly DialogueWindow _dialogueWindow = new();
    private readonly MailWindow _mailWindow = new();
    private readonly MailAttachmentWindow _mailAttachmentWindow = new();
    private readonly StorageWindow _storageWindow = new();
    private readonly ChangelogWindow _changelogWindow = new();
    private readonly HashSet<string> _lootedCorpses = new();
    private int _lastPartyMemberCount;
    private HashSet<Guid> _lastPartyMemberIds = new();
    private string? _tileRequestedZone;
    private DateTime _tileRequestTime;
    private GameClient _client = null!;

    public GameScreen()
    {
        _client = GameMain.Instance!.Client;

        _mapRenderer = new MapRenderer();
        _hudRenderer = new HudRenderer();
        _chatRenderer = new ChatRenderer();
        _inputManager = new InputManager();
        _hudRenderer.SetInputManager(_inputManager);
        _input = new GameInputHandler(_inputManager, _mapRenderer, _hudRenderer, _chatRenderer, _windows);
        _hudDraw = new GameHudRenderer(_hudRenderer, _mapRenderer);

        _socialWindow = new SocialWindow(_client);
        _socialWindow.WhisperRequested += name =>
        {
            _chatRenderer.IsTyping = true;
            _chatRenderer.TypedText = $"/w {name} ";
        };

        WireMapEvents();
        WireCombatEvents();
        WirePartyEvents();
        WireStatusEvents();
        WireInventoryEvents();
        WireHotbarEvents();
        WireShopEvents();
        WireTradeEvents();
        WireQuestEvents();
        WireSkillsEvents();
        WireLootEvents();
        WireMapInteractionEvents();
        WireSettingsEvents();
        WireMailEvents();
        WireStorageEvents();
        WireDialogueEvents();
        WireDeathEvents();
        WireTradeRequestEvents();
        WireChangelogEvents();
        RegisterWindows();

        if (_client.LastChangelog != null)
            ShowChangelog(_client.LastChangelog);
    }

    private void WireMapEvents()
    {
        _client.MapUpdated += map =>
        {
            _mapRenderer.SetMap(map);
            _mapRenderer.SetPlayerName(_client.PlayerName);
            _mapRenderer.SetPlayerLevel(_client.PlayerLevel);
            _minimap.SetPlayerName(_client.PlayerName);
            _minimap.SetMap(map);
            _hudRenderer.UpdateInstanceTimer(map.InstanceExpiresAtUtcMs);
            if (map.TileData != null && map.TileData.Length > 0)
            {
                _mapRenderer.SetTileData(map.TileData, map.Width, map.Height, map.TilesetId ?? map.ZoneId, map.TileWidth);
                _minimap.SetTileData(map.TileData, map.Width, map.Height);
            }
            else if (!_mapRenderer.HasValidTiles(map.Width, map.Height))
            {
                RequestTilesIfNeeded(map);
            }
            if (map.ObstacleData != null && map.ObstacleData.Length > 0)
            {
                _mapRenderer.SetObstacleData(map.ObstacleData, map.Width, map.Height);
                _minimap.SetObstacleData(map.ObstacleData, map.Width, map.Height);
            }
            if (map.ObjectData != null && map.ObjectData.Length > 0)
            {
                _mapRenderer.SetObjectLayerData(map.ObjectData, map.Width, map.Height,
                    map.ObjectTilesetId ?? "", map.ObjectTileWidth > 0 ? map.ObjectTileWidth : map.TileWidth);
            }
            foreach (var p in map.Players)
            {
                if (p.Name == _client.PlayerName) continue;
                _mapRenderer.UpdateRemotePlayer(p.Name, p.Facing, p.WeaponSubtype, p.OffWeaponSubtype, p.ShieldSubtype, p.IsTwoHanded, p.IsDead, p.X, p.Y);
            }
            HazardRenderer.Sync(map.Hazards);
        };
        _client.ZoneChanged += (zoneId, zoneName, pvp) =>
        {
            _mapRenderer.ClearMap();
            _mapRenderer.SnapCameraNextFrame();
            if (!zoneId.StartsWith("instance:"))
                _hudRenderer.UpdateInstanceTimer(null);
        };
        _client.TileDataReceived += (data, w, h, tilesetId, tileSize) =>
        {
            _mapRenderer.SetTileData(data, w, h, tilesetId, tileSize);
            _minimap.SetTileData(data, w, h);
            _tileRequestedZone = null;
        };
        _client.ObstacleDataReceived += (data, w, h) =>
        {
            _mapRenderer.SetObstacleData(data, w, h);
            _minimap.SetObstacleData(data, w, h);
        };
        _client.ObjectLayerDataReceived += (data, w, h, tilesetId, tileSize) =>
        {
            _mapRenderer.SetObjectLayerData(data, w, h, tilesetId, tileSize);
        };
        _client.ChatReceived += (channel, name, text, isAdmin) =>
        {
            if (Enum.TryParse<ChatChannel>(channel, out var ch))
                _chatRenderer.AddMessage(ch, name, text, isAdmin);
            else
                _chatRenderer.AddMessage(ChatChannel.System, name, text, isAdmin);
        };
        _client.SystemMessage += msg => _chatRenderer.AddMessage(ChatChannel.System, "Система", msg);
        _client.WelcomeReceived += () =>
        {
            _ = _client.SendAsync("status", null);
            _ = _client.SendAsync("inventory_request", null);
        };
    }

    private void WireCombatEvents()
    {
        _client.FloatingTextReceived += (x, y, text, argb, isCrit) =>
        {
            uint a = argb;
            var color = new Color(
                (byte)((a >> 16) & 0xFFu),
                (byte)((a >> 8) & 0xFFu),
                (byte)(a & 0xFFu));
            Logger.Debug($"FLT screen argb={argb:X8} -> rgb=({color.R},{color.G},{color.B}) text={text}");
            _mapRenderer.SpawnFloatingText(x, y, text, color, isCrit);
        };
        _client.CombatStateUpdated += (inCombat, targetName, hp, maxHp, targetId) =>
        {
            _hudRenderer.UpdateCombatState(inCombat, targetName, hp, maxHp, targetId);
            if (!inCombat)
            {
                _mapRenderer.ClearSelection();
                _hudRenderer.UpdateTargetDebuffs(null);
            }
        };
        _client.PlayerAttackPerformed += (hand, skillId, targetX, targetY) =>
        {
            _mapRenderer.TriggerAttack(hand);
            if (skillId != null)
            {
                if (SkillEffectManager.IsOnPlayer(skillId))
                {
                    SkillEffectManager.Spawn(skillId, _mapRenderer.GetPlayerX(), _mapRenderer.GetPlayerY());
                }
                else if (targetX.HasValue && targetY.HasValue)
                {
                    SkillEffectManager.Spawn(skillId, targetX.Value, targetY.Value);
                }
                else
                {
                    var sel = _mapRenderer.GetSelectedMapPos();
                    if (sel.HasValue)
                        SkillEffectManager.Spawn(skillId, sel.Value.X, sel.Value.Y);
                    else
                        SkillEffectManager.Spawn(skillId, _mapRenderer.GetPlayerX(), _mapRenderer.GetPlayerY());
                }
            }
        };
        _client.RemotePlayerAttack += (playerName, hand, skillId, targetX, targetY, buffDurationMs) =>
        {
            _mapRenderer.TriggerRemoteAttack(playerName, hand);
            if (skillId != null)
            {
                if (SkillEffectManager.IsOnPlayer(skillId))
                {
                    var pos = _mapRenderer.GetRemotePlayerPos(playerName);
                    if (pos.HasValue)
                    {
                        if (buffDurationMs.HasValue && buffDurationMs.Value > 0)
                            SkillEffectManager.SpawnLooping(skillId, pos.Value.X, pos.Value.Y,
                                sourcePlayer: playerName, durationMs: buffDurationMs.Value, forceMap: true);
                        else
                            SkillEffectManager.Spawn(skillId, pos.Value.X, pos.Value.Y, forceMap: true);
                    }
                }
                else if (targetX.HasValue && targetY.HasValue)
                {
                    SkillEffectManager.Spawn(skillId, targetX.Value, targetY.Value);
                }
                else
                {
                    var pos = _mapRenderer.GetRemotePlayerPos(playerName);
                    if (pos.HasValue)
                        SkillEffectManager.Spawn(skillId, pos.Value.X, pos.Value.Y, forceMap: true);
                }
            }
        };
        _client.RemotePlayerFacing += (playerName, facing) => _mapRenderer.UpdateRemotePlayerFacing(playerName, facing);
        _mapRenderer.OnFacingChanged = facing => _ = _client.SendAsync("player_facing", new { Facing = facing });
        _client.TargetDebuffsUpdated += debuffs =>
        {
            _hudRenderer.UpdateTargetDebuffs(debuffs);
        };
        _client.AttackCooldownUpdated += (skillId, remainingMs, totalMs) =>
        {
            int slot = -1;
            var slots = _inputManager.HotbarSlots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == "skill:" + (_inputManager.GetSkillById(skillId)?.Name ?? ""))
                    slot = i;
            if (slot >= 0 && totalMs > 0)
                _input.HotbarCooldowns[slot] = (DateTime.UtcNow.AddMilliseconds(remainingMs), totalMs);
            if (_input.PendingSkillId == skillId)
            {
                _input.PendingSkillId = null;
                _input.PendingSlot = -1;
                _input.PendingSent = false;
            }
        };
        _client.TargetCleared += _ =>
        {
            _hudRenderer.ClearTarget();
            _mapRenderer.ClearSelection();
        };
        _client.ProjectileSpawned += (id, sx, sy, tx, ty, vt, fm) =>
        {
            ProjectileRenderer.Spawn(id, sx, sy, tx, ty, vt, fm);
        };
        _client.ProjectileHit += (id, hx, hy) =>
        {
            ProjectileRenderer.OnHit(id);
        };
    }

    private void WirePartyEvents()
    {
        _client.PartyUpdated += party =>
        {
            _hudRenderer.UpdateParty(party);
            var myName = _client.PlayerName;
            var groupNames = party.Members
                .Where(m => !string.Equals(m.Name, myName, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Name).ToList();
            _mapRenderer.SetPartyMembers(groupNames);
            _minimap.SetPartyMembers(groupNames);
            var myId = _client.PlayerId;
            if (_lastPartyMemberCount == 0 && party.Members.Count >= 2)
                _chatRenderer.AddMessage(ChatChannel.Party, "Группа", "Группа сформирована!");
            else
                foreach (var m in party.Members)
                    if (m.PlayerId != myId && !_lastPartyMemberIds.Contains(m.PlayerId))
                        _chatRenderer.AddMessage(ChatChannel.Party, "Группа", $"{m.Name} присоединился к группе");
            _lastPartyMemberCount = party.Members.Count;
            _lastPartyMemberIds = party.Members.Select(m => m.PlayerId).ToHashSet();
        };
        _client.PartyDisbanded += () =>
        {
            if (_lastPartyMemberCount > 0)
                _chatRenderer.AddMessage(ChatChannel.Party, "Группа", "Группа распущена.");
            _lastPartyMemberCount = 0;
            _lastPartyMemberIds.Clear();
            _hudRenderer.ClearParty();
            _mapRenderer.SetPartyMembers(Array.Empty<string>());
            _minimap.SetPartyMembers(Array.Empty<string>());
        };
        _client.PartyInviteReceived += (inviterName, _) =>
        {
            _partyInviteWindow.Show(inviterName);
            _windows.BringToFront(_partyInviteWindow);
        };
        _partyInviteWindow.Accepted += inviterName => _ = _client.SendAsync("party_accept", new { InviterName = inviterName });
        _partyInviteWindow.Declined += inviterName => _ = _client.SendAsync("party_decline", new { InviterName = inviterName });
    }

    private void WireStatusEvents()
    {
        _client.StatusUpdated += status =>
        {
            _hudRenderer.UpdateStatus(status);
            _hudDraw.SetAttributePoints(status.AttributePoints);
            _statusWindow.UpdateData(status);
            _input.PlayerGoldCache = status.Gold;
            _skillsWindow.SetPlayerLevel(status.Level);
            _skillsWindow.SetSkillPoints(status.SkillPoints);
            if (_input.LastXp < 0) { _input.LastXp = status.Experience; _input.LastLevel = status.Level; }
            else
            {
                int xpGain = status.Experience - _input.LastXp;
                if (xpGain > 0)
                    _mapRenderer.SpawnFloatingTextAtPlayer($"+{xpGain} XP", new Color(120, 220, 255));
                if (status.Level > _input.LastLevel)
                    _mapRenderer.SpawnFloatingTextAtPlayer("Новый уровень!", Color.Gold, true);
                _input.LastXp = status.Experience;
                _input.LastLevel = status.Level;
            }

            bool hasBuff = status.ActiveDebuffs?.Any(d => d.Type == "AttackSpeedBonus") ?? false;
            if (hasBuff && !SkillEffectManager.HasLooping(SkillIds.Flurry))
                SkillEffectManager.SpawnLooping(SkillIds.Flurry, _mapRenderer.GetPlayerX(), _mapRenderer.GetPlayerY());
            else if (!hasBuff)
                SkillEffectManager.StopLooping(SkillIds.Flurry);

            _mapRenderer.SetSuppressingFireActive(status.ActiveDebuffs?.Any(d => d.Type == "SuppressingFire") ?? false);
        };
        _client.SkillsUpdated += skills =>
        {
            _inputManager.SetSkills(skills);
            _skillsWindow.SetSkillPoints(_client.SkillPoints);
            _skillsWindow.UpdateData(skills);
        };
    }

    private void WireInventoryEvents()
    {
        _client.InventoryUpdated += inv =>
        {
            _inputManager.SetInventory(inv);
            _inventoryWindow.UpdateData(inv);
            _storageWindow.UpdateInventory(inv.Items);
            if (inv.Equipment != null) _equipmentWindow.UpdateData(inv.Equipment);
            string? weaponSub = null;
            bool isTwoHanded = false;
            if (inv.Equipment?.Slots != null)
            {
                if (inv.Equipment.Slots.TryGetValue("rhand", out var wItem) && wItem != null)
                {
                    weaponSub = wItem.WeaponSubtype;
                    isTwoHanded = wItem.TwoHanded;
                }
            }
            _mapRenderer.SetWeaponSubtype(weaponSub);
            _mapRenderer.SetTwoHanded(isTwoHanded);
            string? shieldSub = null;
            string? offWeaponSub = null;
            if (inv.Equipment?.Slots != null && inv.Equipment.Slots.TryGetValue("lhand", out var lHand))
            {
                if (lHand?.Type == "shield" && !Equipment.IsCasterOffhand(lHand))
                    shieldSub = "shield";
                else if (lHand?.Type == "weapon" || Equipment.IsCasterOffhand(lHand))
                    offWeaponSub = lHand?.WeaponSubtype;
            }
            _mapRenderer.SetShieldSubtype(shieldSub);
            _mapRenderer.SetOffHandWeaponSubtype(offWeaponSub);
        };
        _inventoryWindow.NewItemCountChanged += count => _hudDraw.SetNewInventoryCount(count);
        _equipmentWindow.UnequipItem += slot => _ = _client.SendAsync("unequip", new { Slot = slot });
        _equipmentWindow.CloseRequested += () => _equipmentWindow.Visible = false;
        _inventoryWindow.DragStateChanged += item =>
        {
            _equipmentWindow.DraggingType = item?.Type;
            _input.DragOverlayItem = item;
        };
        _equipmentWindow.DragStateChanged += item =>
        {
            _equipmentWindow.DraggingType = item?.Type;
            _input.DragOverlayItem = item;
        };
        _equipmentWindow.IsOverInventory = pt => _inventoryWindow.Contains(pt);
        _lootWindow.DragStateChanged += item => _input.DragOverlayItem = item;
        _lootWindow.DropOnInventory += (pt, item) =>
        {
            if (_inventoryWindow.Contains(pt))
                if (_lootWindow.CorpseId.StartsWith("chest_"))
                    _ = _client.SendAsync("take_chest_loot", new { InstanceId = _lootWindow.CorpseId.Substring(6), ItemIds = new[] { item.Id }, TakeGold = false });
                else
                    _ = _client.SendAsync("take_loot", new { CorpseId = _lootWindow.CorpseId, TakeAll = false, ItemIds = new[] { item.Id }, TakeGold = false });
        };
        _inventoryWindow.DropOnEquip += (pt, item) =>
        {
            if (!_equipmentWindow.Visible) return false;
            if (_equipmentWindow.TryGetSlotAt(pt, item.Type, out var slot) && slot != null)
            {
                _ = _client.SendAsync("equip", new { ItemId = item.Id, TargetSlot = slot });
                return true;
            }
            return false;
        };
        _inventoryWindow.EquipItem += id =>
        {
            _ = _client.SendAsync("equip", new { ItemId = id });
            GameInputHandler.OpenEquipmentBesideInventory(_equipmentWindow, _inventoryWindow, GameMain.Instance!);
        };
        _inventoryWindow.UseItem += id => _ = _client.SendAsync("use_item", new { ItemId = id });
        _inventoryWindow.DeleteItem += id => _ = _client.SendAsync("drop_item", new { ItemId = id });
        _inventoryWindow.SortItems += () =>
        {
            var inv = _client.Inventory;
            if (inv?.Items == null) return;
            int Cat(string t) => t switch
            {
                "weapon" => 0, "armor" => 1, "accessory" => 2,
                "consumable" => 3, "collectible" => 4, "material" => 5, _ => 6
            };
            var order = inv.Items.OrderBy(i => Cat(i.Type)).ThenBy(i => i.Name).Select(i => i.Id).ToList();
            _ = _client.SendAsync("inventory_sort", new { Order = order });
        };
        _statusWindow.AllocateAttribute += attr => _ = _client.SendAsync("allocate_attribute", new { Attribute = attr });
        _statusWindow.ResetAttributes += () => _ = _client.SendAsync("reset_attributes", new { });
    }

    private void WireHotbarEvents()
    {
        _client.HotbarUpdated += slots => _inputManager.UpdateHotbar(slots);
        _inputManager.HotbarActivated += (idx, item) => _input.ActivateHotbarSlot(idx, item, GameMain.Instance!);
    }

    private void WireShopEvents()
    {
        _client.ShopUpdated += data =>
        {
            _shopWindow.UpdateData(data);
            _inventoryWindow.Visible = true;
            _inventoryWindow.ShopMode = true;
            _input.PositionTradeWindows(_shopWindow, _inventoryWindow, GameMain.Instance!);
        };
        _shopWindow.Closed += () =>
        {
            _shopWindow.Visible = false;
            _inventoryWindow.Visible = false;
            _inventoryWindow.ShopMode = false;
        };
        _shopWindow.BuyItem += (id, qty) => _ = _client.SendAsync("buy", new { ItemId = id, Quantity = qty });
        _shopWindow.DragStateChanged += item => _input.DragOverlayItem = item;
        _shopWindow.SellAllTrophies += () => _ = _client.SendAsync("sell_all_trophies", new { });
        _shopWindow.DropOnInventory += (pt, item) =>
        {
            if (!_inventoryWindow.Visible || !_inventoryWindow.Contains(pt)) return false;
            int stock = Math.Max(1, item.Stock);
            int maxAffordable = item.Value > 0
                ? _input.PlayerGoldCache / item.Value : stock;
            int max = Math.Min(stock, Math.Max(1, maxAffordable));
            if (stock > 1 || item.MaxStack > 1)
                _input.OpenQuantity(item.Name, max, item.Value,
                    q => _ = _client.SendAsync("buy", new { ItemId = item.Id, Quantity = q }), true, _quantityDialog, GameMain.Instance!);
            else
                _ = _client.SendAsync("buy", new { ItemId = item.Id, Quantity = 1 });
            return true;
        };
        _shopWindow.PendingBuy += (item, max) =>
        {
            int stock = Math.Max(1, item.Stock);
            int maxAffordable = item.Value > 0
                ? _input.PlayerGoldCache / item.Value : stock;
            int realMax = Math.Min(max, Math.Max(1, maxAffordable));
            _input.OpenQuantity(item.Name, realMax, item.Value,
                q => _ = _client.SendAsync("buy", new { ItemId = item.Id, Quantity = q }), true, _quantityDialog, GameMain.Instance!);
        };
        _inventoryWindow.DropOnSell += (pt, item) =>
        {
            if (!_shopWindow.Visible || !_shopWindow.Contains(pt)) return false;
            _ = _client.SendAsync("sell", new { ItemId = item.Id, Quantity = 1 });
            return true;
        };
        _inventoryWindow.SellItem += (id, qty) => _ = _client.SendAsync("sell", new { ItemId = id, Quantity = qty });
        _inventoryWindow.PendingSell += (item, max) =>
            _input.OpenQuantity(item.Name, max, 1, q => _ = _client.SendAsync("sell", new { ItemId = item.Id, Quantity = q }), true, _quantityDialog, GameMain.Instance!);
        _inventoryWindow.PendingDrop += (item, max) =>
            _input.OpenQuantity(item.Name, max, 1, q => _ = _client.SendAsync("drop_item", new { ItemId = item.Id, Quantity = q }), false, _quantityDialog, GameMain.Instance!);
    }

    private void WireTradeEvents()
    {
        _client.TradeOpened += data =>
        {
            var inv = data.YourInventory ?? new List<TradeItemData>();
            var grouped = inv.GroupBy(i => i.Id).Select(gr => $"{gr.First().Name} x{gr.Count()}").ToList();
            Logger.Action($"ОБМЕН: получен trade_open с '{data.OtherName}', предметов в инвентаре={inv.Count} (уникальных={grouped.Count}), золото={data.YourGold}");
            foreach (var line in grouped) Logger.Debug($"ОБМЕН: инвентарь аккаунта -> {line}");
            _tradeWindow.Open(data);
            _windows.BringToFront(_tradeWindow);
        };
        _client.TradeOfferUpdated += offer =>
        {
            if (offer.IsFromMe) _tradeWindow.UpdateMyOffer(offer);
            else _tradeWindow.UpdateTheirOffer(offer);
        };
        _client.TradeConfirmUpdated += conf => _tradeWindow.UpdateConfirm(conf);
        _client.TradeCompleted += done =>
        {
            _tradeWindow.HandleComplete(done);
            if (!string.IsNullOrEmpty(done.Message))
                _chatRenderer.AddMessage(ChatChannel.System, "Обмен", done.Message);
        };
        _client.TradeClosed += msg =>
        {
            _tradeWindow.Visible = false;
            if (!string.IsNullOrEmpty(msg))
                _chatRenderer.AddMessage(ChatChannel.System, "Обмен", msg);
        };
        _tradeWindow.OfferChanged += (entries, gold) =>
            _ = _client.SendAsync("trade_offer", new { Entries = entries, Gold = gold });
        _tradeWindow.RequestQuantity += (itemName, max, defaultQty, onConfirm) =>
            _input.OpenQuantity(itemName, max, 0, onConfirm, false, _quantityDialog, GameMain.Instance!);
        _tradeWindow.ConfirmRequested += () => _ = _client.SendAsync("trade_confirm", null);
        _tradeWindow.CancelRequested += () => _ = _client.SendAsync("trade_cancel", null);
    }

    private void WireQuestEvents()
    {
        _questBoardWindow.TakeQuest += id => _ = _client.SendAsync("take_quest", new { QuestId = id });
        _questBoardWindow.CompleteQuest += id => _ = _client.SendAsync("complete_quest", new { QuestId = id });
        _questBoardWindow.AbandonQuest += id => _ = _client.SendAsync("abandon_quest", new { QuestId = id });
        _questLogWindow.AbandonQuest += id => _ = _client.SendAsync("abandon_quest", new { QuestId = id });
        _client.QuestLogUpdated += (available, active) =>
        {
            _questLogWindow.UpdateActive(active);
            _activeQuests = active ?? new List<QuestInfo>();
            _questBoardWindow.UpdateData(available, _activeQuests);
        };
        _client.BoardOpened += () =>
        {
            _questBoardWindow.Visible = true;
            GameInputHandler.CenterWindow(_questBoardWindow, GameMain.Instance!);
        };
    }

    private void WireSkillsEvents()
    {
        _skillsWindow.UseSkill += id =>
        {
            if (_input.PendingSkillId == id)
            {
                _input.PendingSkillId = null;
                _input.PendingSlot = -1;
                _input.PendingSent = false;
                _ = _client.SendAsync("cancel_skill", new { });
                return;
            }
            int slot = -1;
            var slots = _inputManager.HotbarSlots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == "skill:" + (_inputManager.GetSkillById(id)?.Name ?? ""))
                    slot = i;
            _input.PendingSkillId = id;
            _input.PendingSlot = slot;
            _input.PendingSent = false;
        };
        _skillsWindow.SkillDragStateChanged += skill => _input.DragOverlaySkill = skill;
        _skillsWindow.SkillDragEnded += () => _input.HandleSkillDragEnd(GameMain.Instance!);
        _skillsWindow.LearnSkill += skillId =>
            _ = _client.SendAsync("allocate_skill", new { SkillId = skillId });
        _skillsWindow.ResetSkills += () =>
            _ = _client.SendAsync("reset_skills", new { });
    }

    private void WireLootEvents()
    {
        _client.LootReceived += (corpseId, monsterName, damagePct, items, gold) =>
        {
            var lootItems = items.Select(item => new LostAndDivine.ClientMonoGame.Networking.LootItemInfo
            {
                Id = item.Id, Name = item.Name, Type = item.Type, WeaponSubtype = item.WeaponSubtype, Value = item.Value, Description = item.Description
            }).ToList();
            _lootWindow.Setup(corpseId, monsterName, damagePct, lootItems, gold);
            _lootWindow.X = Math.Max(0, (GameMain.Instance!.Graphics.PreferredBackBufferWidth - _lootWindow.Width) / 2);
            _lootWindow.Y = Math.Max(0, (GameMain.Instance!.Graphics.PreferredBackBufferHeight - _lootWindow.Height) / 2);
        };
        _lootWindow.TakeItem += item =>
        {
            if (_lootWindow.CorpseId.StartsWith("chest_"))
                _ = _client.SendAsync("take_chest_loot", new { InstanceId = _lootWindow.CorpseId.Substring(6), ItemIds = new[] { item.Id }, TakeGold = false });
            else
                _ = _client.SendAsync("take_loot", new { CorpseId = _lootWindow.CorpseId, TakeAll = false, ItemIds = new[] { item.Id }, TakeGold = false });
            _lootWindow.RemoveItem(item);
        };
        _lootWindow.TakeLoot += (corpseId, takeAll, ids, takeGold) =>
        {
            if (corpseId.StartsWith("chest_"))
                _ = _client.SendAsync("take_chest_loot", new { InstanceId = corpseId.Substring(6), ItemIds = ids, TakeGold = takeGold });
            else
                _ = _client.SendAsync("take_loot", new { CorpseId = corpseId, TakeAll = takeAll, ItemIds = ids, TakeGold = takeGold });
        };
    }

    private void WireMapInteractionEvents()
    {
        _mapRenderer.MoveRequested += (x, y) =>
        {
            Logger.Action($"Движение в клетку ({x}, {y})");
            _ = _client.SendAsync("move_to", new { X = x, Y = y });
        };
        _mapRenderer.InteractRequested += (entity, x, y) =>
        {
            Logger.Action($"Взаимодействие с {entity.Type} '{entity.Name}' ({x}, {y})");
            if (entity.Type == "corpse" && entity.Id != null)
                _ = _client.SendAsync("loot_corpse", new { CorpseId = entity.Id });
            else if (entity.Type == "player")
                _ = _client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, PlayerId = entity.Id?.ToString() });
            else
                _ = _client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, MonsterId = entity.Id?.ToString() });
        };
        _mapRenderer.EntityPickRequested += (entities, mapX, mapY) =>
        {
            var filtered = entities.Where(e => e.Type != "corpse" || e.Id == null || !_lootedCorpses.Contains(e.Id)).ToList();
            if (filtered.Count == 0) return;
            if (filtered.Count == 1)
            {
                var single = filtered[0];
                Logger.Action($"Выбрана сущность: {single.Type} '{single.Name}' ({mapX}, {mapY})");
                if (single.Type == "corpse" && single.Id != null)
                {
                    _lootedCorpses.Add(single.Id);
                    _ = _client.SendAsync("loot_corpse", new { CorpseId = single.Id });
                }
                else if (single.Type == "player")
                    _ = _client.SendAsync("interact_target", new { Type = single.Type, X = mapX, Y = mapY, PlayerId = single.Id?.ToString() });
                else
                    _ = _client.SendAsync("interact_target", new { Type = single.Type, X = mapX, Y = mapY, MonsterId = single.Id?.ToString() });
                return;
            }
            _entityPickDialog.Setup(filtered, mapX, mapY);
            _entityPickDialog.X = Math.Max(0, (GameMain.Instance!.Graphics.PreferredBackBufferWidth - _entityPickDialog.Width) / 2);
            _entityPickDialog.Y = Math.Max(0, (GameMain.Instance!.Graphics.PreferredBackBufferHeight - _entityPickDialog.Height) / 2);
            _windows.BringToFront(_entityPickDialog);
        };
        _entityPickDialog.OnPick += (entity, x, y) =>
        {
            Logger.Action($"Выбрана сущность: {entity.Type} '{entity.Name}' ({x}, {y})");
            if (entity.Type == "corpse" && entity.Id != null)
            {
                _lootedCorpses.Add(entity.Id);
                _ = _client.SendAsync("loot_corpse", new { CorpseId = entity.Id });
            }
            else if (entity.Type == "player")
                _ = _client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, PlayerId = entity.Id?.ToString() });
            else
                _ = _client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, MonsterId = entity.Id?.ToString() });
        };
        _mapRenderer.SelectionChanged += entity =>
        {
            if (entity == null)
            {
                _hudRenderer.UpdateTargetDebuffs(null);
            }
            else if (entity.Type == "player" && entity.Id != null)
            {
                _hudRenderer.UpdateTargetDebuffs(null);
                _ = _client.SendAsync("select_target", new { PlayerId = entity.Id });
            }
            else if (entity.Type == "monster" && entity.Id != null)
            {
                var map = _client.CurrentMap;
                var mon = map?.Monsters.FirstOrDefault(m => m.Id.ToString() == entity.Id);
                if (mon?.ActiveDebuffTypes is { Count: > 0 } types)
                {
                    var infos = types.Select(t => new DebuffInfo
                    {
                        Type = t, DisplayName = DebuffDisplayName(t), Description = "",
                        RemainingMs = 0, DurationMs = 0
                    }).ToList();
                    _hudRenderer.UpdateTargetDebuffs(infos);
                }
                else
                {
                    _hudRenderer.UpdateTargetDebuffs(null);
                }
            }
            else
            {
                _hudRenderer.UpdateTargetDebuffs(null);
            }
        };
    }

    private void WireSettingsEvents()
    {
        _settingsWindow.ApplyRequested += ApplySettings;
        _settingsWindow.LogoutRequested += () =>
        {
            _settingsWindow.Visible = false;
            _logoutConfirmWindow.ResetTimer();
            _logoutConfirmWindow.X = (GameMain.Instance!.Graphics.PreferredBackBufferWidth - _logoutConfirmWindow.Width) / 2;
            _logoutConfirmWindow.Y = (GameMain.Instance!.Graphics.PreferredBackBufferHeight - _logoutConfirmWindow.Height) / 2;
            _logoutConfirmWindow.Visible = true;
            _windows.BringToFront(_logoutConfirmWindow);
        };
        _logoutConfirmWindow.Confirmed += () =>
        {
            _ = _client.SendAsync("logout", new { });
            GameMain.Instance!.Network.Disconnect();
            GameMain.Instance.ShowLogin();
        };
        _logoutConfirmWindow.Cancelled += () => { };
    }

    private void WireMailEvents()
    {
        _mailWindow.InboxRequested += () =>
        {
            _ = _client.SendAsync("mail", new { Action = "inbox" });
        };
        _mailWindow.OutboxRequested += () =>
        {
            _ = _client.SendAsync("mail", new { Action = "outbox" });
        };
        _mailWindow.SendRequested += (recipient, subject, body, gold, attachments) =>
        {
            _ = _client.SendAsync("mail", new
            {
                Action = "send",
                RecipientName = recipient,
                Subject = subject,
                Body = body,
                GoldAmount = gold,
                Attachments = attachments.Select(a => new
                {
                    a.TemplateId, a.Quantity, a.WeaponSubtype, a.HealAmount, a.RestoreMana
                }).ToList()
            });
        };
        _mailWindow.ReadRequested += id =>
        {
            _ = _client.SendAsync("mail", new { Action = "read", MailId = id });
        };
        _mailWindow.DeleteRequested += id =>
        {
            _ = _client.SendAsync("mail", new { Action = "delete", MailId = id });
        };
        _mailWindow.TakeAttachmentRequested += id =>
        {
            _ = _client.SendAsync("mail", new { Action = "take", MailId = id });
        };
        _mailWindow.AttachmentRequested += () =>
        {
            var items = _client.Inventory?.Items?
                .Where(i => i.Type != "gold")
                .ToList() ?? new();
            _mailAttachmentWindow.Open(items, _mailWindow.ComposeAttachments);
            GameInputHandler.CenterWindow(_mailAttachmentWindow, GameMain.Instance!);
            _windows.BringToFront(_mailAttachmentWindow);
        };
        _mailAttachmentWindow.ConfirmRequested += () =>
        {
            _mailWindow.SetComposeAttachments(_mailAttachmentWindow.Attachments);
        };
        _mailAttachmentWindow.RequestQuantity += (name, max, defaultQty, onConfirm) =>
            _input.OpenQuantity(name, max, 0, onConfirm, false, _quantityDialog, GameMain.Instance!);
        _client.MailListReceived += (folder, mails) =>
        {
            if (folder == "inbox")
                _mailWindow.SetInbox(mails);
            else if (folder == "outbox")
                _mailWindow.SetOutbox(mails);
        };
        _client.MailDetailReceived += mail =>
        {
            _mailWindow.UpdateMail(mail);
        };
        _client.MailResultReceived += (ok, msg) =>
        {
            if (!string.IsNullOrEmpty(msg))
                _chatRenderer.AddMessage(ChatChannel.System, "Почта", msg);
            if (ok && _mailWindow.SelectedMailId > 0)
                _ = _client.SendAsync("mail", new { Action = "read", MailId = _mailWindow.SelectedMailId });
        };
        _client.MailUnreadReceived += count =>
        {
            _hudDraw.SetMailUnreadCount(count);
            _mailWindow.RefreshInboxIfOpen();
        };
    }

    private void WireStorageEvents()
    {
        _client.StorageOpened += data =>
        {
            var invItems = _client.Inventory?.Items ?? new List<Item>();
            _storageWindow.UpdateData(invItems, data.Items, data.Slots);
            GameInputHandler.CenterWindow(_storageWindow, GameMain.Instance!);
            _windows.BringToFront(_storageWindow);
        };
        _client.StorageUpdated += data =>
        {
            var invItems = _client.Inventory?.Items ?? new List<Item>();
            _storageWindow.UpdateData(invItems, data.Items, data.Slots);
        };
        _storageWindow.DepositItem += (id, qty) => _ = _client.SendAsync("storage_deposit", new { ItemId = id, Quantity = qty });
        _storageWindow.WithdrawItem += (id, qty) => _ = _client.SendAsync("storage_withdraw", new { ItemId = id, Quantity = qty });
        _storageWindow.PendingDeposit += (item, max) =>
            _input.OpenQuantity(item.Name, max, 0, q => _ = _client.SendAsync("storage_deposit", new { ItemId = item.Id, Quantity = q }), false, _quantityDialog, GameMain.Instance!);
        _storageWindow.PendingWithdraw += (item, max) =>
            _input.OpenQuantity(item.Name, max, 0, q => _ = _client.SendAsync("storage_withdraw", new { ItemId = item.Id, Quantity = q }), false, _quantityDialog, GameMain.Instance!);
    }

    private void WireDialogueEvents()
    {
        _client.DialogueOpened += (npcId, speaker, text, choices) =>
        {
            _dialogueWindow.SetNode(speaker, text, choices);
            GameInputHandler.CenterWindow(_dialogueWindow, GameMain.Instance!);
            _input.PushWindow(_dialogueWindow);
        };
        _client.DialogueClosed += () =>
        {
            _dialogueWindow.CloseDialogue();
        };
        _dialogueWindow.DialogueClosed += () =>
        {
            _ = _client.SendAsync("dialogue_choice", new { ChoiceIndex = -1 });
        };
        _dialogueWindow.ChoiceSelected += index =>
        {
            _ = _client.SendAsync("dialogue_choice", new { ChoiceIndex = index });
        };
    }

    private void WireDeathEvents()
    {
        _client.PlayerDeathReceived += lostGold =>
        {
            _deathWindow.Activate(lostGold);
            GameInputHandler.CenterWindow(_deathWindow, GameMain.Instance!);
            _windows.BringToFront(_deathWindow);
            _mapRenderer.SetPlayerDead(true);
        };
        _deathWindow.ReviveRequested += () =>
        {
            _ = _client.SendAsync("revive", null);
            _mapRenderer.SetPlayerDead(false);
        };
        _client.StatusUpdated += _ =>
        {
            if (!_client.IsDead && _deathWindow.Visible)
            {
                _deathWindow.Deactivate();
                _mapRenderer.SetPlayerDead(false);
            }
        };
    }

    private void WireTradeRequestEvents()
    {
        _client.TradeRequestReceived += inviterName =>
        {
            _tradeRequestWindow.Show(inviterName);
            _windows.BringToFront(_tradeRequestWindow);
        };
        _tradeRequestWindow.Accepted += inviterName => _ = _client.SendAsync("trade_accept", new { InviterName = inviterName });
        _tradeRequestWindow.Declined += inviterName => _ = _client.SendAsync("trade_decline", new { InviterName = inviterName });
    }

    private void WireChangelogEvents()
    {
        _client.ChangelogReceived += data => ShowChangelog(data);
        _settingsWindow.OpenChangelog += () => ShowChangelog(_client.LastChangelog);
    }

    private void ShowChangelog(ChangelogData? data)
    {
        if (data?.Entries == null || data.Entries.Count == 0) return;

        _changelogWindow.SetData(data);
        var viewport = GameMain.Instance?.GraphicsDevice.Viewport;
        if (viewport.HasValue)
        {
            _changelogWindow.X = (viewport.Value.Width - _changelogWindow.Width) / 2;
            _changelogWindow.Y = (viewport.Value.Height - _changelogWindow.Height) / 2;
        }
        _changelogWindow.Visible = true;
        _windows.BringToFront(_changelogWindow);
    }

    private void RegisterWindows()
    {
        _windows.Add(_inventoryWindow);
        _windows.Add(_statusWindow);
        _windows.Add(_skillsWindow);
        _windows.Add(_equipmentWindow);
        _windows.Add(_questLogWindow);
        _windows.Add(_shopWindow);
        _windows.Add(_lootWindow);
        _windows.Add(_questBoardWindow);
        _windows.Add(_tradeWindow);
        _windows.Add(_quantityDialog);
        _windows.Add(_entityPickDialog);
        _windows.Add(_settingsWindow);
        _windows.Add(_logoutConfirmWindow);
        _windows.Add(_partyInviteWindow);
        _windows.Add(_tradeRequestWindow);
        _windows.Add(_socialWindow);
        _windows.Add(_deathWindow);
        _windows.Add(_dialogueWindow);
        _windows.Add(_mailWindow);
        _windows.Add(_mailAttachmentWindow);
        _windows.Add(_storageWindow);
        _windows.Add(_changelogWindow);
    }

    /// <summary>
    /// Self-heal тайлов: если map_update пришёл без TileData и у рендера нет валидных
    /// тайлов для текущей зоны (гонка при логине — первый map_update мог прийти до
    /// создания GameScreen), запрашиваем тайлы у сервера. Повтор не чаще раза в 3 сек.
    /// </summary>
    private void RequestTilesIfNeeded(WorldMap map)
    {
        if (_mapRenderer.HasValidTiles(map.Width, map.Height)) return;
        if (_tileRequestedZone == map.ZoneId && DateTime.UtcNow - _tileRequestTime < TimeSpan.FromSeconds(3))
            return;
        _tileRequestedZone = map.ZoneId;
        _tileRequestTime = DateTime.UtcNow;
        Logger.Info($"TileRequest: запрашиваю тайлы зоны '{map.ZoneId}' ({map.Width}x{map.Height})");
        _ = GameMain.Instance!.Client.SendAsync("tile_request", null);
    }

    private void ApplySettings()
    {
        var g = GameMain.Instance!.Graphics;
        var (rw, rh) = _settingsWindow.SelectedResolution;
        g.PreferredBackBufferWidth = rw;
        g.PreferredBackBufferHeight = rh;
        switch (_settingsWindow.SelectedMode)
        {
            case "fullscreen":
                g.IsFullScreen = true;
                GameMain.Instance.Window.IsBorderless = false;
                break;
            case "borderless":
                g.IsFullScreen = true;
                GameMain.Instance.Window.IsBorderless = true;
                break;
            default:
                g.IsFullScreen = false;
                GameMain.Instance.Window.IsBorderless = false;
                break;
        }
        g.ApplyChanges();
        var s = SettingsManager.Load();
        s.Width = rw;
        s.Height = rh;
        s.Mode = _settingsWindow.SelectedMode;
        s.Save();
    }

    public void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        var client = GameMain.Instance!.Client;
        var game = GameMain.Instance!;

        SkillEffectManager.Update(dtMs: (float)gameTime.ElapsedGameTime.TotalMilliseconds);
        HazardRenderer.Update(dtMs: (float)gameTime.ElapsedGameTime.TotalMilliseconds);

        _input.HandleHotbarDrop(mouse, game);
        bool mouseOverAnyWindowBefore = _windows.IsMouseOverVisibleWindow(mouse.X, mouse.Y);
        _windows.Update(gameTime, keyboard, mouse);

        bool settingsOpen = _settingsWindow.Visible;
        bool mouseOverAnyWindow = mouseOverAnyWindowBefore || _windows.IsMouseOverVisibleWindow(mouse.X, mouse.Y);

        bool mailTyping = _mailWindow.IsInputActive;
        bool shopEscConsumed = _shopWindow.Visible && _shopWindow.ConsumesEscape;
        if (!_chatRenderer.IsTyping && !mailTyping && !shopEscConsumed)
        {
            bool escPressed = keyboard.IsKeyDown(Keys.Escape) && _input.PrevKeyboard.IsKeyUp(Keys.Escape);
            if (escPressed && _shopWindow.Visible && _input.WindowStack.Count == 0)
                _shopWindow.CloseShop();
            else
                _input.HandleEscape(keyboard, _settingsWindow, game);
        }

        if (settingsOpen && _settingsWindow.Visible)
        {
            _input.PrevKeyboard = keyboard; _input.PrevMouse = mouse; return;
        }

        _input.HandlePendingTrade(game);
        _input.HandleHotbarClick(mouse, mouseOverAnyWindow, game);

        // Chat clicks
        {
            int hotbarW2 = (int)(game.Graphics.PreferredBackBufferWidth * 0.35f);
            int hotbarLeft2 = (game.Graphics.PreferredBackBufferWidth - hotbarW2) / 2;
            int chatX2 = 8;
            int chatW2 = hotbarLeft2 - chatX2 - 8;
            int chatH2 = 180;
            int chatY2 = game.Graphics.PreferredBackBufferHeight - chatH2 - 8;
            var chatRect = new Rectangle(chatX2, chatY2, chatW2, chatH2);
            bool chatPressed = mouse.LeftButton == ButtonState.Pressed && _input.PrevMouse.LeftButton == ButtonState.Released;
            bool chatHandled = _chatRenderer.HandleClick(mouse.X, mouse.Y, chatX2, chatY2, chatW2, chatH2, chatPressed);
            if (chatHandled) mouseOverAnyWindow = true;

            if (chatRect.Contains(mouse.X, mouse.Y))
            {
                mouseOverAnyWindow = true;
                int scroll = mouse.ScrollWheelValue - _input.PrevMouse.ScrollWheelValue;
                if (scroll != 0) _chatRenderer.HandleScroll(scroll > 0 ? -3 : 3, chatH2 - 54);
            }
        }

        _input.HandlePendingSkill(game);
        _input.HandleChatInput(keyboard, game);
        if (!_chatRenderer.IsTyping && !mailTyping)
            _input.HandleWindowToggles(keyboard, game,
                _inventoryWindow, _statusWindow, _skillsWindow, _equipmentWindow,
                _questLogWindow, _socialWindow, _settingsWindow);
        if (!_chatRenderer.IsTyping && !mailTyping)
            _inputManager.HandleHotbarKeys(keyboard, _input.PrevKeyboard);

        // Icon clicks
        bool clickedIcon = false;
        if (_hudDraw.IconRects.Length >= 6 &&
            mouse.LeftButton == ButtonState.Pressed && _input.PrevMouse.LeftButton == ButtonState.Released)
        {
            foreach (var r in _hudDraw.IconRects)
            {
                if (r.Contains(mouse.X, mouse.Y)) { clickedIcon = true; break; }
            }
            _input.HandleIconClick(mouse, mouseOverAnyWindow, game,
                _inventoryWindow, _statusWindow, _skillsWindow, _equipmentWindow,
                _socialWindow, _questLogWindow, _settingsWindow, _mailWindow, _hudDraw.IconRects);
        }
        mouseOverAnyWindow |= clickedIcon;

        // Party buttons
        bool partyHandled = _input.HandlePartyButtons(mouse, mouseOverAnyWindow, game,
            _hudDraw.InvitePartyRect, _hudDraw.TradePlayerRect,
            _hudDraw.PartyLeaveRect, _hudDraw.PartyDisbandRect);
        mouseOverAnyWindow |= partyHandled;

        bool overHotbar = _input.HitHotbarSlot(mouse.X, mouse.Y, game) >= 0;
        bool overIconBar = _hudDraw.IconRects.Length > 0 && _hudDraw.IconRects.Any(r => r.Contains(mouse.X, mouse.Y));

        var mmRect = _minimap.GetPanelRect(game.Graphics.PreferredBackBufferWidth);
        bool overMinimap = mmRect.Contains(mouse.X, mouse.Y);

        bool overLeaveBtn = _hudDraw.InstanceLeaveRect.Contains(mouse.X, mouse.Y);
        if (overLeaveBtn && _input.PrevMouse.LeftButton == ButtonState.Released && mouse.LeftButton == ButtonState.Pressed)
            _ = client.SendAsync("leave_instance", new { });

        if (!mouseOverAnyWindow && !overHotbar && !overMinimap)
        {
            int scroll = mouse.ScrollWheelValue - _input.PrevMouse.ScrollWheelValue;
            if (scroll != 0) _mapRenderer.ChangeZoom(scroll > 0 ? 0.15f : -0.15f);
        }
        if (!mouseOverAnyWindow && !overHotbar && !overMinimap)
            _inputManager.HandleMapClick(mouse, _input.PrevMouse, _mapRenderer);
        if (!mouseOverAnyWindow && !overHotbar && !overMinimap)
            _inputManager.HandleMapRightClick(mouse, _input.PrevMouse, _mapRenderer);

        // Compute cursor type for current frame
        {
            int w2 = game.Graphics.PreferredBackBufferWidth;
            int h2 = game.Graphics.PreferredBackBufferHeight;
            int topH2 = 0;
            bool overMap = !mouseOverAnyWindow && !overHotbar && !overIconBar && !overMinimap && mouse.Y >= topH2;
            string ct = "main";
            if (overMap)
            {
                int areaW = w2;
                int areaH = h2 - topH2;
                ct = _mapRenderer.GetCursorType(mouse.X, mouse.Y, areaW, areaH);
            }
            else
            {
                _mapRenderer.ClearHoverTile();
            }
            CurrentCursorType = ct;
        }

        _input.PrevKeyboard = keyboard;
        _input.PrevMouse = mouse;
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        int w = GameMain.Instance!.Graphics.PreferredBackBufferWidth;
        int h = GameMain.Instance!.Graphics.PreferredBackBufferHeight;
        int topH = 0;

        _mapRenderer.Draw(spriteBatch, 0, topH, w, h - topH);
        if (_mapRenderer.IsMapLoaded)
        {
        _mapRenderer.DrawSkillEffects(spriteBatch, 0, topH, w, h - topH);
        _minimap.SetViewBounds(_mapRenderer.GetViewBounds());
        // Координаты игрока берём из авторитетной клетки (map.Players), а не из центра камеры:
        // камера плавно догоняет игрока и показывает отставание на шаг.
        _minimap.Draw(spriteBatch, _minimap.GetPanelRect(w), _mapRenderer.GetPlayerX(), _mapRenderer.GetPlayerY());
        _hudDraw.DrawInstanceLeaveButton(spriteBatch, w);
        _hudDraw.DrawQuestTracker(spriteBatch, w, _activeQuests);
        float panelH = _hudRenderer.DrawPlayerStatusPanel(spriteBatch, 8, topH + 8);
        float debuffH = _hudRenderer.DrawPlayerDebuffs(spriteBatch, 8, topH + 8 + panelH + 4, w - 16);
        _hudRenderer.SetSelectedEntity(_mapRenderer.GetSelectedEntity());
        _hudRenderer.DrawTargetBar(spriteBatch, w);
        _hudRenderer.DrawTargetDebuffs(spriteBatch, w, 64 + 18 + 4);
        _hudRenderer.DrawZoneIndicator(spriteBatch, w);
        _hudDraw.DrawTargetButtons(spriteBatch, w, GameMain.Instance!);
        int partyY = topH + 8 + (int)panelH + 4 + (int)debuffH + 4;
        _hudDraw.DrawPartyPanel(spriteBatch, 8, partyY, 240, GameMain.Instance!);
        _hudRenderer.DrawDebuffTooltip(spriteBatch);

        // Hotbar
        int hotbarH = 64;
        int hotbarW = (int)(w * 0.35f);
        int hotbarX = (w - hotbarW) / 2;
        int hotbarY = h - hotbarH - 8;
        var hotbarIcons = new Texture2D?[10];
        var hotbarCounts = new int[10];
        var cdRemain = new int[10];
        var cdTotal = new int[10];
        int hoverSlot = _input.HitHotbarSlot(Mouse.GetState().X, Mouse.GetState().Y, GameMain.Instance!);
        int highlightSlot = _input.PendingSlot;
        for (int i = 0; i < 10; i++)
        {
            hotbarIcons[i] = _inputManager.GetHotbarIcon(i);
            hotbarCounts[i] = _inputManager.GetHotbarItemCount(i);
            if (_input.HotbarCooldowns.TryGetValue(i, out var cd))
            {
                int remMs = (int)(cd.End - DateTime.UtcNow).TotalMilliseconds;
                if (remMs <= 0) _input.HotbarCooldowns.Remove(i);
                else { cdRemain[i] = remMs; cdTotal[i] = cd.Total; }
            }
        }
        _hudRenderer.DrawHotbar(spriteBatch, hotbarX, hotbarY, hotbarW, hotbarH, _inputManager.HotbarSlots, hotbarIcons, hotbarCounts,
            hoverSlot, highlightSlot, cdRemain, cdTotal);
        _hudRenderer.DrawHotbarTooltip(spriteBatch);

        // Chat
        int hotbarLeft = (w - hotbarW) / 2;
        int chatX = 8;
        int chatW = hotbarLeft - chatX - 8;
        int chatH = 180;
        int chatY = h - chatH - 8;
        _chatRenderer.Draw(spriteBatch, chatX, chatY, chatW, chatH);

        // Icon bar
        _hudDraw.LayoutIconBar(w, h);
        _hudDraw.DrawIconBar(spriteBatch);
        }

        // Settings overlay
        if (_settingsWindow.Visible)
            spriteBatch.Draw(SpriteCache.Pixel, new Rectangle(0, 0, w, h), new Color(0, 0, 0, 140));

        _windows.Draw(gameTime, spriteBatch);

        // Drag overlay
        int dragHitIdx = _input.HitHotbarSlot(Mouse.GetState().X, Mouse.GetState().Y, GameMain.Instance!);
        _hudDraw.DrawDragOverlay(spriteBatch, _input.DragOverlayItem, _input.DragOverlaySkill, dragHitIdx, GameMain.Instance!);

        spriteBatch.End();
    }

    public static string? CurrentCursorType { get; set; }

    public void Dispose()
    {
        _client.UnsubscribeAll(this);
        _socialWindow.Unsubscribe();
    }

    private static string DebuffDisplayName(string type) => type switch
    {
        "Returning"       => "Возвращение",
        "Stun"            => "Оглушение",
        "Root"            => "Обездвижен",
        "Slow"            => "Замедление",
        "Dot"             => "Отравление",
        "SuppressingFire" => "Подавл. огонь",
        "ArmorPenetration"=> "Пробитие брони",
        "DamageBonus"     => "Усиление урона",
        "DamageReduction" => "Ослабление",
        "AccuracyReduction"=> "Дезориентация",
        "AttackSpeedBonus"=> "Проворность",
        "CleaveReady"     => "Рассечение",
        "DualWieldBonus"  => "Двойное оружие",
        _                 => type
    };
}
