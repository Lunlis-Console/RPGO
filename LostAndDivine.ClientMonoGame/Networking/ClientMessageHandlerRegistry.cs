using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using LostAndDivine.ClientMonoGame.Windows;
using LostAndDivine.ClientMonoGame.Screens;
using System.Text.Json;

namespace LostAndDivine.ClientMonoGame.Networking;

/// <summary>
/// Registry of client-side message handlers. Maps message type → handler method.
/// Each handler extracts data and fires the corresponding GameClient event via Raise* methods.
/// </summary>
internal static class ClientMessageHandlerRegistry
{
    private static readonly Dictionary<string, Action<GameClient, GameMessage>> _handlers = new()
    {
        ["auth_response"] = HandleAuthResponse,
        ["welcome"] = HandleWelcome,
        ["map_update"] = HandleMapUpdate,
        ["chat"] = HandleChat,
        ["error"] = HandleError,
        ["status_response"] = HandleStatusResponse,
        ["inventory_response"] = HandleInventoryResponse,
        ["quest_log"] = HandleQuestLog,
        ["zone_transition"] = HandleZoneTransition,
        ["shop_response"] = HandleShopResponse,
        ["shop_update"] = HandleShopResponse,
        ["trade_open"] = HandleTradeOpen,
        ["trade_offer_update"] = HandleTradeOfferUpdate,
        ["trade_confirm_update"] = HandleTradeConfirmUpdate,
        ["trade_complete"] = HandleTradeComplete,
        ["trade_close"] = HandleTradeClose,
        ["trade_declined"] = HandleTradeDeclined,
        ["dialogue_open"] = HandleDialogueOpen,
        ["dialogue_close"] = HandleDialogueClose,
        ["damage"] = HandleDamage,
        ["heal"] = HandleHeal,
        ["mana_regen"] = HandleManaRegen,
        ["player_death"] = HandlePlayerDeath,
        ["combat_state"] = HandleCombatState,
        ["target_debuff_update"] = HandleTargetDebuffUpdate,
        ["cancel_target"] = HandleCancelTarget,
        ["target_cleared"] = HandleCancelTarget,
        ["party_invite_sent"] = HandlePartyInviteSent,
        ["party_invite_declined"] = HandlePartyInviteDeclined,
        ["trade_request_sent"] = HandleTradeRequestSent,
        ["party_update"] = HandlePartyUpdate,
        ["party_disbanded"] = HandlePartyDisbanded,
        ["party_invite_received"] = HandlePartyInviteReceived,
        ["trade_request_received"] = HandleTradeRequestReceived,
        ["skills_response"] = HandleSkillsResponse,
        ["hotbar_update"] = HandleHotbar,
        ["hotbar_response"] = HandleHotbar,
        ["loot_corpse"] = HandleLootCorpse,
        ["board_open"] = HandleBoardOpen,
        ["open_board"] = HandleBoardOpen,
        ["friend_list"] = HandleFriendList,
        ["friend_result"] = HandleFriendResult,
        ["attack_cooldown"] = HandleAttackCooldown,
        ["skill_cooldown"] = HandleAttackCooldown,
        ["projectile_spawn"] = HandleProjectileSpawn,
        ["player_attack"] = HandlePlayerAttack,
        ["player_facing"] = HandlePlayerFacing,
        ["projectile_hit"] = HandleProjectileHit,
        ["mail_list"] = HandleMailList,
        ["mail_detail"] = HandleMailDetail,
        ["mail_unread"] = HandleMailUnread,
        ["mail_result"] = HandleMailResult,
        ["storage_open"] = HandleStorageOpen,
        ["storage_update"] = HandleStorageUpdate,
        ["character_list"] = HandleCharacterList,
    };

    public static bool TryHandle(GameClient client, GameMessage message)
    {
        if (_handlers.TryGetValue(message.Type, out var handler))
        {
            handler(client, message);
            return true;
        }
        return false;
    }

