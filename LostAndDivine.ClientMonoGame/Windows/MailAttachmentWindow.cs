using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using LostAndDivine.ClientMonoGame.Rendering;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.ClientMonoGame.Windows;

/// <summary>
/// Окно выбора вложений для письма (по образцу окна склада):
/// слева инвентарь игрока, справа выбранные вложения.
/// Перенос: двойной клик, drag&drop, ПКМ (выбор количества), Shift+ПКМ (весь стак).
/// Обратно из вложений в инвентарь — те же действия.
/// </summary>
public class MailAttachmentWindow : GameWindow
{
    private List<Item> _inventoryItems = new();
    private List<MailAttachment> _attachments = new();

    private List<(Item item, int count)> _invStacks = new();
    private List<MailAttachment> _attachList = new();

    private int _inventoryScroll;
    private int _attachScroll;

    private const int InvCols = 7;
    private const int InvRows = 7;
    private const int AttachCols = 4;
    private const int AttachRows = 7;
    private const int CellGap = 4;
    private const int PanelHeaderH = 28;
    private const int ScrollbarW = 10;
    private const int MiddleGap = 16;
    private const int BottomBarH = 44;

    private int _invCellSize;
    private int _attachCellSize;
    private Rectangle _invPanelRect;
    private Rectangle _attachPanelRect;
    private Rectangle[,] _invSlotRects = new Rectangle[InvCols, InvRows];
    private Rectangle[,] _attachSlotRects = new Rectangle[AttachCols, AttachRows];

    private int _hoverInvIdx = -1;
    private int _hoverAttachIdx = -1;
    private Item? _tooltipItem;
    private Rectangle _tooltipSlotRect;
    private Rectangle _tooltipPanelRect;

    private int _dragFromPanel = -1; // 0 = инвентарь, 1 = вложения
    private int _dragIdx = -1;
    private Point _dragOffset;
    private Point _dragPos;

    private new MouseState _prevMouse;
    private int _prevScrollWheel;
    private int _lastClickInvIdx = -1;
    private int _lastClickAttachIdx = -1;
    private TimeSpan _lastClickInvTime;
    private TimeSpan _lastClickAttachTime;

    public event Action? ConfirmRequested;
    public event Action? CancelRequested;
    public event Action<string, int, int, Action<int>>? RequestQuantity;

    public List<MailAttachment> Attachments => _attachments;

    public override bool IsDragging => _dragIdx >= 0;

    public MailAttachmentWindow()
    {
        Title = "Вложения письма";
        Width = 680;
        Height = 560;
        Visible = false;
        IsModal = true;
    }

    public void Open(List<Item> inventoryItems, List<MailAttachment> current)
    {
        _inventoryItems = inventoryItems ?? new List<Item>();
        _attachments = (current ?? new List<MailAttachment>()).Select(a => a.Clone()).ToList();
        Visible = true;
        _inventoryScroll = 0;
        _attachScroll = 0;
    }

    private static bool IsStackable(string type) =>
        type is "consumable" or "collectible" or "trophy" or "material";

    private int AttachedQty(string templateId)
        => _attachments.Where(a => a.TemplateId == templateId).Sum(a => a.Quantity);

    private int InventoryQty(string templateId)
        => _inventoryItems.Where(i => i.TemplateId == templateId).Sum(i => Math.Max(1, i.Quantity));

    private int Available(string templateId)
        => Math.Max(0, InventoryQty(templateId) - AttachedQty(templateId));

    private void BuildStacks()
    {
        _invStacks.Clear();
        var grouped = _inventoryItems
            .Where(i => !string.IsNullOrEmpty(i.TemplateId) && i.Type != "gold")
            .GroupBy(i => i.TemplateId);
        foreach (var g in grouped)
        {
            var first = g.First();
            int available = Available(first.TemplateId);
            if (available <= 0) continue;
            var item = first.Clone();
            item.Quantity = available;
            int cap = IsStackable(item.Type) ? Math.Max(2, item.MaxStack) : 1;
            int qty = available;
            while (qty > 0)
            {
                int chunk = Math.Min(cap, qty);
                var st = item.Clone();
                st.Quantity = chunk;
                _invStacks.Add((st, chunk));
                qty -= chunk;
            }
        }

        _attachList = _attachments
            .GroupBy(a => a.TemplateId)
            .Select(a => new MailAttachment
            {
                TemplateId = a.Key,
                Name = a.First().Name,
                Type = a.First().Type,
                Quantity = a.Sum(x => x.Quantity),
                WeaponSubtype = a.First().WeaponSubtype,
                HealAmount = a.First().HealAmount,
                RestoreMana = a.First().RestoreMana
            })
            .ToList();
    }

