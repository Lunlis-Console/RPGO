using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Rendering;

public sealed class HazardAnim
{
    public Guid Id;
    public int X, Y;
    public HazardKind Kind;
    public bool IsTriggered;
    public float ElapsedMs;
    public bool AnimationPlayed;

    public void Update(float dtMs)
    {
        if (Kind == HazardKind.Acid)
        {
            ElapsedMs += dtMs;
        }
        else
        {
            if (IsTriggered && !AnimationPlayed)
            {
                ElapsedMs += dtMs;
                if (ElapsedMs >= 4 * FrameMs)
                {
                    AnimationPlayed = true;
                }
            }
        }
    }

    public bool Finished => Kind != HazardKind.Acid && AnimationPlayed;

    public string TextureKey => Kind switch
    {
        HazardKind.Acid => "hazard_retreat_acid",
        HazardKind.Smoke => "hazard_retreat_smoke",
        HazardKind.Snare => "hazard_retreat_snare",
        _ => "hazard_retreat_acid"
    };

    private const int FrameCount = 4;
    private const float FrameMs = 1000f / 6f;

    public int CurrentFrame
    {
        get
        {
            if (Kind == HazardKind.Acid)
            {
                int f = (int)(ElapsedMs / FrameMs);
                f %= FrameCount;
                return Math.Min(f, FrameCount - 1);
            }
            else
            {
                if (!IsTriggered)
                    return 0;
                int f = (int)(ElapsedMs / FrameMs);
                return Math.Min(f, FrameCount - 1);
            }
        }
    }
}

public static class HazardRenderer
{
    private static readonly List<HazardAnim> _active = new();
    private static readonly HashSet<Guid> _spent = new();
    private static readonly object _lock = new();

    public static void Sync(List<HazardPosition>? serverHazards)
    {
        lock (_lock)
        {
            if (serverHazards == null || serverHazards.Count == 0)
            {
                _active.Clear();
                _spent.Clear();
                return;
            }

            var serverIds = new HashSet<Guid>();
            foreach (var sh in serverHazards)
            {
                serverIds.Add(sh.Id);

                if (_spent.Contains(sh.Id))
                    continue;

                if (!Enum.TryParse<HazardKind>(sh.Kind, true, out var kind))
                    continue;

                var existing = _active.Find(a => a.Id == sh.Id);
                if (existing != null)
                {
                    existing.X = sh.X;
                    existing.Y = sh.Y;
                    if (sh.IsTriggered && !existing.IsTriggered)
                    {
                        existing.IsTriggered = true;
                        existing.ElapsedMs = 0;
                        existing.AnimationPlayed = false;
                    }
                }
                else
                {
                    _active.Add(new HazardAnim
                    {
                        Id = sh.Id,
                        X = sh.X,
                        Y = sh.Y,
                        Kind = kind,
                        IsTriggered = sh.IsTriggered,
                        ElapsedMs = 0,
                        AnimationPlayed = false
                    });
                }
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (!serverIds.Contains(_active[i].Id))
                    _active.RemoveAt(i);
            }

            _spent.RemoveWhere(id => !serverIds.Contains(id));
        }
    }

    public static void Update(float dtMs)
    {
        lock (_lock)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                _active[i].Update(dtMs);
                if (_active[i].Finished)
                {
                    _spent.Add(_active[i].Id);
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
                var tex = SpriteCache.Get(a.TextureKey);
                if (tex == null) continue;

                int fw = tex.Width;
                int fh = tex.Height / 4;
                int frame = a.CurrentFrame;
                var src = new Rectangle(0, frame * fh, fw, fh);

                float sx = gridOX + (a.X - viewStartX) * cellW;
                float sy = gridOY + (a.Y - viewStartY) * cellH;

                float scale = Math.Max(cellW, cellH) * 1.2f / Math.Max(fw, fh);
                int dw = Math.Max(1, (int)(fw * scale));
                int dh = Math.Max(1, (int)(fh * scale));

                sb.Draw(tex, new Rectangle((int)sx + (int)(cellW - dw) / 2, (int)sy + (int)(cellH - dh) / 2, dw, dh), src, Color.White);
            }
        }
    }

    public static void Clear() { lock (_lock) { _active.Clear(); _spent.Clear(); } }
}