    private static void HandleAuthResponse(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement authEl)
        {
            bool success = authEl.TryGetProperty("Success", out var s) && s.GetBoolean();
            string msg = authEl.TryGetProperty("Message", out var me) ? (me.GetString() ?? "") : "";
            c.RaiseSystemMessage(success ? msg : $"Ошибка: {msg}");
            if (success)
            {
                string? token = authEl.TryGetProperty("session_token", out var stEl) ? stEl.GetString() : null;
                Guid pid = authEl.TryGetProperty("player_id", out var pidEl) && pidEl.ValueKind == JsonValueKind.String ? Guid.Parse(pidEl.GetString() ?? Guid.Empty.ToString()) : Guid.Empty;
                c.SessionToken = token;
                c.PlayerId = pid;
                GameMain.Instance?.Network.SetSession(token ?? "", pid);

                if (authEl.TryGetProperty("characters", out var charsEl) && charsEl.ValueKind == JsonValueKind.Array)
                {
                    var slots = new List<CharacterSlot>();
                    foreach (var ch in charsEl.EnumerateArray())
                    {
                        slots.Add(new CharacterSlot
                        {
                            Name = ch.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Level = ch.TryGetProperty("level", out var l) ? l.GetInt32() : 1,
                            ClassName = ch.TryGetProperty("className", out var cl) ? cl.GetString() ?? "" : "",
                            Zone = ch.TryGetProperty("zone", out var z) ? z.GetString() ?? "" : ""
                        });
                    }
                    c.RaiseCharacterListUpdated(slots.ToArray());
                }
                else if (pid != Guid.Empty)
                {
                    _ = c.SendAsync("skills_request", null);
                }
            }
        }
    }

    private static void HandleWelcome(GameClient c, GameMessage m)
    {
        var wel = m.Deserialize<WelcomeData>();
        c.PlayerName = wel?.PlayerName ?? "Игрок";
        c.PlayerClass = wel?.ClassName ?? "";
        c.RaiseWelcomeReceived();
    }

    private static void HandleMapUpdate(GameClient c, GameMessage m)
    {
        var map = m.Deserialize<WorldMap>();
        if (map != null)
        {
            c.CurrentMap = map;
            c.RaiseMapUpdated(map);
        }
    }

    private static void HandleChat(GameClient c, GameMessage m)
    {
        var chat = m.Deserialize<ChatData>();
        if (chat != null)
        {
            string channel = chat.Channel ?? "System";
            c.RaiseChatReceived(channel, chat.Name ?? "Система", chat.Text ?? "", chat.IsAdmin);
        }
    }

    private static void HandleError(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement errEl)
        {
            string text = errEl.TryGetProperty("Message", out var me) ? (me.GetString() ?? "Неизвестная ошибка") : "Неизвестная ошибка";
            c.RaiseErrorReceived(text);
            c.RaiseSystemMessage($"[Ошибка] {text}");
        }
    }

    private static void HandleStatusResponse(GameClient c, GameMessage m)
    {
        var st = m.Deserialize<StatusData>();
        if (st != null)
        {
            c.Status = st;
            c.PlayerLevel = st.Level;
            if (st.Health > 0) c.IsDead = false;
            c.RaiseStatusUpdated(st);
            c.RaiseStatusDetailsUpdated(st);
        }
    }

    private static void HandleInventoryResponse(GameClient c, GameMessage m)
    {
        var inv = m.Deserialize<InventoryData>();
        if (inv != null)
        {
            c.Inventory = inv;
            c.RaiseInventoryUpdated(inv);
        }
    }

    private static void HandleQuestLog(GameClient c, GameMessage m)
    {
        var log = m.Deserialize<QuestLogData>();
        c.AvailableQuests = log?.Available ?? new List<QuestInfo>();
        c.ActiveQuests = log?.Active ?? new List<QuestInfo>();
        c.RaiseQuestLogUpdated(c.AvailableQuests, c.ActiveQuests);
    }

    private static void HandleZoneTransition(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement ztEl)
        {
            string zoneId = ztEl.TryGetProperty("ZoneId", out var zi) ? zi.GetString() ?? BalanceStatic.MainZoneId : BalanceStatic.MainZoneId;
            string zoneName = ztEl.TryGetProperty("ZoneName", out var zn) ? zn.GetString() ?? zoneId : zoneId;
            bool pvp = ztEl.TryGetProperty("PvPEnabled", out var pv) && pv.GetBoolean();
            c.RaiseZoneChanged(zoneId, zoneName, pvp);

            if (ztEl.TryGetProperty("TileData", out var tdEl) && tdEl.ValueKind == JsonValueKind.String)
            {
                string? b64 = tdEl.GetString();
                if (!string.IsNullOrEmpty(b64))
                {
                    byte[] tileData = Convert.FromBase64String(b64);
                    // Размер карты в клетках (Width/Height) — если сервер его не прислал,
                    // откатываемся на размеры тайла (старые серверы).
                    int mapW = ztEl.TryGetProperty("Width", out var wEl) ? wEl.GetInt32() : 0;
                    int mapH = ztEl.TryGetProperty("Height", out var hEl) ? hEl.GetInt32() : 0;
                    int tw = ztEl.TryGetProperty("TileWidth", out var twEl) ? twEl.GetInt32() : 32;
                    int th = ztEl.TryGetProperty("TileHeight", out var thEl) ? thEl.GetInt32() : 32;
                    string tilesetId = ztEl.TryGetProperty("TilesetId", out var tsEl) ? tsEl.GetString() ?? zoneId : zoneId;
                    int tileSize = ztEl.TryGetProperty("TileSize", out var tszEl) ? tszEl.GetInt32() : 32;
                    int w = mapW > 0 ? mapW : tw;
                    int h = mapH > 0 ? mapH : th;
                    c.RaiseTileDataReceived(tileData, w, h, tilesetId, tileSize);

                    if (ztEl.TryGetProperty("ObstacleData", out var odEl) && odEl.ValueKind == JsonValueKind.String)
                    {
                        string? ob64 = odEl.GetString();
                        if (!string.IsNullOrEmpty(ob64))
                            c.RaiseObstacleDataReceived(Convert.FromBase64String(ob64), w, h);
                    }

                    if (ztEl.TryGetProperty("ObjectData", out var objEl) && objEl.ValueKind == JsonValueKind.String)
                    {
                        string? obj64 = objEl.GetString();
                        if (!string.IsNullOrEmpty(obj64))
                        {
                            string objectTilesetId = ztEl.TryGetProperty("ObjectTilesetId", out var otsEl) ? otsEl.GetString() ?? "" : "";
                            int objectTileSize = ztEl.TryGetProperty("ObjectTileWidth", out var otwEl) ? otwEl.GetInt32() : tw;
                            c.RaiseObjectLayerDataReceived(Convert.FromBase64String(obj64), w, h, objectTilesetId, objectTileSize);
                        }
                    }
                }
            }
        }
    }

    private static void HandleShopResponse(GameClient c, GameMessage m)
    {
        var shop = m.Deserialize<ShopData>();
        if (shop != null)
            c.RaiseShopUpdated(shop);
    }

    private static void HandleTradeOpen(GameClient c, GameMessage m)
    {
        var open = m.Deserialize<TradeOpenData>();
        if (open != null)
            c.RaiseTradeOpened(open);
    }

    private static void HandleTradeOfferUpdate(GameClient c, GameMessage m)
    {
        var offer = m.Deserialize<TradeOfferData>();
        if (offer != null)
            c.RaiseTradeOfferUpdated(offer);
    }

    private static void HandleTradeConfirmUpdate(GameClient c, GameMessage m)
    {
        var conf = m.Deserialize<TradeConfirmData>();
        if (conf != null)
            c.RaiseTradeConfirmUpdated(conf);
    }

    private static void HandleTradeComplete(GameClient c, GameMessage m)
    {
        var done = m.Deserialize<TradeCompleteData>();
        if (done != null)
            c.RaiseTradeCompleted(done);
    }

    private static void HandleTradeClose(GameClient c, GameMessage m)
    {
        string msg = "Обмен отменён.";
        if (m.Data is JsonElement el && el.TryGetProperty("Message", out var mEl))
            msg = mEl.GetString() ?? msg;
        c.RaiseTradeClosed(msg);
    }

    private static void HandleTradeDeclined(GameClient c, GameMessage m)
    {
        string msg = "Игрок отказался от обмена.";
        if (m.Data is JsonElement el && el.TryGetProperty("Message", out var mEl))
            msg = mEl.GetString() ?? msg;
        c.RaiseTradeClosed(msg);
    }

    private static void HandleDialogueOpen(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement dlgEl)
        {
            string npcId = dlgEl.TryGetProperty("NpcId", out var nid) ? (nid.GetString() ?? "") : "";
            string speaker = dlgEl.TryGetProperty("Speaker", out var sp) ? (sp.GetString() ?? "") : "";
            string text = dlgEl.TryGetProperty("Text", out var tx) ? (tx.GetString() ?? "") : "";
            var choices = new List<(string, int)>();
            if (dlgEl.TryGetProperty("Choices", out var carr) && carr.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (var ce in carr.EnumerateArray())
                {
                    string ct = ce.TryGetProperty("Text", out var ctP) ? (ctP.GetString() ?? "") : "";
                    choices.Add((ct, idx));
                    idx++;
                }
            }
            c.RaiseDialogueOpened(npcId, speaker, text, choices);
        }
    }

    private static void HandleDialogueClose(GameClient c, GameMessage m)
    {
        c.RaiseDialogueClosed();
    }

    private static void HandleDamage(GameClient c, GameMessage m)
    {
        if (m.Data is not JsonElement dmgEl) return;

        int amount = dmgEl.TryGetProperty("Amount", out var am) ? am.GetInt32() : 0;
        bool isCrit = dmgEl.TryGetProperty("IsCrit", out var ic) && ic.GetBoolean();
        bool isSkill = dmgEl.TryGetProperty("IsSkill", out var skillEl) && skillEl.GetBoolean();
        int x = dmgEl.TryGetProperty("X", out var xp) ? xp.GetInt32() : 0;
        int y = dmgEl.TryGetProperty("Y", out var yp) ? yp.GetInt32() : 0;
        string target = dmgEl.TryGetProperty("Target", out var tg) ? (tg.GetString() ?? "") : "";
        string result = dmgEl.TryGetProperty("Result", out var rs) ? (rs.GetString() ?? "") : "";

        var (color, text, crit) = GetDamageText(amount, result, isSkill, isCrit, target);
        Logger.Debug($"FLT dmg argb={color:X8} text={text} crit={crit}");
        c.RaiseFloatingText(x, y, text, color, crit);
    }

    private static readonly Dictionary<string, (uint Color, string Text)> _damageZeroTable = new()
    {
        ["miss"]      = (0xFFAAAAAAu, "Промах"),
        ["parry"]     = (0xFF66CCFFu, "Парирование"),
        ["block"]     = (0xFFFFCC44u, "Блок"),
        ["returning"] = (0xFF8888FFu, "Возвращение"),
    };

    private static (uint Color, string Text, bool Crit) GetDamageText(int amount, string result,
        bool isSkill, bool isCrit, string target)
    {
        if (amount <= 0)
        {
            _damageZeroTable.TryGetValue(result, out var entry);
            return (entry.Color != 0 ? entry.Color : 0xFFAAAAAAu,
                    entry.Text ?? "Промах", false);
        }
        if (isSkill)
            return (isCrit ? 0xFFFF6600u : 0xFFFFDD44u, $"-{amount}" + (isCrit ? "!" : ""), isCrit);
        if (isCrit)
            return (target == "player" ? 0xFFFFD040u : 0xFF40FF80u, $"-{amount}!", true);
        return (target == "player" ? 0xFFF06040u : 0xFF30CC60u, $"-{amount}", false);
    }

    private static void HandleHeal(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement healEl)
        {
            int amount = healEl.TryGetProperty("Amount", out var ham) ? ham.GetInt32() : 0;
            int x = healEl.TryGetProperty("X", out var hxp) ? hxp.GetInt32() : 0;
            int y = healEl.TryGetProperty("Y", out var hyp) ? hyp.GetInt32() : 0;
            Logger.Debug($"FLT heal argb={0xFF40E060u:X8} text=+{amount}");
            c.RaiseFloatingText(x, y, "+" + amount, 0xFF40E060u, false);
        }
    }

    private static void HandleManaRegen(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement manaEl)
        {
            int amount = manaEl.TryGetProperty("Amount", out var mam) ? mam.GetInt32() : 0;
            int x = manaEl.TryGetProperty("X", out var mxp) ? mxp.GetInt32() : 0;
            int y = manaEl.TryGetProperty("Y", out var myp) ? myp.GetInt32() : 0;
            Logger.Debug($"FLT mana_regen argb={0xFF60A0FFu:X8} text=+{amount}");
            c.RaiseFloatingText(x, y, "+" + amount, 0xFF60A0FFu, false);
        }
    }

    private static void HandlePlayerDeath(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement deathEl)
        {
            int lostGold = deathEl.TryGetProperty("LostGold", out var lgEl) ? lgEl.GetInt32() : 0;
            c.IsDead = true;
            c.DeathLostGold = lostGold;
            c.RaisePlayerDeath(lostGold);
        }
    }

    private static void HandleCombatState(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement cs)
        {
            bool inCombat = cs.TryGetProperty("InCombat", out var ic2) && ic2.GetBoolean();
            string? tName = cs.TryGetProperty("TargetName", out var tn) ? tn.GetString() : null;
            string? tId = cs.TryGetProperty("TargetId", out var ti) ? ti.GetString() : null;
            int tHp = cs.TryGetProperty("TargetHp", out var th) ? th.GetInt32() : 0;
            int tMaxHp = cs.TryGetProperty("TargetMaxHp", out var tmh) ? tmh.GetInt32() : 0;
            c.RaiseCombatStateUpdated(inCombat, tName, tHp, tMaxHp, tId);
            if (!inCombat)
            {
                c.RaiseTargetDebuffsUpdated(null);
            }
            else if (cs.TryGetProperty("TargetDebuffs", out var tdArr) && tdArr.ValueKind == JsonValueKind.Array)
            {
                var tDebuffs = new List<DebuffInfo>();
                foreach (var el in tdArr.EnumerateArray())
                    tDebuffs.Add(new DebuffInfo
                    {
                        Type = el.TryGetProperty("Type", out var dt) ? dt.GetString() ?? "" : "",
                        DisplayName = el.TryGetProperty("DisplayName", out var dn) ? dn.GetString() ?? "" : "",
                        Description = el.TryGetProperty("Description", out var dd2) ? dd2.GetString() ?? "" : "",
                        Value = el.TryGetProperty("Value", out var dv) ? dv.GetDouble() : 0,
                        RemainingMs = el.TryGetProperty("RemainingMs", out var dr) ? dr.GetInt32() : 0,
                        DurationMs = el.TryGetProperty("DurationMs", out var dd) ? dd.GetInt32() : 0
                    });
                c.RaiseTargetDebuffsUpdated(tDebuffs);
            }
        }
    }

    private static void HandleTargetDebuffUpdate(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement tdu && tdu.TryGetProperty("Debuffs", out var debArr) && debArr.ValueKind == JsonValueKind.Array)
        {
            var debuffs = new List<DebuffInfo>();
            foreach (var el in debArr.EnumerateArray())
                debuffs.Add(new DebuffInfo
                {
                    Type = el.TryGetProperty("Type", out var dt) ? dt.GetString() ?? "" : "",
                    DisplayName = el.TryGetProperty("DisplayName", out var dn) ? dn.GetString() ?? "" : "",
                    Description = el.TryGetProperty("Description", out var ddsc) ? ddsc.GetString() ?? "" : "",
                    Value = el.TryGetProperty("Value", out var dv) ? dv.GetDouble() : 0,
                    RemainingMs = el.TryGetProperty("RemainingMs", out var dr) ? dr.GetInt32() : 0,
                    DurationMs = el.TryGetProperty("DurationMs", out var dd) ? dd.GetInt32() : 0
                });
            c.RaiseTargetDebuffsUpdated(debuffs);
        }
    }

    private static void HandleCancelTarget(GameClient c, GameMessage m)
    {
        c.RaiseTargetCleared("cleared");
    }

    private static void HandlePartyInviteSent(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement pis && pis.TryGetProperty("TargetName", out var ptn))
            c.RaiseChatReceived("Party", "Пати", $"Приглашение отправлено {ptn.GetString()}", false);
    }

    private static void HandlePartyInviteDeclined(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement pdec && pdec.TryGetProperty("TargetName", out var pdn))
            c.RaiseChatReceived("Party", "Пати", $"{pdn.GetString()} отказал(а) от приглашения", false);
    }

    private static void HandleTradeRequestSent(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement trs && trs.TryGetProperty("TargetName", out var trn))
            c.RaiseChatReceived("System", "Трейд", $"Запрос обмена отправлен {trn.GetString()}", false);
    }

    private static void HandlePartyUpdate(GameClient c, GameMessage m)
    {
        var party = m.Deserialize<PartyInfo>();
        if (party != null)
            c.RaisePartyUpdated(party);
    }

    private static void HandlePartyDisbanded(GameClient c, GameMessage m)
    {
        c.RaisePartyDisbanded();
    }

    private static void HandlePartyInviteReceived(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement pir)
        {
            string? inviterName = pir.TryGetProperty("InviterName", out var invEl) ? invEl.GetString() : null;
            if (inviterName != null)
                c.RaisePartyInviteReceived(inviterName, "");
        }
    }

    private static void HandleTradeRequestReceived(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement trEl)
        {
            string? inviterName = trEl.TryGetProperty("InviterName", out var invN) ? invN.GetString() : null;
            if (inviterName != null)
                c.RaiseTradeRequestReceived(inviterName);
        }
    }

    private static void HandleSkillsResponse(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement sk)
        {
            var list = new List<ClientSkillInfo>();
            if (sk.TryGetProperty("Skills", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in arr.EnumerateArray())
                {
                    list.Add(new ClientSkillInfo
                    {
                        Id = e.TryGetProperty("Id", out var id) ? (id.GetString() ?? "") : "",
                        Name = e.TryGetProperty("Name", out var nm) ? (nm.GetString() ?? "") : "",
                        Description = e.TryGetProperty("Description", out var ds) ? (ds.GetString() ?? "") : "",
                        Type = e.TryGetProperty("Type", out var ty) ? (ty.GetString() ?? "") : "",
                        MpCost = e.TryGetProperty("MpCost", out var mp) ? mp.GetInt32() : 0,
                        CooldownMs = e.TryGetProperty("CooldownMs", out var cd) ? cd.GetInt32() : 0,
                        DamageMultiplier = e.TryGetProperty("DamageMultiplier", out var dm) ? dm.GetDouble() : 1,
                        MinLevel = e.TryGetProperty("MinLevel", out var ml) ? ml.GetInt32() : 1,
                        SkillPointCost = e.TryGetProperty("SkillPointCost", out var sp) ? sp.GetInt32() : 1,
                        ParentId = e.TryGetProperty("ParentId", out var pa) && pa.ValueKind != JsonValueKind.Null ? pa.GetString() : null,
                        Tier = e.TryGetProperty("Tier", out var ti) ? ti.GetInt32() : 1,
                        IconName = e.TryGetProperty("IconName", out var ic) && ic.ValueKind != JsonValueKind.Null ? ic.GetString() : null,
                        MaxRank = e.TryGetProperty("MaxRank", out var mr) ? mr.GetInt32() : 3
                    });
                }
            }
            var learned = new List<string>();
            if (sk.TryGetProperty("LearnedSkills", out var ls) && ls.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in ls.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && e.GetString() is string sid)
                        learned.Add(sid);
            }
            var ranks = new Dictionary<string, int>();
            if (sk.TryGetProperty("SkillRanks", out var sr) && sr.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in sr.EnumerateObject())
                    if (p.Value.TryGetInt32(out int r)) ranks[p.Name] = r;
            }
            int skillPts = sk.TryGetProperty("SkillPoints", out var spt) ? spt.GetInt32() : 0;
            c.Ui(() =>
            {
                foreach (var s in list)
                {
                    s.Learned = learned.Contains(s.Id);
                    s.Rank = ranks.TryGetValue(s.Id, out int r) ? r : 1;
                }
                c._skillPoints = skillPts;
            });
            c.RaiseSkillsUpdated(list);
        }
    }

    private static void HandleHotbar(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement hb)
        {
            var slots = new string?[10];
            if (hb.TryGetProperty("Slots", out var sarr) && sarr.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var e in sarr.EnumerateArray())
                {
                    if (i >= 10) break;
                    slots[i++] = e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                }
            }
            c.RaiseHotbarUpdated(slots);
        }
    }

    private static void HandleLootCorpse(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement lootEl)
        {
            string corpseId = lootEl.TryGetProperty("CorpseId", out var cid) ? (cid.GetString() ?? "") : "";
            string monsterName = lootEl.TryGetProperty("MonsterName", out var mn) ? (mn.GetString() ?? "") : "";
            int gold = lootEl.TryGetProperty("Gold", out var g) ? g.GetInt32() : 0;
            int dmgPct = lootEl.TryGetProperty("DamagePercent", out var dp) ? dp.GetInt32() : 0;
            var items = new List<LootItemInfo>();
            if (lootEl.TryGetProperty("Items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var iel in itemsEl.EnumerateArray())
                {
                    items.Add(ParseLootItem(iel));
                }
            }
            c.RaiseLootReceived(corpseId, monsterName, dmgPct, items, gold);
        }
    }

    private static LootItemInfo ParseLootItem(JsonElement el)
    {
        int I(string k, int d = 0) => el.TryGetProperty(k, out var p) ? p.GetInt32() : d;
        double D(string k, double d = 0) => el.TryGetProperty(k, out var p) ? p.GetDouble() : d;
        bool B(string k) => el.TryGetProperty(k, out var p) && p.GetBoolean();
        string S(string k, string d = "") => el.TryGetProperty(k, out var p) ? (p.GetString() ?? d) : d;
        return new LootItemInfo
        {
            Id = S("Id"), TemplateId = S("TemplateId"), Name = S("Name"), Type = S("Type"),
            WeaponSubtype = S("WeaponSubtype"), Quantity = I("Quantity", 1), Value = I("Value"),
            Description = S("Description"), MaxHealthBonus = I("MaxHealthBonus"),
            HealAmount = I("HealAmount"), RestoreMana = I("RestoreMana"), MaxStack = I("MaxStack", 10),
            BonusStrength = I("BonusStrength"), BonusEndurance = I("BonusEndurance"),
            BonusAgility = I("BonusAgility"), BonusCunning = I("BonusCunning"),
            BonusIntellect = I("BonusIntellect"), BonusWisdom = I("BonusWisdom"),
            BonusPhysAttack = I("BonusPhysAttack"), BonusMagAttack = I("BonusMagAttack"),
            BonusDefense = I("BonusDefense"), BonusResistance = I("BonusResistance"),
            BonusCritChance = D("BonusCritChance"), BonusCritDamage = D("BonusCritDamage"),
            BonusEvadeChance = D("BonusEvadeChance"), BonusAttackSpeed = D("BonusAttackSpeed"),
            BonusBlockChance = D("BonusBlockChance"), BonusParryChance = D("BonusParryChance"),
            DamageType = S("DamageType"), RequiredLevel = I("RequiredLevel"),
            DamageMin = I("DamageMin"), DamageMax = I("DamageMax"),
            AttackSpeedModifier = D("AttackSpeedModifier", 1.0), TwoHanded = B("TwoHanded"),
            AttackRange = I("AttackRange", 1)
        };
    }

    private static void HandleBoardOpen(GameClient c, GameMessage m)
    {
        c.RaiseBoardOpened();
    }

    private static void HandleFriendList(GameClient c, GameMessage m)
    {
        var fl = m.Deserialize<FriendListData>();
        if (fl != null)
            c.RaiseFriendListUpdated(fl.Friends);
    }

    private static void HandleFriendResult(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement frEl)
        {
            bool ok = frEl.TryGetProperty("Success", out var okEl) && okEl.GetBoolean();
            string msg = frEl.TryGetProperty("Message", out var mEl) ? (mEl.GetString() ?? "") : "";
            c.RaiseFriendResultReceived(ok, msg);
        }
    }

    private static void HandleAttackCooldown(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement ac)
        {
            string? sid = ac.TryGetProperty("SkillId", out var sidEl) ? sidEl.GetString() : null;
            int rem = ac.TryGetProperty("RemainingMs", out var remEl) ? remEl.GetInt32() : 0;
            int total = ac.TryGetProperty("TotalMs", out var totEl) ? totEl.GetInt32() : 0;
            if (sid != null)
                c.RaiseAttackCooldownUpdated(sid, rem, total);
        }
    }

    private static void HandleProjectileSpawn(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement ps)
        {
            string id = ps.TryGetProperty("Id", out var psId) ? (psId.GetString() ?? "") : "";
            double sx = ps.TryGetProperty("StartX", out var psSx) ? psSx.GetDouble() : 0;
            double sy = ps.TryGetProperty("StartY", out var psSy) ? psSy.GetDouble() : 0;
            double tx = ps.TryGetProperty("TargetX", out var psTx) ? psTx.GetDouble() : 0;
            double ty = ps.TryGetProperty("TargetY", out var psTy) ? psTy.GetDouble() : 0;
            string vt = ps.TryGetProperty("VisualType", out var psVt) ? (psVt.GetString() ?? "arrow") : "arrow";
            int fm = ps.TryGetProperty("FlightMs", out var psFm) ? psFm.GetInt32() : 350;
            c.RaiseProjectileSpawned(id, sx, sy, tx, ty, vt, fm);
        }
    }

    private static void HandlePlayerAttack(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement paEl)
        {
            string hand = paEl.TryGetProperty("Hand", out var hd) ? (hd.GetString() ?? "main") : "main";
            string playerName = paEl.TryGetProperty("PlayerName", out var pn) ? (pn.GetString() ?? "") : "";
            string? skillId = paEl.TryGetProperty("SkillId", out var skid) && skid.ValueKind != JsonValueKind.Null ? skid.GetString() : null;
            int? targetX = paEl.TryGetProperty("TargetX", out var tx) ? tx.GetInt32() : null;
            int? targetY = paEl.TryGetProperty("TargetY", out var ty) ? ty.GetInt32() : null;
            int? buffDurationMs = paEl.TryGetProperty("BuffDurationMs", out var bd) ? bd.GetInt32() : null;
            if (!string.IsNullOrEmpty(playerName) && playerName != c.PlayerName)
                c.RaiseRemotePlayerAttack(playerName, hand, skillId, targetX, targetY, buffDurationMs);
            else
                c.RaisePlayerAttackPerformed(hand, skillId, targetX, targetY);
        }
    }

    private static void HandlePlayerFacing(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement pfEl)
        {
            string pfName = pfEl.TryGetProperty("PlayerName", out var pfn) ? (pfn.GetString() ?? "") : "";
            string pfFacing = pfEl.TryGetProperty("Facing", out var pff) ? (pff.GetString() ?? "down") : "down";
            if (!string.IsNullOrEmpty(pfName) && pfName != c.PlayerName)
                c.RaiseRemotePlayerFacing(pfName, pfFacing);
        }
    }

    private static void HandleProjectileHit(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement ph)
        {
            string hid = ph.TryGetProperty("Id", out var phId) ? (phId.GetString() ?? "") : "";
            double hx = ph.TryGetProperty("X", out var phX) ? phX.GetDouble() : 0;
            double hy = ph.TryGetProperty("Y", out var phY) ? phY.GetDouble() : 0;
            c.RaiseProjectileHit(hid, hx, hy);
        }
    }

    private static void HandleMailList(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement mlData)
        {
            var messages = new List<MailEntry>();
            if (mlData.TryGetProperty("Mails", out var msgs))
            {
                foreach (var mm in msgs.EnumerateArray())
                {
                    messages.Add(new MailEntry
                    {
                        Id = mm.TryGetProperty("Id", out var mid) ? mid.GetInt32() : 0,
                        SenderName = mm.TryGetProperty("SenderName", out var ms) ? ms.GetString() ?? "" : "",
                        RecipientName = mm.TryGetProperty("RecipientName", out var mr) ? mr.GetString() ?? "" : "",
                        Subject = mm.TryGetProperty("Subject", out var msu) ? msu.GetString() ?? "" : "",
                        Body = mm.TryGetProperty("Body", out var mb) ? mb.GetString() ?? "" : "",
                        GoldAmount = mm.TryGetProperty("GoldAmount", out var mg) ? mg.GetInt32() : 0,
                        Attachments = ParseAttachments(mm),
                        SentAt = mm.TryGetProperty("SentAt", out var mst) ? mst.GetString() ?? "" : "",
                        ReadAt = mm.TryGetProperty("ReadAt", out var mrd) ? mrd.GetString() ?? "" : "",
                        TakenAt = mm.TryGetProperty("TakenAt", out var mtn) ? mtn.GetString() ?? "" : ""
                    });
                }
            }
            string folder = mlData.TryGetProperty("Folder", out var mf) ? mf.GetString() ?? "" : "";
            c.RaiseMailListReceived(folder, messages);
        }
    }

    private static void HandleMailDetail(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement mdData)
        {
            var msg = new MailEntry
            {
                Id = mdData.TryGetProperty("Id", out var did) ? did.GetInt32() : 0,
                SenderName = mdData.TryGetProperty("SenderName", out var ds) ? ds.GetString() ?? "" : "",
                RecipientName = mdData.TryGetProperty("RecipientName", out var dr) ? dr.GetString() ?? "" : "",
                Subject = mdData.TryGetProperty("Subject", out var dsu) ? dsu.GetString() ?? "" : "",
                Body = mdData.TryGetProperty("Body", out var db) ? db.GetString() ?? "" : "",
                GoldAmount = mdData.TryGetProperty("GoldAmount", out var dg) ? dg.GetInt32() : 0,
                Attachments = ParseAttachments(mdData),
                SentAt = mdData.TryGetProperty("SentAt", out var dst) ? dst.GetString() ?? "" : "",
                ReadAt = mdData.TryGetProperty("ReadAt", out var drd) ? drd.GetString() ?? "" : "",
                TakenAt = mdData.TryGetProperty("TakenAt", out var dtn) ? dtn.GetString() ?? "" : ""
            };
            c.RaiseMailDetailReceived(msg);
        }
    }

    private static List<MailAttachment> ParseAttachments(JsonElement mailEl)
    {
        var result = new List<MailAttachment>();
        if (mailEl.TryGetProperty("Attachments", out var atts) && atts.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in atts.EnumerateArray())
            {
                result.Add(new MailAttachment
                {
                    TemplateId = a.TryGetProperty("TemplateId", out var ti) ? ti.GetString() ?? "" : "",
                    Name = a.TryGetProperty("Name", out var nm) ? nm.GetString() ?? "" : "",
                    Type = a.TryGetProperty("Type", out var ty) ? ty.GetString() ?? "" : "",
                    Quantity = a.TryGetProperty("Quantity", out var qq) ? qq.GetInt32() : 0,
                    WeaponSubtype = a.TryGetProperty("WeaponSubtype", out var ws) ? ws.GetString() ?? "" : "",
                    HealAmount = a.TryGetProperty("HealAmount", out var ha) ? ha.GetInt32() : 0,
                    RestoreMana = a.TryGetProperty("RestoreMana", out var rm) ? rm.GetInt32() : 0
                });
            }
        }
        else if (mailEl.TryGetProperty("ItemId", out var iid) && !string.IsNullOrEmpty(iid.GetString()))
        {
            result.Add(new MailAttachment
            {
                TemplateId = iid.GetString() ?? "",
                Name = mailEl.TryGetProperty("ItemName", out var inm) ? inm.GetString() ?? "" : "",
                Type = mailEl.TryGetProperty("ItemType", out var ity) ? ity.GetString() ?? "" : "",
                Quantity = mailEl.TryGetProperty("ItemQuantity", out var iqy) ? iqy.GetInt32() : 0
            });
        }
        return result;
    }

    private static void HandleMailUnread(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement muData)
        {
            int count = muData.TryGetProperty("Count", out var uc) ? uc.GetInt32() : 0;
            c.RaiseMailUnreadReceived(count);
        }
    }

    private static void HandleMailResult(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement mrData)
        {
            bool ok = mrData.TryGetProperty("Success", out var msucc) && msucc.GetBoolean();
            string err = mrData.TryGetProperty("Message", out var merr) ? merr.GetString() ?? "" : "";
            c.RaiseMailResultReceived(ok, err);
        }
    }

    private static void HandleStorageOpen(GameClient c, GameMessage m)
    {
        var data = m.Deserialize<StorageData>();
        if (data != null)
            c.RaiseStorageOpened(data);
    }

    private static void HandleStorageUpdate(GameClient c, GameMessage m)
    {
        var data = m.Deserialize<StorageData>();
        if (data != null)
            c.RaiseStorageUpdated(data);
    }

    private static void HandleCharacterList(GameClient c, GameMessage m)
    {
        if (m.Data is JsonElement el)
        {
            if (el.TryGetProperty("Error", out var errEl))
            {
                c.RaiseSystemMessage(errEl.GetString() ?? "Ошибка");
                return;
            }

            var slots = new List<CharacterSlot>();
            if (el.TryGetProperty("characters", out var charsEl) && charsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var ch in charsEl.EnumerateArray())
                {
                    slots.Add(new CharacterSlot
                    {
                        Name = ch.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        Level = ch.TryGetProperty("level", out var l) ? l.GetInt32() : 1,
                        ClassName = ch.TryGetProperty("className", out var cl) ? cl.GetString() ?? "" : "",
                        Zone = ch.TryGetProperty("zone", out var z) ? z.GetString() ?? "" : ""
                    });
                }
            }
            c.RaiseCharacterListUpdated(slots.ToArray());
        }
    }
}