    private void AddAttachment(string templateId, string name, string type, int qty, Item? source = null)
    {
        int available = Available(templateId);
        qty = Math.Min(qty, available);
        if (qty <= 0) return;

        var existing = _attachments.FirstOrDefault(a => a.TemplateId == templateId);
        if (existing != null)
            existing.Quantity += qty;
        else
            _attachments.Add(new MailAttachment
            {
                TemplateId = templateId, Name = name, Type = type, Quantity = qty,
                WeaponSubtype = source?.WeaponSubtype ?? "",
                HealAmount = source?.HealAmount ?? 0,
                RestoreMana = source?.RestoreMana ?? 0
            });
    }

    private void RemoveAttachment(string templateId, int qty)
    {
        var existing = _attachments.FirstOrDefault(a => a.TemplateId == templateId);
        if (existing == null) return;
        existing.Quantity -= qty;
        if (existing.Quantity <= 0)
            _attachments.Remove(existing);
    }

    public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
    {
        if (!Visible) { _prevMouse = mouse; _prevScrollWheel = mouse.ScrollWheelValue; return; }
        base.Update(gameTime, keyboard, mouse);

        BuildStacks();

        int totalW = Width - 32;
        int invPanelW = (int)(totalW * 0.58f);
        int attachPanelW = totalW - invPanelW - MiddleGap;

        _invCellSize = (invPanelW - (InvCols - 1) * CellGap - ScrollbarW - 4) / InvCols;
        _attachCellSize = (attachPanelW - (AttachCols - 1) * CellGap - ScrollbarW - 4) / AttachCols;

        int invPanelH = PanelHeaderH + InvRows * _invCellSize + (InvRows - 1) * CellGap + BottomBarH;
        int attachPanelH = PanelHeaderH + AttachRows * _attachCellSize + (AttachRows - 1) * CellGap + BottomBarH;

        _invPanelRect = new Rectangle(X + 16, Y + TitleH + 4, invPanelW, invPanelH);
        _attachPanelRect = new Rectangle(X + 16 + invPanelW + MiddleGap, Y + TitleH + 4, attachPanelW, attachPanelH);

        ComputeInvSlots();
        ComputeAttachSlots();

        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
        bool rightPressed = mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;

        _hoverInvIdx = FindSlotAt(mouse.X, mouse.Y, _invSlotRects, InvCols, InvRows);
        _hoverAttachIdx = FindSlotAt(mouse.X, mouse.Y, _attachSlotRects, AttachCols, AttachRows);

        if (clicked)
        {
            if (_hoverInvIdx >= 0 && _hoverInvIdx < _invStacks.Count)
            {
                var now = gameTime.TotalGameTime;
                if (_lastClickInvIdx == _hoverInvIdx && (now - _lastClickInvTime).TotalMilliseconds < 400)
                {
                    var (item, count) = _invStacks[_hoverInvIdx];
                    TransferInvToAttach(item, count, wholeStack: false);
                    _lastClickInvIdx = -1;
                }
                else
                {
                    _lastClickInvIdx = _hoverInvIdx;
                    _lastClickInvTime = now;
                    _dragFromPanel = 0;
                    _dragIdx = _hoverInvIdx;
                    int col = _hoverInvIdx % InvCols;
                    int row = _hoverInvIdx / InvCols;
                    _dragOffset = new Point(mouse.X - _invSlotRects[col, row].X, mouse.Y - _invSlotRects[col, row].Y);
                    _dragPos = new Point(mouse.X, mouse.Y);
                }
            }
            else if (_hoverAttachIdx >= 0 && _hoverAttachIdx < _attachList.Count)
            {
                var now = gameTime.TotalGameTime;
                if (_lastClickAttachIdx == _hoverAttachIdx && (now - _lastClickAttachTime).TotalMilliseconds < 400)
                {
                    var att = _attachList[_hoverAttachIdx];
                    TransferAttachToInv(att, wholeStack: false);
                    _lastClickAttachIdx = -1;
                }
                else
                {
                    _lastClickAttachIdx = _hoverAttachIdx;
                    _lastClickAttachTime = now;
                    _dragFromPanel = 1;
                    _dragIdx = _hoverAttachIdx;
                    int col = _hoverAttachIdx % AttachCols;
                    int row = _hoverAttachIdx / AttachCols;
                    _dragOffset = new Point(mouse.X - _attachSlotRects[col, row].X, mouse.Y - _attachSlotRects[col, row].Y);
                    _dragPos = new Point(mouse.X, mouse.Y);
                }
            }
            else
            {
                // Кнопки внизу
                int btnY = _attachPanelRect.Bottom - BottomBarH + 8;
                var okBtn = new Rectangle(_attachPanelRect.X + 8, btnY, 90, BtnH);
                var cancelBtn = new Rectangle(_attachPanelRect.X + 108, btnY, 90, BtnH);
                if (okBtn.Contains(mouse.X, mouse.Y))
                {
                    ConfirmRequested?.Invoke();
                    Visible = false;
                    _prevMouse = mouse;
                    return;
                }
                if (cancelBtn.Contains(mouse.X, mouse.Y))
                {
                    CancelRequested?.Invoke();
                    Visible = false;
                    _prevMouse = mouse;
                    return;
                }
            }
        }

        if (_dragIdx >= 0 && mouse.LeftButton == ButtonState.Pressed)
            _dragPos = new Point(mouse.X, mouse.Y);

        if (_dragIdx >= 0 && released)
        {
            bool droppedOnOther = false;
            if (_dragFromPanel == 0 && _attachPanelRect.Contains(mouse.X, mouse.Y))
                droppedOnOther = true;
            else if (_dragFromPanel == 1 && _invPanelRect.Contains(mouse.X, mouse.Y))
                droppedOnOther = true;

            if (droppedOnOther && _dragFromPanel == 0)
            {
                if (_dragIdx < _invStacks.Count)
                {
                    var (item, count) = _invStacks[_dragIdx];
                    TransferInvToAttach(item, count, wholeStack: false);
                }
            }
            else if (droppedOnOther && _dragFromPanel == 1)
            {
                if (_dragIdx < _attachList.Count)
                    TransferAttachToInv(_attachList[_dragIdx], wholeStack: false);
            }
            _dragIdx = -1;
            _dragFromPanel = -1;
        }

        if (rightPressed && _dragIdx < 0)
        {
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (_hoverInvIdx >= 0 && _hoverInvIdx < _invStacks.Count)
            {
                var (item, count) = _invStacks[_hoverInvIdx];
                TransferInvToAttach(item, count, wholeStack: shift);
            }
            else if (_hoverAttachIdx >= 0 && _hoverAttachIdx < _attachList.Count)
            {
                TransferAttachToInv(_attachList[_hoverAttachIdx], wholeStack: shift);
            }
        }

        int delta = mouse.ScrollWheelValue - _prevScrollWheel;
        if (delta != 0)
        {
            if (_invPanelRect.Contains(mouse.X, mouse.Y))
            {
                int maxScroll = Math.Max(0, (_invStacks.Count + InvCols - 1) / InvCols - InvRows);
                _inventoryScroll = Math.Clamp(_inventoryScroll + (delta < 0 ? 1 : -1), 0, maxScroll);
            }
            else if (_attachPanelRect.Contains(mouse.X, mouse.Y))
            {
                int maxScroll = Math.Max(0, (_attachList.Count + AttachCols - 1) / AttachCols - AttachRows);
                _attachScroll = Math.Clamp(_attachScroll + (delta < 0 ? 1 : -1), 0, maxScroll);
            }
        }

        _prevMouse = mouse;
        _prevScrollWheel = mouse.ScrollWheelValue;
    }

