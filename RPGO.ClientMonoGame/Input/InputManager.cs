using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Data;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Input;

public class InputManager
{
    private List<ClientSkillInfo> _skills = new();
    private InventoryData? _inventory;
    private string?[] _hotbarSlots = new string?[10];

    public string?[] HotbarSlots => _hotbarSlots;

    public event Action<int, string>? HotbarActivated;

    public void SetSkills(List<ClientSkillInfo> skills) => _skills = skills;
    public void SetInventory(InventoryData inv) => _inventory = inv;
    public void UpdateHotbar(string?[] slots)
    {
        for (int i = 0; i < 10 && i < slots.Length; i++)
            _hotbarSlots[i] = slots[i];
    }

    public ClientSkillInfo? GetSkillByName(string name) =>
        _skills.FirstOrDefault(s => s.Name == name);

    public ClientSkillInfo? GetSkillById(string id) =>
        _skills.FirstOrDefault(s => s.Id == id);

    public Texture2D? GetHotbarIcon(int idx)
    {
        if (idx < 0 || idx >= _hotbarSlots.Length) return null;
        var slot = _hotbarSlots[idx];
        if (string.IsNullOrEmpty(slot)) return null;
        if (slot!.StartsWith("skill:"))
        {
            var skill = GetSkillByName(slot[6..]);
            if (skill != null)
            {
                var icon = !string.IsNullOrEmpty(skill.IconName) ? SpriteCache.Get(skill.IconName) : SpriteCache.ForItemType(skill.Type);
                return icon ?? SpriteCache.Get("skill");
            }
        }
        else
        {
            var name = slot!.StartsWith("item:") ? slot["item:".Length..] : slot;
            var item = GetItemByName(name);
            if (item != null)
                return SpriteCache.ForItemType(item.Type);
        }
        return null;
    }

    public Item? GetItemByName(string name) =>
        _inventory?.Items?.FirstOrDefault(i => i.Name == name);

    public int GetHotbarItemCount(int idx)
    {
        if (idx < 0 || idx >= _hotbarSlots.Length) return 0;
        var slot = _hotbarSlots[idx];
        if (string.IsNullOrEmpty(slot) || !slot!.StartsWith("item:")) return 0;
        var name = slot["item:".Length..];
        if (_inventory?.Items == null) return 0;
        // Суммируем Quantity по всем стакам с этим именем (а не число записей)
        return _inventory.Items.Where(i => i.Name == name).Sum(i => i.Quantity);
    }

    public void HandleHotbarKeys(KeyboardState keyboard, KeyboardState prevKeyboard)
    {
        for (int i = 0; i < 10; i++)
        {
            Keys key = i switch
            {
                0 => Keys.D1, 1 => Keys.D2, 2 => Keys.D3, 3 => Keys.D4, 4 => Keys.D5,
                5 => Keys.D6, 6 => Keys.D7, 7 => Keys.D8, 8 => Keys.D9, 9 => Keys.D0,
                _ => Keys.None
            };
            if (keyboard.IsKeyDown(key) && prevKeyboard.IsKeyUp(key))
            {
                var slotName = _hotbarSlots[i];
                if (!string.IsNullOrEmpty(slotName))
                    HotbarActivated?.Invoke(i, slotName);
            }
        }
    }

    private TimeSpan _lastMoveSent;

    // Последнее направление взгляда игрока ("down" | "up" | "left" | "right").
    public string Facing { get; private set; } = "down";

    public void HandleMovement(KeyboardState keyboard, KeyboardState prevKeyboard, GameClient client, MapRenderer mapRenderer)
    {
        int dx = 0, dy = 0;
        if (keyboard.IsKeyDown(Keys.W) || keyboard.IsKeyDown(Keys.Up)) dy = -1;
        if (keyboard.IsKeyDown(Keys.S) || keyboard.IsKeyDown(Keys.Down)) dy = 1;
        if (keyboard.IsKeyDown(Keys.A) || keyboard.IsKeyDown(Keys.Left)) dx = -1;
        if (keyboard.IsKeyDown(Keys.D) || keyboard.IsKeyDown(Keys.Right)) dx = 1;

        if (dx == 0 && dy == 0) return;

        // Обновляем направление взгляда по последнему нажатию (приоритет по горизонтали).
        if (dx < 0) Facing = "left";
        else if (dx > 0) Facing = "right";
        else if (dy < 0) Facing = "up";
        else Facing = "down";

        // Непрерывное движение при удержании клавиши с учётом серверного интервала
        int intervalMs = client.Status?.MoveIntervalMs ?? 500;
        var now = DateTime.UtcNow.TimeOfDay;
        if ((now - _lastMoveSent).TotalMilliseconds < intervalMs) return;
        _lastMoveSent = now;

        if (client.Status != null)
        {
            int targetX = client.Status.X + dx;
            int targetY = client.Status.Y + dy;
            Logger.Action($"Движение: ({targetX}, {targetY})");
            _ = client.SendAsync("move_to", new { X = targetX, Y = targetY });
        }
    }

    public void HandleMapClick(MouseState mouse, MouseState prevMouse, MapRenderer mapRenderer)
    {
        if (mouse.LeftButton == ButtonState.Pressed && prevMouse.LeftButton == ButtonState.Released)
        {
            // Координаты и размеры карты должны совпадать с GameScreen.Draw:
            // карта растянута на весь экран под топбаром.
            int topH = 40;
            int w = GameMain.Instance!.Graphics.PreferredBackBufferWidth;
            int h = GameMain.Instance!.Graphics.PreferredBackBufferHeight;
            int offsetX = 0;
            int offsetY = topH;
            int areaW = w;
            int areaH = h - topH;

            if (mouse.X >= offsetX && mouse.X < offsetX + areaW && mouse.Y >= offsetY && mouse.Y < offsetY + areaH)
            {
                mapRenderer.HandleClick(mouse.X, mouse.Y, offsetX, offsetY, areaW, areaH);
            }
        }
    }
}
