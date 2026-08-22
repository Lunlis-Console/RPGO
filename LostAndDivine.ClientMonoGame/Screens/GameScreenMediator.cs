using System;
using System.Collections.Generic;
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

public class GameScreenMediator
{
    private readonly GameClient _client;
    private readonly WindowManager _windows;
    private readonly GameScreen _owner;

    // Wiring-only state (not referenced by GameScreen.Update/Draw/Dispose/RegisterWindows)
    private PartyInfo? _partyInfo;
    private int _lastPartyMemberCount;
    private DateTime _tileRequestTime;

    public GameScreenMediator(GameClient client, WindowManager windows, GameScreen owner)
    {
        _client = client;
        _windows = windows;
        _owner = owner;
    }

    public void WireAll()
    {
        WireMapEvents();
        WireCombatEvents();
        WirePartyEvents();
        WireInstanceEvents();
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
    }

    private void WireMapEvents()
    {
        _client.SectorsReloaded += () =>
        {
            _owner.WorldMapPreloaded = false;
            // Карта мира показывает только открытый мир: сбрасываем кэш только в нём.
            // Вернувшись в main после релоада, игрок получит свежий слепок из PreloadWorldMap.
            if (_owner.CurrentZoneId == BalanceStatic.MainZoneId)
            {
                _owner.WorldMapWindow.ResetSectors();
                _owner.RequestedSectors.Clear();
            }
            PreloadWorldMap();
        };
        _client.MapUpdated += map =>
        {
            _owner.MapRenderer.SetMap(map);
            _owner.MapRenderer.SetPlayerName(_client.PlayerName);
            _owner.MapRenderer.SetPlayerLevel(_client.PlayerLevel);
            _owner.Minimap.SetPlayerName(_client.PlayerName);
            _owner.Minimap.SetMap(map);
            _owner.HudRenderer.UpdateInstanceTimer(map.InstanceExpiresAtUtcMs);
            _owner.CurrentZoneId = map.ZoneId;
            if (map.TileData != null && map.TileData.Length > 0)
            {
                _owner.MapRenderer.SetTileData(map.TileData, map.Width, map.Height, map.TilesetId ?? map.ZoneId, map.TileWidth);
                _owner.Minimap.SetTileData(map.TileData, map.Width, map.Height);
            }
            else if (!_owner.MapRenderer.HasValidTiles(map.Width, map.Height))
            {
                if (map.ZoneId == BalanceStatic.MainZoneId)
                {
                    RequestSectorsAround();
                    PreloadWorldMap();
                }
                else
                    RequestTilesIfNeeded(map);
            }
            if (map.ObstacleData != null && map.ObstacleData.Length > 0)
            {
                _owner.MapRenderer.SetObstacleData(map.ObstacleData, map.Width, map.Height);
                _owner.Minimap.SetObstacleData(map.ObstacleData, map.Width, map.Height);
            }
            if (map.ObjectData != null && map.ObjectData.Length > 0)
            {
                _owner.MapRenderer.SetObjectLayerData(map.ObjectData, map.Width, map.Height,
                    map.ObjectTilesetId ?? "", map.ObjectTileWidth > 0 ? map.ObjectTileWidth : map.TileWidth);
            }
            foreach (var p in map.Players)
            {
                if (p.Name == _client.PlayerName) continue;
                _owner.MapRenderer.UpdateRemotePlayer(p.Name, p.Facing, p.WeaponSubtype, p.OffWeaponSubtype, p.ShieldSubtype, p.IsTwoHanded, p.IsDead, p.X, p.Y);
            }
            HazardRenderer.Sync(map.Hazards);
        };
        _client.EntityStateReceived += state => _owner.MapRenderer.MergeEntityState(state);
        _client.ZoneChanged += (zoneId, zoneName, pvp) =>
        {
            _owner.CurrentZoneId = zoneId;
            _owner.MapRenderer.ClearMap();
            _owner.MapRenderer.ClearSectors();
            _owner.Minimap.ClearSectors();
            _owner.RequestedSectors.Clear();
            if (!zoneId.StartsWith("instance:"))
                _owner.HudRenderer.UpdateInstanceTimer(null);
        };
        _client.TileDataReceived += (data, w, h, tilesetId, tileSize) =>
        {
            _owner.MapRenderer.SetTileData(data, w, h, tilesetId, tileSize);
            _owner.Minimap.SetTileData(data, w, h);
            _owner.TileRequestedZone = null;
        };
        _client.ObstacleDataReceived += (data, w, h) =>
        {
            _owner.MapRenderer.SetObstacleData(data, w, h);
            _owner.Minimap.SetObstacleData(data, w, h);
        };
        _client.ObjectLayerDataReceived += (data, w, h, tilesetId, tileSize) =>
        {
            _owner.MapRenderer.SetObjectLayerData(data, w, h, tilesetId, tileSize);
        };
        _client.SectorDataReceived += sector =>
        {
            _owner.MapRenderer.SetSectorData(sector);
            _owner.Minimap.SetSectorData(sector);
            _owner.WorldMapWindow.SetSectorData(sector);
            _owner.RequestedSectors.Remove((sector.Col, sector.Row));
            RequestSectorsAround();
            PreloadWorldMap();
        };
        _client.ChatReceived += (channel, name, text, isAdmin) =>
        {
            if (Enum.TryParse<ChatChannel>(channel, out var ch))
                _owner.ChatRenderer.AddMessage(ch, name, text, isAdmin);
            else
                _owner.ChatRenderer.AddMessage(ChatChannel.System, name, text, isAdmin);
        };
        _client.SystemMessage += msg => _owner.ChatRenderer.AddMessage(ChatChannel.System, "Система", msg);
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
            _owner.MapRenderer.SpawnFloatingText(x, y, text, color, isCrit);
        };
        _client.CombatStateUpdated += (inCombat, targetName, hp, maxHp, targetId) =>
        {
            _owner.HudRenderer.UpdateCombatState(inCombat, targetName, hp, maxHp, targetId);
            if (!inCombat)
            {
                _owner.MapRenderer.ClearSelection();
                _owner.HudRenderer.UpdateTargetDebuffs(null);
            }
        };
        _client.PlayerAttackPerformed += (hand, skillId, targetX, targetY) =>
        {
            _owner.MapRenderer.TriggerAttack(hand);
            if (skillId != null)
            {
                if (SkillEffectManager.IsOnPlayer(skillId))
                {
                    SkillEffectManager.Spawn(skillId, _owner.MapRenderer.GetPlayerX(), _owner.MapRenderer.GetPlayerY());
                }
                else if (targetX.HasValue && targetY.HasValue)
                {
                    SkillEffectManager.Spawn(skillId, targetX.Value, targetY.Value);
                }
                else
                {
                    var sel = _owner.MapRenderer.GetSelectedMapPos();
                    if (sel.HasValue)
                        SkillEffectManager.Spawn(skillId, sel.Value.X, sel.Value.Y);
                    else
                        SkillEffectManager.Spawn(skillId, _owner.MapRenderer.GetPlayerX(), _owner.MapRenderer.GetPlayerY());
                }
            }
        };
        _client.RemotePlayerAttack += (playerName, hand, skillId, targetX, targetY, buffDurationMs) =>
        {
            _owner.MapRenderer.TriggerRemoteAttack(playerName, hand);
            if (skillId != null)
            {
                if (SkillEffectManager.IsOnPlayer(skillId))
                {
                    var pos = _owner.MapRenderer.GetRemotePlayerPos(playerName);
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
                    var pos = _owner.MapRenderer.GetRemotePlayerPos(playerName);
                    if (pos.HasValue)
                        SkillEffectManager.Spawn(skillId, pos.Value.X, pos.Value.Y, forceMap: true);
                }
            }
        };
        _client.RemotePlayerFacing += (playerName, facing) => _owner.MapRenderer.UpdateRemotePlayerFacing(playerName, facing);
        _owner.MapRenderer.OnFacingChanged = facing => _ = _client.SendAsync("player_facing", new { Facing = facing });
        _client.TargetDebuffsUpdated += debuffs =>
        {
            _owner.HudRenderer.UpdateTargetDebuffs(debuffs);
        };
        _client.AttackCooldownUpdated += (skillId, remainingMs, totalMs) =>
        {
            int slot = -1;
            var slots = _owner.InputManager.HotbarSlots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == "skill:" + (_owner.InputManager.GetSkillById(skillId)?.Name ?? ""))
                    slot = i;
            if (slot >= 0 && totalMs > 0)
                _owner.Input.HotbarCooldowns[slot] = (DateTime.UtcNow.AddMilliseconds(remainingMs), totalMs);
            if (_owner.Input.PendingSkillId == skillId)
            {
                _owner.Input.PendingSkillId = null;
                _owner.Input.PendingSlot = -1;
                _owner.Input.PendingSent = false;
            }
        };
        _client.TargetCleared += _ =>
        {
            _owner.HudRenderer.ClearTarget();
            _owner.MapRenderer.ClearSelection();
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
            _partyInfo = party;
            _owner.HudRenderer.UpdateParty(party);
            var myName = _client.PlayerName;
            var groupNames = party.Members
                .Where(m => !string.Equals(m.Name, myName, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Name).ToList();
            _owner.MapRenderer.SetPartyMembers(groupNames);
            _owner.Minimap.SetPartyMembers(groupNames);
            var myId = _client.PlayerId;
            if (_lastPartyMemberCount == 0 && party.Members.Count >= 2)
                _owner.ChatRenderer.AddMessage(ChatChannel.Party, "Группа", "Группа сформирована!");
            else
                foreach (var m in party.Members)
                    if (m.PlayerId != myId && !_owner.LastPartyMemberIds.Contains(m.PlayerId))
                        _owner.ChatRenderer.AddMessage(ChatChannel.Party, "Группа", $"{m.Name} присоединился к группе");
            _lastPartyMemberCount = party.Members.Count;
            _owner.LastPartyMemberIds = party.Members.Select(m => m.PlayerId).ToHashSet();
        };
        _client.PartyDisbanded += () =>
        {
            _partyInfo = null;
            if (_lastPartyMemberCount > 0)
                _owner.ChatRenderer.AddMessage(ChatChannel.Party, "Группа", "Группа распущена.");
            _lastPartyMemberCount = 0;
            _owner.LastPartyMemberIds.Clear();
            _owner.HudRenderer.ClearParty();
            _owner.MapRenderer.SetPartyMembers(Array.Empty<string>());
            _owner.Minimap.SetPartyMembers(Array.Empty<string>());
        };
        _client.PartyInviteReceived += (inviterName, _) =>
        {
            _owner.PartyInviteWindow.Show(inviterName);
            _windows.BringToFront(_owner.PartyInviteWindow);
        };
        _owner.PartyInviteWindow.Accepted += inviterName => _ = _client.SendAsync("party_accept", new { InviterName = inviterName });
        _owner.PartyInviteWindow.Declined += inviterName => _ = _client.SendAsync("party_decline", new { InviterName = inviterName });
    }

    private void WireInstanceEvents()
    {
        _client.InstanceWindowOpened += () =>
        {
            bool isLeader = _partyInfo != null && _partyInfo.LeaderId == _client.PlayerId;
            _owner.InstanceWindow.Show(isLeader);
            _windows.BringToFront(_owner.InstanceWindow);
            _ = _client.SendAsync("instance_list_request", null);
        };
        _client.InstanceListReceived += list => _owner.InstanceWindow.SetInstances(list);
        _client.InstanceInviteReceived += (leaderName, templateName, templateId) =>
        {
            _owner.InstanceInviteWindow.Show(leaderName, templateName);
            _windows.BringToFront(_owner.InstanceInviteWindow);
        };
        _client.InstanceInviteUpdate += (templateName, members) => _owner.InstanceWindow.SetSession(templateName, members);
        _client.InstanceStarted += (templateName, mode) =>
        {
            _owner.InstanceWindow.OnStarted(templateName, mode);
            _owner.InstanceWindow.Visible = false;
            _owner.InstanceInviteWindow.Visible = false;
            _owner.DialogueWindow.CloseDialogue();
        };
        _owner.InstanceWindow.SoloRequested += templateId => _ = _client.SendAsync("instance_enter_solo", new { TemplateId = templateId });
        _owner.InstanceWindow.GroupRequested += templateId => _ = _client.SendAsync("instance_invite", new { TemplateId = templateId });
        _owner.InstanceWindow.StartRequested += () => _ = _client.SendAsync("instance_start", null);
        _owner.InstanceInviteWindow.Ready += () => _ = _client.SendAsync("instance_invite_response", new { Ready = true });
        _owner.InstanceInviteWindow.Cancelled += () => _ = _client.SendAsync("instance_invite_response", new { Ready = false });
    }

    private void WireStatusEvents()
    {
        _client.InspectReceived += data =>
        {
            _owner.InspectWindow.OpenInspect(data);
            GameInputHandler.CenterWindow(_owner.InspectWindow, GameMain.Instance!);
            _owner.Input.PushWindow(_owner.InspectWindow);
            _windows.BringToFront(_owner.InspectWindow);
        };
        _client.StatusUpdated += status =>
        {
            _owner.HudRenderer.UpdateStatus(status);
            _owner.HudDraw.SetAttributePoints(status.AttributePoints);
            _owner.StatusWindow.UpdateData(status);
            _owner.Input.PlayerGoldCache = status.Gold;
            _owner.SkillsWindow.SetPlayerLevel(status.Level);
            _owner.SkillsWindow.SetSkillPoints(status.SkillPoints);
            if (_owner.Input.LastXp < 0) { _owner.Input.LastXp = status.Experience; _owner.Input.LastLevel = status.Level; }
            else
            {
                int xpGain = status.Experience - _owner.Input.LastXp;
                if (xpGain > 0)
                    _owner.MapRenderer.SpawnFloatingTextAtPlayer($"+{xpGain} XP", new Color(120, 220, 255));
                if (status.Level > _owner.Input.LastLevel)
                    _owner.MapRenderer.SpawnFloatingTextAtPlayer("Новый уровень!", Color.Gold, true);
                _owner.Input.LastXp = status.Experience;
                _owner.Input.LastLevel = status.Level;
            }

            bool hasBuff = status.ActiveDebuffs?.Any(d => d.Type == "AttackSpeedBonus") ?? false;
            if (hasBuff && !SkillEffectManager.HasLooping(SkillIds.Flurry))
                SkillEffectManager.SpawnLooping(SkillIds.Flurry, _owner.MapRenderer.GetPlayerX(), _owner.MapRenderer.GetPlayerY());
            else if (!hasBuff)
                SkillEffectManager.StopLooping(SkillIds.Flurry);

            _owner.MapRenderer.SetSuppressingFireActive(status.ActiveDebuffs?.Any(d => d.Type == "SuppressingFire") ?? false);

            // Открытый мир: секторы по мере перемещения игрока
            if (_owner.CurrentZoneId == BalanceStatic.MainZoneId)
                RequestSectorsAround();
        };
        _client.SkillsUpdated += skills =>
        {
            _owner.InputManager.SetSkills(skills);
            _owner.SkillsWindow.SetSkillPoints(_client.SkillPoints);
            _owner.SkillsWindow.UpdateData(skills);
        };
    }

    private void WireInventoryEvents()
    {
        _client.InventoryUpdated += inv =>
        {
            _owner.InputManager.SetInventory(inv);
            _owner.InventoryWindow.UpdateData(inv);
            if (inv.Equipment != null) _owner.EquipmentWindow.UpdateData(inv.Equipment);
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
            _owner.MapRenderer.SetWeaponSubtype(weaponSub);
            _owner.MapRenderer.SetTwoHanded(isTwoHanded);
            string? shieldSub = null;
            string? offWeaponSub = null;
            if (inv.Equipment?.Slots != null && inv.Equipment.Slots.TryGetValue("lhand", out var lHand))
            {
                if (lHand?.Type == "shield" && !Equipment.IsCasterOffhand(lHand))
                    shieldSub = "shield";
                else if (lHand?.Type == "weapon" || Equipment.IsCasterOffhand(lHand))
                    offWeaponSub = lHand?.WeaponSubtype;
            }
            _owner.MapRenderer.SetShieldSubtype(shieldSub);
            _owner.MapRenderer.SetOffHandWeaponSubtype(offWeaponSub);
            if (_owner.EnhancementWindow.Visible)
                _owner.EnhancementWindow.Refresh(inv.Items ?? new List<Item>());
        };
        _owner.InventoryWindow.NewItemCountChanged += count => _owner.HudDraw.SetNewInventoryCount(count);
        _owner.EquipmentWindow.UnequipItem += slot => _ = _client.SendAsync("unequip", new { Slot = slot });
        _owner.EquipmentWindow.UnequipAll += () => _ = _client.SendAsync("unequip_all", new { });
        _owner.EquipmentWindow.MoveToSlot += (from, to) => _ = _client.SendAsync("equip", new { TargetSlot = to, FromSlot = from });
        _owner.EquipmentWindow.CloseRequested += () => _owner.EquipmentWindow.Visible = false;
        _owner.InventoryWindow.DragStateChanged += item =>
        {
            _owner.EquipmentWindow.DraggingType = item?.Type;
            _owner.Input.DragOverlayItem = item;
            _owner.EnhancementWindow.DraggedItem = item;
        };
        _owner.EquipmentWindow.DragStateChanged += item =>
        {
            _owner.EquipmentWindow.DraggingType = item?.Type;
            _owner.Input.DragOverlayItem = item;
        };
        _owner.EquipmentWindow.IsOverInventory = pt => _owner.InventoryWindow.Contains(pt);
        _owner.EnhancementWindow.IsOverInventory = pt => _owner.InventoryWindow.Contains(pt);
        _owner.EnhancementWindow.DragStateChanged += item => _owner.Input.DragOverlayItem = item;
        _owner.LootWindow.DragStateChanged += item => _owner.Input.DragOverlayItem = item;
        _owner.LootWindow.DropOnInventory += (pt, item) =>
        {
            if (_owner.InventoryWindow.Contains(pt))
            {
                if (_owner.LootWindow.CorpseId.StartsWith("chest_"))
                    _ = _client.SendAsync("take_chest_loot", new { InstanceId = _owner.LootWindow.CorpseId.Substring(6), ItemIds = new[] { item.Id }, TakeGold = false });
                else
                    _ = _client.SendAsync("take_loot", new { CorpseId = _owner.LootWindow.CorpseId, TakeAll = false, ItemIds = new[] { item.Id }, TakeGold = false });
                _owner.LootWindow.RemoveItem(item);
            }
        };
        _owner.InventoryWindow.DropOnEquip += (pt, item) =>
        {
            if (!_owner.EquipmentWindow.Visible) return false;
            if (_owner.EquipmentWindow.TryGetSlotAt(pt, item.Type, out var slot) && slot != null)
            {
                _ = _client.SendAsync("equip", new { ItemId = item.Id, TargetSlot = slot });
                return true;
            }
            return false;
        };
        _owner.InventoryWindow.EquipItem += id =>
        {
            _ = _client.SendAsync("equip", new { ItemId = id });
            GameInputHandler.OpenEquipmentBesideInventory(_owner.EquipmentWindow, _owner.InventoryWindow, GameMain.Instance!);
        };
        _owner.InventoryWindow.UseItem += id => _ = _client.SendAsync("use_item", new { ItemId = id });
        _owner.InventoryWindow.DeleteItem += id => _ = _client.SendAsync("drop_item", new { ItemId = id });
        _owner.InventoryWindow.SortItems += () =>
        {
            var inv = _client.Inventory;
            if (inv?.Items == null) return;
            // Сортировка сразу по двум признакам: редкость (эпик первым), затем требуемый уровень
            var order = inv.Items.OrderByDescending(i => i.Quality)
                .ThenBy(i => i.RequiredLevel)
                .ThenBy(i => i.Name)
                .Select(i => i.Id).ToList();
            _ = _client.SendAsync("inventory_sort", new { Order = order });
        };
        _owner.StatusWindow.AllocateAttribute += attr => _ = _client.SendAsync("allocate_attribute", new { Attribute = attr });
        _owner.StatusWindow.ResetAttributes += () => _ = _client.SendAsync("reset_attributes", new { });
    }

    private void WireHotbarEvents()
    {
        _client.HotbarUpdated += slots => _owner.InputManager.UpdateHotbar(slots);
        _owner.InputManager.HotbarActivated += (idx, item) => _owner.Input.ActivateHotbarSlot(idx, item, GameMain.Instance!);
    }

    private void WireShopEvents()
    {
        _client.ShopUpdated += data =>
        {
            _owner.ShopWindow.UpdateData(data);
            _owner.InventoryWindow.Visible = true;
            _owner.InventoryWindow.ShopMode = true;
            _owner.Input.PositionTradeWindows(_owner.ShopWindow, _owner.InventoryWindow, GameMain.Instance!);
            // Магазин поверх инвентаря в стеке окон: первый Esc закроет магазин, второй — инвентарь
            _owner.Input.PushWindow(_owner.InventoryWindow);
            _owner.Input.PushWindow(_owner.ShopWindow);
        };
        _owner.ShopWindow.Closed += () =>
        {
            _owner.ShopWindow.Visible = false;
            _owner.InventoryWindow.Visible = false;
            _owner.InventoryWindow.ShopMode = false;
        };
        _owner.ShopWindow.Escaped += () => _owner.InventoryWindow.ShopMode = false;
        _owner.ShopWindow.BuyItem += (id, qty) => _ = _client.SendAsync("buy", new { ItemId = id, Quantity = qty });
        _owner.ShopWindow.DragStateChanged += item => _owner.Input.DragOverlayItem = item;
        _owner.ShopWindow.SellAllTrophies += () => _ = _client.SendAsync("sell_all_trophies", new { });
        _owner.ShopWindow.DropOnInventory += (pt, item) =>
        {
            if (!_owner.InventoryWindow.Visible || !_owner.InventoryWindow.Contains(pt)) return false;
            int stock = Math.Max(1, item.Stock);
            int maxAffordable = item.Value > 0
                ? _owner.Input.PlayerGoldCache / item.Value : stock;
            int max = Math.Min(stock, Math.Max(1, maxAffordable));
            if (stock > 1 || item.MaxStack > 1)
                _owner.Input.OpenQuantity(item.Name, max, item.Value,
                    q => _ = _client.SendAsync("buy", new { ItemId = item.Id, Quantity = q }), true, _owner.QuantityDialog, GameMain.Instance!);
            else
                _ = _client.SendAsync("buy", new { ItemId = item.Id, Quantity = 1 });
            return true;
        };
        _owner.ShopWindow.PendingBuy += (item, max) =>
        {
            int stock = Math.Max(1, item.Stock);
            int maxAffordable = item.Value > 0
                ? _owner.Input.PlayerGoldCache / item.Value : stock;
            int realMax = Math.Min(max, Math.Max(1, maxAffordable));
            _owner.Input.OpenQuantity(item.Name, realMax, item.Value,
                q => _ = _client.SendAsync("buy", new { ItemId = item.Id, Quantity = q }), true, _owner.QuantityDialog, GameMain.Instance!);
        };
        _owner.InventoryWindow.DropOnSell += (pt, item) =>
        {
            if (!_owner.ShopWindow.Visible || !_owner.ShopWindow.Contains(pt)) return false;
            _ = _client.SendAsync("sell", new { ItemId = item.Id, Quantity = 1 });
            return true;
        };
        _client.EnhancementOpened += () =>
        {
            _owner.EnhancementWindow.Reset();
            _owner.EnhancementWindow.Visible = true;
            _owner.Input.PushWindow(_owner.EnhancementWindow);
        };
        _owner.EnhancementWindow.UpgradeRequested += (itemId, stoneId) =>
            _ = _client.SendAsync("upgrade_item", new { ItemId = itemId, StoneId = stoneId });
        _owner.InventoryWindow.DropOnEnhance += (pt, item) =>
        {
            if (!_owner.EnhancementWindow.Visible || !_owner.EnhancementWindow.Contains(pt)) return false;
            return _owner.EnhancementWindow.AddItem(item);
        };
        _owner.InventoryWindow.SellItem += (id, qty) => _ = _client.SendAsync("sell", new { ItemId = id, Quantity = qty });
        _owner.InventoryWindow.PendingSell += (item, max) =>
            _owner.Input.OpenQuantity(item.Name, max, 1, q => _ = _client.SendAsync("sell", new { ItemId = item.Id, Quantity = q }), true, _owner.QuantityDialog, GameMain.Instance!);
        _owner.InventoryWindow.PendingDrop += (item, max) =>
            _owner.Input.OpenQuantity(item.Name, max, 1, q => _ = _client.SendAsync("drop_item", new { ItemId = item.Id, Quantity = q }), false, _owner.QuantityDialog, GameMain.Instance!);
    }

    private void WireTradeEvents()
    {
        _client.TradeOpened += data =>
        {
            var inv = data.YourInventory ?? new List<TradeItemData>();
            var grouped = inv.GroupBy(i => i.Id).Select(gr => $"{gr.First().Name} x{gr.Count()}").ToList();
            Logger.Action($"ОБМЕН: получен trade_open с '{data.OtherName}', предметов в инвентаре={inv.Count} (уникальных={grouped.Count}), золото={data.YourGold}");
            foreach (var line in grouped) Logger.Debug($"ОБМЕН: инвентарь аккаунта -> {line}");
            _owner.TradeWindow.Open(data);
            _windows.BringToFront(_owner.TradeWindow);
        };
        _client.TradeOfferUpdated += offer =>
        {
            if (offer.IsFromMe) _owner.TradeWindow.UpdateMyOffer(offer);
            else _owner.TradeWindow.UpdateTheirOffer(offer);
        };
        _client.TradeConfirmUpdated += conf => _owner.TradeWindow.UpdateConfirm(conf);
        _client.TradeCompleted += done =>
        {
            _owner.TradeWindow.HandleComplete(done);
            if (!string.IsNullOrEmpty(done.Message))
                _owner.ChatRenderer.AddMessage(ChatChannel.System, "Обмен", done.Message);
        };
        _client.TradeClosed += msg =>
        {
            _owner.TradeWindow.Visible = false;
            if (!string.IsNullOrEmpty(msg))
                _owner.ChatRenderer.AddMessage(ChatChannel.System, "Обмен", msg);
        };
        _owner.TradeWindow.OfferChanged += (entries, gold) =>
            _ = _client.SendAsync("trade_offer", new { Entries = entries, Gold = gold });
        _owner.TradeWindow.RequestQuantity += (itemName, max, defaultQty, onConfirm) =>
            _owner.Input.OpenQuantity(itemName, max, 0, onConfirm, false, _owner.QuantityDialog, GameMain.Instance!);
        _owner.TradeWindow.ConfirmRequested += () => _ = _client.SendAsync("trade_confirm", null);
        _owner.TradeWindow.CancelRequested += () => _ = _client.SendAsync("trade_cancel", null);
    }

    private void WireQuestEvents()
    {
        _owner.QuestBoardWindow.TakeQuest += id => _ = _client.SendAsync("take_quest", new { QuestId = id });
        _owner.QuestBoardWindow.CompleteQuest += id => _ = _client.SendAsync("complete_quest", new { QuestId = id });
        _owner.QuestBoardWindow.AbandonQuest += id => _ = _client.SendAsync("abandon_quest", new { QuestId = id });
        _owner.QuestLogWindow.AbandonQuest += id => _ = _client.SendAsync("abandon_quest", new { QuestId = id });
        _client.QuestLogUpdated += (available, active, history) =>
        {
            _owner.QuestLogWindow.UpdateData(active, history);
            _owner.ActiveQuests = active ?? new List<QuestInfo>();
            _owner.QuestBoardWindow.UpdateData(available, _owner.ActiveQuests);
        };
        _client.BoardOpened += () =>
        {
            _owner.QuestBoardWindow.Visible = true;
            GameInputHandler.CenterWindow(_owner.QuestBoardWindow, GameMain.Instance!);
        };
    }

    private void WireSkillsEvents()
    {
        _owner.SkillsWindow.UseSkill += id =>
        {
            if (_owner.Input.PendingSkillId == id)
            {
                _owner.Input.PendingSkillId = null;
                _owner.Input.PendingSlot = -1;
                _owner.Input.PendingSent = false;
                _ = _client.SendAsync("cancel_skill", new { });
                return;
            }
            int slot = -1;
            var slots = _owner.InputManager.HotbarSlots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == "skill:" + (_owner.InputManager.GetSkillById(id)?.Name ?? ""))
                    slot = i;
            _owner.Input.PendingSkillId = id;
            _owner.Input.PendingSlot = slot;
            _owner.Input.PendingSent = false;
        };
        _owner.SkillsWindow.SkillDragStateChanged += skill => _owner.Input.DragOverlaySkill = skill;
        _owner.SkillsWindow.SkillDragEnded += () => _owner.Input.HandleSkillDragEnd(GameMain.Instance!);
        _owner.SkillsWindow.LearnSkill += skillId =>
            _ = _client.SendAsync("allocate_skill", new { SkillId = skillId });
        _owner.SkillsWindow.ResetSkills += () =>
            _ = _client.SendAsync("reset_skills", new { });
    }

    private void WireLootEvents()
    {
        _client.LootReceived += (corpseId, monsterName, damagePct, items, gold) =>
        {
            _owner.LootWindow.Setup(corpseId, monsterName, damagePct, items, gold);
            _owner.LootWindow.X = Math.Max(0, (GameMain.Instance!.Graphics.PreferredBackBufferWidth - _owner.LootWindow.Width) / 2);
            _owner.LootWindow.Y = Math.Max(0, (GameMain.Instance!.Graphics.PreferredBackBufferHeight - _owner.LootWindow.Height) / 2);
        };
        _owner.LootWindow.TakeItem += item =>
        {
            if (_owner.LootWindow.CorpseId.StartsWith("chest_"))
                _ = _client.SendAsync("take_chest_loot", new { InstanceId = _owner.LootWindow.CorpseId.Substring(6), ItemIds = new[] { item.Id }, TakeGold = false });
            else
                _ = _client.SendAsync("take_loot", new { CorpseId = _owner.LootWindow.CorpseId, TakeAll = false, ItemIds = new[] { item.Id }, TakeGold = false });
            _owner.LootWindow.RemoveItem(item);
        };
        _owner.LootWindow.TakeLoot += (corpseId, takeAll, ids, takeGold) =>
        {
            if (corpseId.StartsWith("chest_"))
                _ = _client.SendAsync("take_chest_loot", new { InstanceId = corpseId.Substring(6), ItemIds = ids, TakeGold = takeGold });
            else
                _ = _client.SendAsync("take_loot", new { CorpseId = corpseId, TakeAll = takeAll, ItemIds = ids, TakeGold = takeGold });
        };
    }

    private void WireMapInteractionEvents()
    {
        _owner.MapRenderer.MoveRequested += (x, y) =>
        {
            Logger.Action($"Движение в клетку ({x}, {y})");
            _ = _client.SendAsync("move_to", new { X = x, Y = y });
        };
        _owner.MapRenderer.InteractRequested += (entity, x, y) =>
        {
            Logger.Action($"Взаимодействие с {entity.Type} '{entity.Name}' ({x}, {y})");
            if (entity.Type == "corpse" && entity.Id != null)
                _ = _client.SendAsync("loot_corpse", new { CorpseId = entity.Id });
            else if (entity.Type == "player")
                _ = _client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, PlayerId = entity.Id?.ToString() });
            else
                _ = _client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, MonsterId = entity.Id?.ToString() });
        };
        _owner.MapRenderer.EntityPickRequested += (entities, mapX, mapY) =>
        {
            var filtered = entities.Where(e => e.Type != "corpse" || e.Id == null || !_owner.LootedCorpses.Contains(e.Id)).ToList();
            if (filtered.Count == 0) return;
            if (filtered.Count == 1)
            {
                var single = filtered[0];
                Logger.Action($"Выбрана сущность: {single.Type} '{single.Name}' ({mapX}, {mapY})");
                if (single.Type == "corpse" && single.Id != null)
                {
                    _owner.LootedCorpses.Add(single.Id);
                    _ = _client.SendAsync("loot_corpse", new { CorpseId = single.Id });
                }
                else if (single.Type == "player")
                    _ = _client.SendAsync("interact_target", new { Type = single.Type, X = mapX, Y = mapY, PlayerId = single.Id?.ToString() });
                else
                    _ = _client.SendAsync("interact_target", new { Type = single.Type, X = mapX, Y = mapY, MonsterId = single.Id?.ToString() });
                return;
            }
            _owner.EntityPickDialog.Setup(filtered, mapX, mapY);
            _owner.EntityPickDialog.X = Math.Max(0, (GameMain.Instance!.Graphics.PreferredBackBufferWidth - _owner.EntityPickDialog.Width) / 2);
            _owner.EntityPickDialog.Y = Math.Max(0, (GameMain.Instance!.Graphics.PreferredBackBufferHeight - _owner.EntityPickDialog.Height) / 2);
            _windows.BringToFront(_owner.EntityPickDialog);
        };
        _owner.EntityPickDialog.OnPick += (entity, x, y) =>
        {
            Logger.Action($"Выбрана сущность: {entity.Type} '{entity.Name}' ({x}, {y})");
            if (entity.Type == "corpse" && entity.Id != null)
            {
                _owner.LootedCorpses.Add(entity.Id);
                _ = _client.SendAsync("loot_corpse", new { CorpseId = entity.Id });
            }
            else if (entity.Type == "player")
                _ = _client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, PlayerId = entity.Id?.ToString() });
            else
                _ = _client.SendAsync("interact_target", new { Type = entity.Type, X = x, Y = y, MonsterId = entity.Id?.ToString() });
        };
        _owner.MapRenderer.SelectionChanged += entity =>
        {
            if (entity == null)
            {
                _owner.HudRenderer.UpdateTargetDebuffs(null);
            }
            else if (entity.Type == "player" && entity.Id != null)
            {
                _owner.HudRenderer.UpdateTargetDebuffs(null);
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
                    _owner.HudRenderer.UpdateTargetDebuffs(infos);
                }
                else
                {
                    _owner.HudRenderer.UpdateTargetDebuffs(null);
                }
            }
            else
            {
                _owner.HudRenderer.UpdateTargetDebuffs(null);
            }
        };
    }

    private void WireSettingsEvents()
    {
        _owner.SettingsWindow.ApplyRequested += ApplySettings;
        _owner.SettingsWindow.LogoutRequested += () =>
        {
            _owner.SettingsWindow.Visible = false;
            _owner.LogoutConfirmWindow.ResetTimer();
            _owner.LogoutConfirmWindow.X = (GameMain.Instance!.Graphics.PreferredBackBufferWidth - _owner.LogoutConfirmWindow.Width) / 2;
            _owner.LogoutConfirmWindow.Y = (GameMain.Instance!.Graphics.PreferredBackBufferHeight - _owner.LogoutConfirmWindow.Height) / 2;
            _owner.LogoutConfirmWindow.Visible = true;
            _windows.BringToFront(_owner.LogoutConfirmWindow);
        };
        _owner.LogoutConfirmWindow.Confirmed += () =>
        {
            _ = _client.SendAsync("logout", new { });
            GameMain.Instance!.Network.Disconnect();
            GameMain.Instance.ShowLogin();
        };
        _owner.LogoutConfirmWindow.Cancelled += () => { };
    }

    private void WireMailEvents()
    {
        _owner.MailWindow.InboxRequested += () =>
        {
            _ = _client.SendAsync("mail", new { Action = "inbox" });
        };
        _owner.MailWindow.OutboxRequested += () =>
        {
            _ = _client.SendAsync("mail", new { Action = "outbox" });
        };
        _owner.MailWindow.SendRequested += (recipient, subject, body, gold, attachments) =>
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
        _owner.MailWindow.ReadRequested += id =>
        {
            _ = _client.SendAsync("mail", new { Action = "read", MailId = id });
        };
        _owner.MailWindow.DeleteRequested += id =>
        {
            _ = _client.SendAsync("mail", new { Action = "delete", MailId = id });
        };
        _owner.MailWindow.TakeAttachmentRequested += id =>
        {
            _ = _client.SendAsync("mail", new { Action = "take", MailId = id });
        };
        _owner.MailWindow.AttachmentRequested += () =>
        {
            var items = _client.Inventory?.Items?
                .Where(i => i.Type != "gold")
                .ToList() ?? new();
            _owner.MailAttachmentWindow.Open(items, _owner.MailWindow.ComposeAttachments);
            GameInputHandler.CenterWindow(_owner.MailAttachmentWindow, GameMain.Instance!);
            _windows.BringToFront(_owner.MailAttachmentWindow);
        };
        _owner.MailAttachmentWindow.ConfirmRequested += () =>
        {
            _owner.MailWindow.SetComposeAttachments(_owner.MailAttachmentWindow.Attachments);
        };
        _owner.MailAttachmentWindow.RequestQuantity += (name, max, defaultQty, onConfirm) =>
            _owner.Input.OpenQuantity(name, max, 0, onConfirm, false, _owner.QuantityDialog, GameMain.Instance!);
        _client.MailListReceived += (folder, mails) =>
        {
            if (folder == "inbox")
                _owner.MailWindow.SetInbox(mails);
            else if (folder == "outbox")
                _owner.MailWindow.SetOutbox(mails);
        };
        _client.MailDetailReceived += mail =>
        {
            _owner.MailWindow.UpdateMail(mail);
        };
        _client.MailResultReceived += (ok, msg) =>
        {
            if (!string.IsNullOrEmpty(msg))
                _owner.ChatRenderer.AddMessage(ChatChannel.System, "Почта", msg);
            if (ok && _owner.MailWindow.SelectedMailId > 0)
                _ = _client.SendAsync("mail", new { Action = "read", MailId = _owner.MailWindow.SelectedMailId });
        };
        _client.MailUnreadReceived += count =>
        {
            _owner.HudDraw.SetMailUnreadCount(count);
            _owner.MailWindow.RefreshInboxIfOpen();
        };
    }

    private void WireStorageEvents()
    {
        _client.StorageOpened += data =>
        {
            _owner.StorageWindow.UpdateData(data.Items, data.Slots);
            _owner.InventoryWindow.Visible = true;
            _owner.Input.PositionStorageWindows(_owner.StorageWindow, _owner.InventoryWindow, GameMain.Instance!);
            // Склад поверх инвентаря в стеке окон: первый Esc закроет склад, второй — инвентарь
            _owner.Input.PushWindow(_owner.InventoryWindow);
            _owner.Input.PushWindow(_owner.StorageWindow);
            _windows.BringToFront(_owner.StorageWindow);
        };
        _client.StorageUpdated += data =>
        {
            _owner.StorageWindow.UpdateData(data.Items, data.Slots);
        };
        _owner.StorageWindow.WithdrawItem += (id, qty) => _ = _client.SendAsync("storage_withdraw", new { ItemId = id, Quantity = qty });
        _owner.StorageWindow.PendingWithdraw += (item, max) =>
            _owner.Input.OpenQuantity(item.Name, max, 0, q => _ = _client.SendAsync("storage_withdraw", new { ItemId = item.Id, Quantity = q }), false, _owner.QuantityDialog, GameMain.Instance!);
        _owner.StorageWindow.DragStateChanged += item => _owner.Input.DragOverlayItem = item;
        _owner.StorageWindow.IsOverInventory = pt => _owner.InventoryWindow.Visible && _owner.InventoryWindow.Contains(pt);
        _owner.InventoryWindow.IsStorageOpen = () => _owner.StorageWindow.Visible;
        _owner.InventoryWindow.DepositItem += (id, qty) => _ = _client.SendAsync("storage_deposit", new { ItemId = id, Quantity = qty });
        _owner.InventoryWindow.PendingDeposit += (item, max) =>
            _owner.Input.OpenQuantity(item.Name, max, 0, q => _ = _client.SendAsync("storage_deposit", new { ItemId = item.Id, Quantity = q }), false, _owner.QuantityDialog, GameMain.Instance!);
        _owner.InventoryWindow.DropOnStorage += (pt, item) =>
        {
            if (!_owner.StorageWindow.Visible || !_owner.StorageWindow.Contains(pt)) return false;
            int qty = Math.Max(1, item.Quantity);
            if (item.MaxStack > 1 && qty > 1)
            {
                _owner.Input.OpenQuantity(item.Name, qty, 0,
                    q => _ = _client.SendAsync("storage_deposit", new { ItemId = item.Id, Quantity = q }),
                    false, _owner.QuantityDialog, GameMain.Instance!);
            }
            else
            {
                _ = _client.SendAsync("storage_deposit", new { ItemId = item.Id, Quantity = 1 });
            }
            return true;
        };
    }

    private void WireDialogueEvents()
    {
        _client.DialogueOpened += (npcId, speaker, text, choices) =>
        {
            _owner.DialogueWindow.SetNode(speaker, text, choices);
            GameInputHandler.CenterWindow(_owner.DialogueWindow, GameMain.Instance!);
            _owner.Input.PushWindow(_owner.DialogueWindow);
        };
        _client.DialogueClosed += () =>
        {
            _owner.DialogueWindow.CloseDialogue();
        };
        _owner.DialogueWindow.DialogueClosed += () =>
        {
            _ = _client.SendAsync("dialogue_choice", new { ChoiceIndex = -1 });
        };
        _owner.DialogueWindow.ChoiceSelected += index =>
        {
            _ = _client.SendAsync("dialogue_choice", new { ChoiceIndex = index });
        };
    }

    private void WireDeathEvents()
    {
        _client.PlayerDeathReceived += lostGold =>
        {
            _owner.DeathWindow.Activate(lostGold);
            GameInputHandler.CenterWindow(_owner.DeathWindow, GameMain.Instance!);
            _windows.BringToFront(_owner.DeathWindow);
            _owner.MapRenderer.SetPlayerDead(true);
        };
        _owner.DeathWindow.ReviveRequested += () =>
        {
            _ = _client.SendAsync("revive", null);
            _owner.MapRenderer.SetPlayerDead(false);
        };
        _client.StatusUpdated += _ =>
        {
            if (!_client.IsDead && _owner.DeathWindow.Visible)
            {
                _owner.DeathWindow.Deactivate();
                _owner.MapRenderer.SetPlayerDead(false);
            }
        };
    }

    private void WireTradeRequestEvents()
    {
        _client.TradeRequestReceived += inviterName =>
        {
            _owner.TradeRequestWindow.Show(inviterName);
            _windows.BringToFront(_owner.TradeRequestWindow);
        };
        _owner.TradeRequestWindow.Accepted += inviterName => _ = _client.SendAsync("trade_accept", new { InviterName = inviterName });
        _owner.TradeRequestWindow.Declined += inviterName => _ = _client.SendAsync("trade_decline", new { InviterName = inviterName });
    }

    private void WireChangelogEvents()
    {
        _client.ChangelogReceived += data => ShowChangelog(data);
        _owner.SettingsWindow.OpenChangelog += () => ShowChangelog(_client.LastChangelog);
    }

    public void ShowChangelog(ChangelogData? data)
    {
        if (data?.Entries == null || data.Entries.Count == 0) return;

        _owner.ChangelogWindow.SetData(data);
        var viewport = GameMain.Instance?.GraphicsDevice.Viewport;
        if (viewport.HasValue)
        {
            _owner.ChangelogWindow.X = (viewport.Value.Width - _owner.ChangelogWindow.Width) / 2;
            _owner.ChangelogWindow.Y = (viewport.Value.Height - _owner.ChangelogWindow.Height) / 2;
        }
        _owner.ChangelogWindow.Visible = true;
        _windows.BringToFront(_owner.ChangelogWindow);
    }

    /// <summary>
    /// Self-heal тайлов: если map_update пришёл без TileData и у рендера нет валидных
    /// тайлов для текущей зоны (гонка при логине — первый map_update мог прийти до
    /// создания GameScreen), запрашиваем тайлы у сервера. Повтор не чаще раза в 3 сек.
    /// </summary>
    private void RequestTilesIfNeeded(WorldMap map)
    {
        if (_owner.MapRenderer.HasValidTiles(map.Width, map.Height)) return;
        if (_owner.TileRequestedZone == map.ZoneId && DateTime.UtcNow - _tileRequestTime < TimeSpan.FromSeconds(3))
            return;
        _owner.TileRequestedZone = map.ZoneId;
        _tileRequestTime = DateTime.UtcNow;
        Logger.Info($"TileRequest: запрашиваю тайлы зоны '{map.ZoneId}' ({map.Width}x{map.Height})");
        _ = _client.SendAsync("tile_request", null);
    }

    /// <summary>
    /// Открытый мир (main): запрашивает блок 3x3 секторов вокруг игрока.
    /// Повторные запросы уже запрошенных секторов не уходят, пока сектор не пришёл.
    /// </summary>
    private void RequestSectorsAround()
    {
        if (_owner.CurrentZoneId != BalanceStatic.MainZoneId) return;
        var st = _client.Status;
        if (st == null || st.X < 0 || st.Y < 0) return;

        int centerCol = Math.Clamp(st.X / BalanceStatic.SectorSize, 0, BalanceStatic.SectorCols - 1);
        int centerRow = Math.Clamp(st.Y / BalanceStatic.SectorSize, 0, BalanceStatic.SectorRows - 1);

        var toRequest = new List<(int Col, int Row)>();
        for (int r = centerRow - 1; r <= centerRow + 1; r++)
        {
            if (r < 0 || r >= BalanceStatic.SectorRows) continue;
            for (int c = centerCol - 1; c <= centerCol + 1; c++)
            {
                if (c < 0 || c >= BalanceStatic.SectorCols) continue;
                if (_owner.MapRenderer.HasSector(c, r)) continue;
                if (!_owner.RequestedSectors.Add((c, r))) continue;
                toRequest.Add((c, r));
            }
        }

        foreach (var (c, r) in toRequest)
        {
            Logger.Debug($"SectorRequest: запрашиваю сектор ({c}, {r})");
            _ = _client.SendAsync("sector_request", new { Col = c, Row = r });
        }
    }

    /// <summary>
    /// Слепок карты мира: один раз за «сессию» (вход в игру / /reload / /reloadmap)
    /// запрашивает все секторы открытого мира, чтобы окно карты было готово сразу,
    /// а не заполнялось построчно при открытии. Сервер дедуплицирует уже отправленные
    /// секторы, повторные запросы лишний трафик не дают.
    /// </summary>
    private void PreloadWorldMap()
    {
        if (_owner.WorldMapPreloaded) return;
        if (_owner.CurrentZoneId != BalanceStatic.MainZoneId) return;
        var st = _client.Status;
        if (st == null || st.X < 0 || st.Y < 0) return;
        _owner.WorldMapPreloaded = true;
        for (int r = 0; r < BalanceStatic.SectorRows; r++)
        {
            for (int c = 0; c < BalanceStatic.SectorCols; c++)
            {
                _ = _client.SendAsync("sector_request", new { Col = c, Row = r });
            }
        }
        Logger.Debug("WorldMap: запрошены все секторы открытого мира (слепок).");
    }

    private void ApplySettings()
    {
        var g = GameMain.Instance!.Graphics;
        var (rw, rh) = _owner.SettingsWindow.SelectedResolution;
        g.PreferredBackBufferWidth = rw;
        g.PreferredBackBufferHeight = rh;
        switch (_owner.SettingsWindow.SelectedMode)
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
        s.Mode = _owner.SettingsWindow.SelectedMode;
        s.Save();
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