    private void TransferInvToAttach(Item item, int count, bool wholeStack)
    {
        int available = Available(item.TemplateId);
        if (available <= 0) return;

        if (wholeStack)
        {
            AddAttachment(item.TemplateId, item.Name, item.Type, available, item);
        }
        else if (IsStackable(item.Type) && available > 1)
        {
            RequestQuantity?.Invoke(item.Name, available, 1, qty =>
                AddAttachment(item.TemplateId, item.Name, item.Type, Math.Min(qty, available), item));
        }
        else
        {
            AddAttachment(item.TemplateId, item.Name, item.Type, 1, item);
        }
    }

    private void TransferAttachToInv(MailAttachment att, bool wholeStack)
    {
        int qty = AttachedQty(att.TemplateId);
        if (qty <= 0) return;

        if (wholeStack)
        {
            RemoveAttachment(att.TemplateId, qty);
        }
        else if (IsStackable(att.Type) && qty > 1)
        {
            RequestQuantity?.Invoke(att.Name, qty, 1, q =>
                RemoveAttachment(att.TemplateId, Math.Min(q, qty)));
        }
        else
        {
            RemoveAttachment(att.TemplateId, 1);
        }
    }

    private const int BtnH = 24;

    public override void Draw(SpriteBatch sb)
    {
        if (!Visible) return;
        base.Draw(sb);

        var font = SpriteCache.Font;
        var fontS = SpriteCache.FontSmall ?? SpriteCache.Font;
        if (font == null) return;

        DrawInvPanel(sb, font, fontS);
        DrawAttachPanel(sb, font, fontS);

        if (_tooltipItem != null)
        {
            var lines = ItemTooltip.BuildLines(_tooltipItem);
            var g = GameMain.Instance;
            int wRight = g?.Graphics.PreferredBackBufferWidth ?? 1920;
            int wBottom = g?.Graphics.PreferredBackBufferHeight ?? 1080;
            TooltipRenderer.Draw(sb, lines, Mouse.GetState(), wRight, wBottom);
            _tooltipItem = null;
        }

        if (_dragIdx >= 0)
        {
            var stacks = _dragFromPanel == 0 ? _invStacks : null;
            Item? item = _dragFromPanel == 0
                ? (_dragIdx < _invStacks.Count ? _invStacks[_dragIdx].item : null)
                : ToItem(_dragIdx < _attachList.Count ? _attachList[_dragIdx] : null);
            if (item != null)
            {
                var spr = SpriteCache.ForItem(item);
                int sz = 36;
                var dst = new Rectangle(_dragPos.X - _dragOffset.X, _dragPos.Y - _dragOffset.Y, sz, sz);
                if (spr != null)
                {
                    sb.Draw(spr, dst, Color.White);
                    var qFrame = SpriteCache.ForQualityFrame(item.Quality);
                    if (qFrame != null)
                        sb.Draw(qFrame, dst, Color.White);
                }
                else
                    sb.Draw(SpriteCache.Pixel, dst, new Color(180, 140, 60, 200));
                if (item.Quantity > 1 && fontS != null)
                    sb.DrawString(fontS, item.Quantity.ToString(),
                        new Vector2(dst.Right - 14, dst.Bottom - 14), Color.White);
            }
        }
    }

