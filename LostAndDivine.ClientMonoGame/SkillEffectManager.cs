using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.Shared;

namespace LostAndDivine.ClientMonoGame;

/// <summary>Одна активная анимация навыка на карте.</summary>
public sealed class SkillAnim
{
    public string TextureKey = "";
    public float MapX;
    public float MapY;
    public float ElapsedMs;
    public float FrameMs;
    public int FrameCount;
    public bool OnPlayer;
    public bool VerticalSheet;
    public bool Loop;
    public float DurationMs;
    public string? SourcePlayer;

    public bool Finished => !Loop && ElapsedMs >= FrameMs * FrameCount;
    public int CurrentFrame
    {
        get
        {
            if (FrameCount <= 0) return 0;
            int f = (int)(ElapsedMs / FrameMs);
            if (Loop) f %= FrameCount;
            return Math.Min(f, FrameCount - 1);
        }
    }

    public void Update(float dtMs) => ElapsedMs += dtMs;
}

public static class SkillEffectManager
{
    private static readonly List<SkillAnim> _active = new();
    private static readonly object _lock = new();

    private static readonly Dictionary<string, (string Key, int Frames, float Fps, bool OnPlayer)> _registry = new()
    {
        [SkillIds.StrongArm] = ("skill_anim_stronghand",    4, 12f, false),
        [SkillIds.Flurry]    = ("skill_anim_barrageofblows", 4,  6f, true),
        [SkillIds.Slash]     = ("skill_anim_cutting",        4, 12f, false),
        [SkillIds.HolyTrinity] = ("skill_anim_holytrinity",    4, 12f, false),
        [SkillIds.Duel]      = ("skill_anim_duel",           4, 12f, false),
    };

    public static bool IsOnPlayer(string skillId)
        => _registry.TryGetValue(skillId, out var c) && c.OnPlayer;

    public static void Spawn(string skillId, float mapX, float mapY, bool forceMap = false)
    {
        if (!_registry.TryGetValue(skillId, out var cfg)) return;

        var anim = new SkillAnim
        {
            TextureKey = cfg.Key,
            MapX = mapX,
            MapY = mapY,
            FrameCount = cfg.Frames,
            FrameMs = 1000f / cfg.Fps,
            OnPlayer = !forceMap && cfg.OnPlayer,
            VerticalSheet = true,
        };
        lock (_lock) _active.Add(anim);
    }

    public static void SpawnLooping(string skillId, float mapX, float mapY,
        string? sourcePlayer = null, float durationMs = 0, bool forceMap = false)
    {
        if (!_registry.TryGetValue(skillId, out var cfg)) return;

        lock (_lock)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var a = _active[i];
                if (a.Loop && a.TextureKey == cfg.Key)
                {
                    if (sourcePlayer == null || a.SourcePlayer == sourcePlayer)
                    {
                        _active.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        var anim = new SkillAnim
        {
            TextureKey = cfg.Key,
            MapX = mapX,
            MapY = mapY,
            FrameCount = cfg.Frames,
            FrameMs = 1000f / cfg.Fps,
            OnPlayer = !forceMap && cfg.OnPlayer,
            VerticalSheet = true,
            Loop = true,
            DurationMs = durationMs,
            SourcePlayer = sourcePlayer,
        };
        lock (_lock) _active.Add(anim);
    }

    public static void StopLooping(string skillId)
    {
        if (!_registry.TryGetValue(skillId, out var cfg)) return;
        lock (_lock)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
                if (_active[i].Loop && _active[i].TextureKey == cfg.Key)
                    _active.RemoveAt(i);
        }
    }

    public static void StopLoopingForPlayer(string skillId, string playerName)
    {
        if (!_registry.TryGetValue(skillId, out var cfg)) return;
        lock (_lock)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var a = _active[i];
                if (a.Loop && a.TextureKey == cfg.Key && a.SourcePlayer == playerName)
                    _active.RemoveAt(i);
            }
        }
    }

    public static bool HasLooping(string skillId, string? sourcePlayer = null)
    {
        if (!_registry.TryGetValue(skillId, out var cfg)) return false;
        lock (_lock)
        {
            foreach (var a in _active)
                if (a.Loop && a.TextureKey == cfg.Key)
                {
                    if (sourcePlayer == null || a.SourcePlayer == sourcePlayer)
                        return true;
                }
        }
        return false;
    }

    public static void Update(float dtMs)
    {
        lock (_lock)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                _active[i].Update(dtMs);
                var a = _active[i];
                if (a.Loop)
                {
                    if (a.DurationMs > 0 && a.ElapsedMs >= a.DurationMs)
                        _active.RemoveAt(i);
                }
                else if (a.Finished)
                {
                    _active.RemoveAt(i);
                }
            }
        }
    }

    public static void Draw(SpriteBatch sb, float gridOX, float gridOY,
        int viewStartX, int viewStartY, float cellW, float cellH)
    {
        lock (_lock)
        {
            foreach (var a in _active)
            {
                if (a.OnPlayer) continue;
                DrawAnim(sb, a, gridOX, gridOY, viewStartX, viewStartY, cellW, cellH);
            }
        }
    }

    public static void DrawOnPlayer(SpriteBatch sb, float screenCenterX, float screenCenterY, float cellW, float cellH)
    {
        lock (_lock)
        {
            foreach (var a in _active)
            {
                if (!a.OnPlayer) continue;
                DrawAnimAtScreen(sb, a, screenCenterX, screenCenterY, cellW, cellH);
            }
        }
    }

    private static void DrawAnim(SpriteBatch sb, SkillAnim a,
        float gridOX, float gridOY, int viewStartX, int viewStartY, float cellW, float cellH)
    {
        float sx = gridOX + (a.MapX - viewStartX) * cellW + cellW / 2f;
        float sy = gridOY + (a.MapY - viewStartY) * cellH + cellH / 2f;
        DrawAnimAtScreen(sb, a, sx, sy, cellW, cellH);
    }

    private static void DrawAnimAtScreen(SpriteBatch sb, SkillAnim a, float cx, float cy, float cellW, float cellH)
    {
        var tex = SpriteCache.Get(a.TextureKey);
        if (tex == null) return;

        int fw, fh;
        Rectangle src;
        if (a.VerticalSheet)
        {
            fw = tex.Width;
            fh = tex.Height / a.FrameCount;
            src = new Rectangle(0, a.CurrentFrame * fh, fw, fh);
        }
        else
        {
            fw = tex.Width / a.FrameCount;
            fh = tex.Height;
            src = new Rectangle(a.CurrentFrame * fw, 0, fw, fh);
        }

        float scale = Math.Max(cellW, cellH) * 1.8f / Math.Max(fw, fh);
        int dw = Math.Max(1, (int)(fw * scale));
        int dh = Math.Max(1, (int)(fh * scale));
        sb.Draw(tex, new Rectangle((int)cx - dw / 2, (int)cy - dh / 2, dw, dh), src, Color.White);
    }

    public static void Clear() { lock (_lock) _active.Clear(); }
}