    private Item? ToItem(MailAttachment? att)
    {
        if (att == null) return null;
        return new Item
        {
            TemplateId = att.TemplateId, Name = att.Name, Type = att.Type, Quantity = att.Quantity,
            WeaponSubtype = att.WeaponSubtype, HealAmount = att.HealAmount, RestoreMana = att.RestoreMana
        };
    }

    private void DrawInvPanel(SpriteBatch sb, SpriteFont font, SpriteFont fontS)
    {
        sb.Draw(SpriteCache.Pixel, _invPanelRect, new Color(22, 24, 30));
        DrawBorder(sb, _invPanelRect, new Color(60, 70, 90), 2);
        sb.DrawString(font, "Инвентарь", new Vector2(_invPanelRect.X + 8, _invPanelRect.Y + 6), Color.White);

        int maxScroll = Math.Max(0, (_invStacks.Count + InvCols - 1) / InvCols - InvRows);
        if (_inventoryScroll > maxScroll) _inventoryScroll = maxScroll;

        for (int row = 0; row < InvRows; row++)
        {
            for (int col = 0; col < InvCols; col++)
            {
                int idx = (row + _inventoryScroll) * InvCols + col;
                var rect = _invSlotRects[col, row];
                bool hover = idx == _hoverInvIdx;
                bool filled = idx < _invStacks.Count;
                bool dragging = _dragFromPanel == 0 && idx == _dragIdx;

                sb.Draw(SpriteCache.Pixel, rect, hover ? new Color(55, 60, 80) : new Color(35, 38, 48));
                DrawBorder(sb, rect, new Color(55, 60, 75), 1);

                if (filled && !dragging)
                {
                    var (item, count) = _invStacks[idx];
                    var spr = SpriteCache.ForItem(item);
                    if (spr != null)
                    {
                        var iconRect = new Rectangle(rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8);
                        sb.Draw(spr, iconRect, Color.White);
                        var qFrame = SpriteCache.ForQualityFrame(item.Quality);
                        if (qFrame != null)
                            sb.Draw(qFrame, iconRect, Color.White);
                    }
                    if (count > 1 && fontS != null)
                        sb.DrawString(fontS, count.ToString(),
                            new Vector2(rect.X + rect.Width - 16, rect.Y + rect.Height - 16),
                            new Color(230, 230, 120));
                    if (hover)
                    {
                        _tooltipItem = item;
                        _tooltipSlotRect = rect;
                        _tooltipPanelRect = _invPanelRect;
                    }
                }
            }
        }
    }

    private void DrawAttachPanel(SpriteBatch sb, SpriteFont font, SpriteFont fontS)
    {
        sb.Draw(SpriteCache.Pixel, _attachPanelRect, new Color(22, 24, 30));
        DrawBorder(sb, _attachPanelRect, new Color(60, 70, 90), 2);
        sb.DrawString(font, $"Вложения ({_attachments.Sum(a => a.Quantity)} шт.)",
            new Vector2(_attachPanelRect.X + 8, _attachPanelRect.Y + 6), Color.White);

        int maxScroll = Math.Max(0, (_attachList.Count + AttachCols - 1) / AttachCols - AttachRows);
        if (_attachScroll > maxScroll) _attachScroll = maxScroll;

        for (int row = 0; row < AttachRows; row++)
        {
            for (int col = 0; col < AttachCols; col++)
            {
                int idx = (row + _attachScroll) * AttachCols + col;
                var rect = _attachSlotRects[col, row];
                bool hover = idx == _hoverAttachIdx;
                bool filled = idx < _attachList.Count;
                bool dragging = _dragFromPanel == 1 && idx == _dragIdx;

                sb.Draw(SpriteCache.Pixel, rect, hover ? new Color(55, 60, 80) : new Color(35, 38, 48));
                DrawBorder(sb, rect, new Color(55, 60, 75), 1);

                if (filled && !dragging)
                {
                    var att = _attachList[idx];
                    var item = ToItem(att);
                    if (item != null)
                    {
                        var spr = SpriteCache.ForItem(item);
                        if (spr != null)
                        {
                            var iconRect = new Rectangle(rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8);
                            sb.Draw(spr, iconRect, Color.White);
                            var qFrame = SpriteCache.ForQualityFrame(item.Quality);
                            if (qFrame != null)
                                sb.Draw(qFrame, iconRect, Color.White);
                        }
                        if (att.Quantity > 1 && fontS != null)
                            sb.DrawString(fontS, att.Quantity.ToString(),
                                new Vector2(rect.X + rect.Width - 16, rect.Y + rect.Height - 16),
                                new Color(230, 230, 120));
                        if (hover)
                        {
                            _tooltipItem = item;
                            _tooltipSlotRect = rect;
                            _tooltipPanelRect = _attachPanelRect;
                        }
                    }
                }
            }
        }

        int btnY = _attachPanelRect.Bottom - BottomBarH + 8;
        DrawButtonHover(sb, "Готово", new Rectangle(_attachPanelRect.X + 8, btnY, 90, BtnH), Mouse.GetState(), new Color(50, 120, 70));
        DrawButtonHover(sb, "Отмена", new Rectangle(_attachPanelRect.X + 108, btnY, 90, BtnH), Mouse.GetState(), new Color(130, 50, 50));
    }

    private void ComputeInvSlots()
    {
        int startX = _invPanelRect.X + 8;
        int startY = _invPanelRect.Y + PanelHeaderH;
        for (int row = 0; row < InvRows; row++)
            for (int col = 0; col < InvCols; col++)
                _invSlotRects[col, row] = new Rectangle(
                    startX + col * (_invCellSize + CellGap),
                    startY + row * (_invCellSize + CellGap),
                    _invCellSize, _invCellSize);
    }

    private void ComputeAttachSlots()
    {
        int startX = _attachPanelRect.X + 8;
        int startY = _attachPanelRect.Y + PanelHeaderH;
        for (int row = 0; row < AttachRows; row++)
            for (int col = 0; col < AttachCols; col++)
                _attachSlotRects[col, row] = new Rectangle(
                    startX + col * (_attachCellSize + CellGap),
                    startY + row * (_attachCellSize + CellGap),
                    _attachCellSize, _attachCellSize);
    }

    private static int FindSlotAt(int mx, int my, Rectangle[,] slotRects, int cols, int rows)
    {
        for (int i = 0; i < cols * rows; i++)
        {
            int col = i % cols;
            int row = i / cols;
            if (slotRects[col, row].Contains(mx, my))
                return i;
        }
        return -1;
    }

    private static void DrawBorder(SpriteBatch sb, Rectangle r, Color color, int thickness)
    {
        sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, r.Width, thickness), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, thickness, r.Height), color);
        sb.Draw(SpriteCache.Pixel, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);
    }
}
