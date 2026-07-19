using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RPGGame.ClientMonoGame.Rendering;
using RPGGame.ClientMonoGame.Networking;
using RPGGame.Shared.Models;

namespace RPGGame.ClientMonoGame.Windows
{
    public class TradeWindow : GameWindow
    {
        private string _sessionId = string.Empty;
        private string _otherName = "";

        private List<TradeItemData> _myInventoryItems = new();
        private List<TradeItemData> _myOfferItems = new();
        private List<TradeItemData> _theirOfferItems = new();
        private int _myGoldOffer;
        private int _theirGoldOffer;
        private int _myTotalGold;
        private bool _iConfirmed;
        private bool _otherConfirmed;

        private const int InvCols = 6;
        private const int InvRows = 4;
        private const int Gap = 4;

        private int _scrollOffset;
        private int _maxScroll;

        private bool _goldInputActive;
        private StringBuilder _goldInputBuffer = new();
        private KeyboardState _prevKeyboard;

        private MouseState _prevMouse;
        private bool _wasVisible;
        private TradeItemData? _hoverItem;

        public event Action<List<TradeOfferEntry>, int>? OfferChanged;
        public event Action? ConfirmRequested;
        public event Action? CancelRequested;
        public event Action<string, int, int, Action<int>>? RequestQuantity;

        private static readonly Color CFieldBg = new(35, 38, 48);
        private static readonly Color CFieldHover = new(55, 60, 80);
        private static readonly Color CFieldBorder = new(60, 65, 80);
        private static readonly Color CGold = new(220, 200, 120);
        private static readonly Color CLight = new(210, 210, 220);
        private static readonly Color CDanger = new(140, 40, 40);
        private static readonly Color CDangerHover = new(180, 60, 60);
        private static readonly Color CConfirm = new(40, 120, 40);
        private static readonly Color CConfirmActive = new(140, 100, 40);
        private static readonly Color CBtnBg = new(55, 60, 75);
        private static readonly Color CBtnHover = new(75, 80, 100);
        private static readonly Color CGoldInput = new(50, 55, 45);
        private static readonly Color CGoldInputActive = new(60, 65, 55);

        public TradeWindow()
        {
            Title = "Обмен";
            Width = 620;
            Height = 540;
            Visible = false;
        }

        public void Open(TradeOpenData data)
        {
            _sessionId = data.SessionId ?? string.Empty;
            _otherName = data.OtherName ?? "";
            Title = $"Обмен с {_otherName}";
            _myInventoryItems = data.YourInventory ?? new List<TradeItemData>();
            _myTotalGold = data.YourGold;
            _theirGoldOffer = data.OtherGold;
            _myGoldOffer = 0;
            _myOfferItems.Clear();
            _theirOfferItems.Clear();
            _iConfirmed = false;
            _otherConfirmed = false;
            _scrollOffset = 0;
            _goldInputActive = false;
            _goldInputBuffer.Clear();
            var g = GameMain.Instance!.Graphics;
            X = (g.PreferredBackBufferWidth - Width) / 2;
            Y = (g.PreferredBackBufferHeight - Height) / 2;
            Visible = true;

            var grouped = _myInventoryItems
                .GroupBy(i => i.Id)
                .Select(gr => $"{gr.First().Name} x{gr.Count()}")
                .ToList();
            Logger.Action($"ОБМЕН ОТКРЫТ: с '{_otherName}', session={_sessionId}");
            Logger.Info($"ОБМЕН: золото игрока={_myTotalGold}, золото противника={_theirGoldOffer}");
            Logger.Info($"ОБМЕН: предметов в инвентаре={_myInventoryItems.Count} (уникальных={grouped.Count})");
            foreach (var line in grouped)
                Logger.Debug($"ОБМЕН: инвентарь -> {line}");
        }

        public void UpdateMyOffer(TradeOfferData data)
        {
            _myOfferItems = ExpandItems(data.Offer?.Items);
            _myGoldOffer = data.Offer?.Gold ?? 0;
            int total = _myOfferItems.Sum(i => Math.Max(1, i.Quantity));
            Logger.Debug($"ОБМЕН: сервер обновил МОЙ оффер: предметов={total}, золото={_myGoldOffer}");
        }

        public void UpdateTheirOffer(TradeOfferData data)
        {
            _theirOfferItems = ExpandItems(data.Offer?.Items);
            _theirGoldOffer = data.Offer?.Gold ?? 0;
            int total = _theirOfferItems.Sum(i => Math.Max(1, i.Quantity));
            Logger.Debug($"ОБМЕН: сервер обновил оффер ПРОТИВНИКА: предметов={total}, золото={_theirGoldOffer}");
        }

        private static List<TradeItemData> ExpandItems(List<TradeItemData>? items)
        {
            var result = new List<TradeItemData>();
            if (items == null) return result;
            foreach (var it in items)
            {
                int qty = Math.Max(1, it.Quantity);
                for (int q = 0; q < qty; q++)
                {
                    result.Add(new TradeItemData
                    {
                        Id = it.Id,
                        TemplateId = it.TemplateId,
                        Name = it.Name,
                        Type = it.Type,
                        Value = it.Value,
                        Description = it.Description,
                        Attack = it.Attack,
                        Defense = it.Defense,
                        MaxHealthBonus = it.MaxHealthBonus,
                        HealAmount = it.HealAmount,
                        MaxStack = it.MaxStack,
                        Quantity = 1
                    });
                }
            }
            return result;
        }

        public void UpdateConfirm(TradeConfirmData data)
        {
            _iConfirmed = data.YouConfirmed;
            _otherConfirmed = data.OtherConfirmed;
            Logger.Info($"ОБМЕН: подтверждение: я={_iConfirmed}, противник={_otherConfirmed}");
        }

        public void HandleComplete(TradeCompleteData data)
        {
            Logger.Action($"ОБМЕН ЗАВЕРШЁН: success={data.Success}, msg='{data.Message}'");
            if (data.Success)
                Visible = false;
        }

        private int ComputeCellSize()
        {
            int halfW = ContentW / 2 - 8;
            return Math.Min(50, (halfW - (InvCols - 1) * Gap) / InvCols);
        }

        private Rectangle GetInvSlotRect(int c, int r)
        {
            int cellSize = ComputeCellSize();
            int cx = ContentX;
            int cy = ContentY;
            int invGridY = cy + 18;
            return new Rectangle(
                cx + c * (cellSize + Gap),
                invGridY + r * (cellSize + Gap),
                cellSize, cellSize);
        }

        private Rectangle GetOfferSlotRect(int c, int r)
        {
            int cellSize = ComputeCellSize();
            int cx = ContentX;
            int cy = ContentY;
            int invGridY = cy + 18;
            int offerGridY = invGridY + InvRows * (cellSize + Gap) + 20;
            return new Rectangle(
                cx + c * (cellSize + Gap),
                offerGridY + r * (cellSize + Gap),
                cellSize, cellSize);
        }

        private Rectangle GetGoldInputRect()
        {
            int cellSize = ComputeCellSize();
            int cx = ContentX;
            int cy = ContentY;
            int halfW = ContentW / 2 - 8;
            int invGridY = cy + 18;
            int offerGridY = invGridY + InvRows * (cellSize + Gap) + 20;
            int goldY = offerGridY + 2 * (cellSize + Gap) + 12;
            int labelW = 70;
            return new Rectangle(cx + labelW, goldY - 2, halfW - labelW, 20);
        }

        public override void Update(GameTime gameTime, KeyboardState keyboard, MouseState mouse)
        {
            if (!Visible)
            {
                _wasVisible = false;
                return;
            }

                if (!_wasVisible)
            {
                _wasVisible = true;
                _prevMouse = mouse;
                _prevKeyboard = keyboard;
            }

            bool justClicked = mouse.LeftButton == ButtonState.Pressed
                            && _prevMouse.LeftButton == ButtonState.Released;

            if (_goldInputActive)
            {
                HandleGoldInput(keyboard);

                if (justClicked)
                {
                    var goldRect = GetGoldInputRect();
                    if (!goldRect.Contains(mouse.X, mouse.Y))
                    {
                        CommitGoldInput();
                    }
                }

                _prevMouse = mouse;
                _prevKeyboard = keyboard;
                base.Update(gameTime, keyboard, mouse);
                return;
            }

            if (justClicked)
            {
                var groupedInv = GetGroupedInventory();
                for (int r = 0; r < InvRows; r++)
                    for (int c = 0; c < InvCols; c++)
                    {
                        var rect = GetInvSlotRect(c, r);
                        if (rect.Contains(mouse.X, mouse.Y))
                        {
                            int uniqueIdx = r * InvCols + c + _scrollOffset;
                            if (uniqueIdx < groupedInv.Count)
                                OnInventoryClick(groupedInv[uniqueIdx].Key);
                            _prevMouse = mouse;
                            _prevKeyboard = keyboard;
                            return;
                        }
                    }

                for (int r = 0; r < 2; r++)
                    for (int c = 0; c < InvCols; c++)
                    {
                        var rect = GetOfferSlotRect(c, r);
                        if (rect.Contains(mouse.X, mouse.Y))
                        {
                            int uniqueIdx = r * InvCols + c;
                            var grouped = GetGroupedOffer();
                            if (uniqueIdx < grouped.Count)
                                OnOfferClick(grouped[uniqueIdx].Key);
                            _prevMouse = mouse;
                            _prevKeyboard = keyboard;
                            return;
                        }
                    }

                var goldRect2 = GetGoldInputRect();
                if (goldRect2.Contains(mouse.X, mouse.Y))
                {
                    _goldInputActive = true;
                    _goldInputBuffer.Clear();
                    _goldInputBuffer.Append(_myGoldOffer);
                    _prevMouse = mouse;
                    _prevKeyboard = keyboard;
                    return;
                }
            }

            HandleScrollClick(mouse);

            _prevMouse = mouse;
            _prevKeyboard = keyboard;
            base.Update(gameTime, keyboard, mouse);
        }

        private void HandleScrollClick(MouseState mouse)
        {
            bool justClicked = mouse.LeftButton == ButtonState.Pressed
                            && _prevMouse.LeftButton == ButtonState.Released;
            if (!justClicked) return;

            int cellSize = ComputeCellSize();
            int cx = ContentX;
            int cy = ContentY;
            int gridW = InvCols * cellSize + (InvCols - 1) * Gap;
            int invGridY = cy + 18;

            if (_scrollOffset > 0)
            {
                var upBtn = new Rectangle(cx + gridW + 4, invGridY, 14, 14);
                if (upBtn.Contains(mouse.X, mouse.Y))
                    _scrollOffset = Math.Max(0, _scrollOffset - InvCols);
            }
            if (_scrollOffset < _maxScroll)
            {
                var dnBtn = new Rectangle(cx + gridW + 4, invGridY + InvRows * (cellSize + Gap) - 14, 14, 14);
                if (dnBtn.Contains(mouse.X, mouse.Y))
                    _scrollOffset = Math.Min(_maxScroll, _scrollOffset + InvCols);
            }
        }

        private void HandleGoldInput(KeyboardState keyboard)
        {
            for (int k = (int)Keys.D0; k <= (int)Keys.D9; k++)
            {
                var key = (Keys)k;
                if (keyboard.IsKeyDown(key) && _prevKeyboard.IsKeyUp(key))
                {
                    char ch = (char)('0' + (k - (int)Keys.D0));
                    if (_goldInputBuffer.Length < 8)
                        _goldInputBuffer.Append(ch);
                }
            }

            for (int k = (int)Keys.NumPad0; k <= (int)Keys.NumPad9; k++)
            {
                var key = (Keys)k;
                if (keyboard.IsKeyDown(key) && _prevKeyboard.IsKeyUp(key))
                {
                    char ch = (char)('0' + (k - (int)Keys.NumPad0));
                    if (_goldInputBuffer.Length < 8)
                        _goldInputBuffer.Append(ch);
                }
            }

            if (keyboard.IsKeyDown(Keys.Back) && _prevKeyboard.IsKeyUp(Keys.Back))
            {
                if (_goldInputBuffer.Length > 0)
                    _goldInputBuffer.Remove(_goldInputBuffer.Length - 1, 1);
            }

            if (keyboard.IsKeyDown(Keys.Enter) && _prevKeyboard.IsKeyUp(Keys.Enter))
            {
                CommitGoldInput();
            }

            if (keyboard.IsKeyDown(Keys.Escape) && _prevKeyboard.IsKeyUp(Keys.Escape))
            {
                _goldInputActive = false;
                _goldInputBuffer.Clear();
            }
        }

        private void CommitGoldInput()
        {
            if (int.TryParse(_goldInputBuffer.ToString(), out int val))
            {
                val = Math.Clamp(val, 0, _myTotalGold);
                if (val != _myGoldOffer)
                {
                    _myGoldOffer = val;
                    NotifyOfferChanged();
                }
            }
            _goldInputActive = false;
            _goldInputBuffer.Clear();
        }

        private void OnInventoryClick(string itemId)
        {
            var availableInv = GetAvailableInventory();
            var item = availableInv.FirstOrDefault(i => i.Id == itemId);
            if (item == null) return;
            int inInventory = availableInv.Count(i => i.Id == itemId);
            int alreadyInOffer = _myOfferItems.Count(o => o.Id == itemId);
            int available = inInventory - alreadyInOffer;
            if (available <= 0) return;

            Logger.Debug($"ОБМЕН: клик по инвентарю '{item.Name}' (id={itemId}), доступно={available}");

            if (available > 1)
            {
                RequestQuantity?.Invoke(item.Name, available, 1, qty =>
                {
                    qty = Math.Min(qty, available);
                    Logger.Debug($"ОБМЕН: добавление в оффер '{item.Name}' x{qty}");
                    AddToOffer(itemId, qty);
                    NotifyOfferChanged();
                });
            }
            else
            {
                AddToOffer(itemId, 1);
                NotifyOfferChanged();
            }
        }

        private void AddToOffer(string itemId, int qty)
        {
            for (int q = 0; q < qty; q++)
            {
                var available = GetAvailableInventory();
                var rec = available.FirstOrDefault(i => i.Id == itemId);
                if (rec == null) break;
                _myOfferItems.Add(rec);
            }
        }

        private void RemoveFromOffer(string templateId, int qty)
        {
            for (int q = 0; q < qty; q++)
            {
                var target = _myOfferItems.FirstOrDefault(o => o.TemplateId == templateId);
                if (target == null) break;
                _myOfferItems.Remove(target);
            }
        }

        private void OnOfferClick(string templateId)
        {
            int count = _myOfferItems.Count(o => o.TemplateId == templateId);
            if (count <= 0) return;

            if (count > 1)
            {
                RequestQuantity?.Invoke("предмет", count, 1, qty =>
                {
                    RemoveFromOffer(templateId, qty);
                    NotifyOfferChanged();
                });
            }
            else
            {
                RemoveFromOffer(templateId, 1);
                NotifyOfferChanged();
            }
        }

        private List<KeyValuePair<string, int>> GetGroupedOffer()
        {
            return _myOfferItems
                .GroupBy(i => i.TemplateId)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(i => Math.Max(1, i.Quantity))))
                .ToList();
        }

        private List<TradeItemData> GetAvailableInventory()
        {
            var offered = new List<string>(_myOfferItems.Select(i => i.Id));
            var result = new List<TradeItemData>(_myInventoryItems);
            foreach (var id in offered)
            {
                int idx = result.FindIndex(i => i.Id == id);
                if (idx >= 0) result.RemoveAt(idx);
            }
            return result;
        }

        private List<KeyValuePair<string, int>> GetGroupedInventory()
        {
            return GetAvailableInventory()
                .GroupBy(i => i.Id)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
                .ToList();
        }

        private List<KeyValuePair<string, int>> GetGroupedTheirOffer()
        {
            return _theirOfferItems
                .GroupBy(i => i.TemplateId)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(i => Math.Max(1, i.Quantity))))
                .ToList();
        }

        public override void Draw(SpriteBatch sb)
        {
            if (!Visible) return;

            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (font == null) return;

            _hoverItem = null;
            var mouse = Mouse.GetState();

            sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, Height), new Color(30, 32, 40));
            sb.Draw(SpriteCache.Pixel, new Rectangle(X, Y, Width, TitleH), new Color(45, 55, 75));
            DrawRectOutline(sb, new Rectangle(X, Y, Width, Height), new Color(80, 90, 110));

            var titleSize = font.MeasureString(Title);
            sb.DrawString(font, Title, new Vector2(X + 8, Y + (TitleH - titleSize.Y) / 2), Color.White);

            int cx = ContentX;
            int cy = ContentY;
            int halfW = ContentW / 2 - 8;
            int cellSize = ComputeCellSize();
            int gridW = InvCols * cellSize + (InvCols - 1) * Gap;

            int invGridY = cy + 18;
            DrawText(sb, "Ваш инвентарь:", cx, cy, CGold, font);

            var groupedInv = GetGroupedInventory();
            _maxScroll = Math.Max(0, (groupedInv.Count + InvCols - 1) / InvCols - InvRows);

            for (int r = 0; r < InvRows; r++)
                for (int c = 0; c < InvCols; c++)
                {
                    var rect = GetInvSlotRect(c, r);
                    int uniqueIdx = r * InvCols + c + _scrollOffset;
                    bool filled = uniqueIdx < groupedInv.Count;
                    bool hover = rect.Contains(mouse.X, mouse.Y);

                    sb.Draw(SpriteCache.Pixel, rect, hover ? CFieldHover : CFieldBg);
                    DrawRectOutline(sb, rect, CFieldBorder);

                    if (filled)
                    {
                        var item = GetAvailableInventory().First(i => i.Id == groupedInv[uniqueIdx].Key);
                        if (hover) _hoverItem = item;
                        var spr = SpriteCache.ForItemType(item.Type);
                        if (spr != null)
                            sb.Draw(spr, new Rectangle(rect.X + 4, rect.Y + 4, cellSize - 8, cellSize - 8), Color.White);

                        int count = groupedInv[uniqueIdx].Value;
                        if (count > 1)
                            DrawText(sb, count.ToString(), rect.X + cellSize - 14, rect.Y + cellSize - 14, new Color(230, 230, 120), font);
                    }
                }

            if (_scrollOffset > 0)
            {
                var upBtn = new Rectangle(cx + gridW + 4, invGridY, 14, 14);
                DrawText(sb, "^", upBtn.X + 3, upBtn.Y, CLight, font);
            }
            if (_scrollOffset < _maxScroll)
            {
                var dnBtn = new Rectangle(cx + gridW + 4, invGridY + InvRows * (cellSize + Gap) - 14, 14, 14);
                DrawText(sb, "v", dnBtn.X + 3, dnBtn.Y, CLight, font);
            }

            int offerGridY = invGridY + InvRows * (cellSize + Gap) + 20;
            DrawText(sb, "Ваш оффер:", cx, offerGridY - 18, new Color(180, 200, 180), font);

            var groupedOffer = GetGroupedOffer();
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < InvCols; c++)
                {
                    var rect = GetOfferSlotRect(c, r);
                    int uniqueIdx = r * InvCols + c;
                    bool filled = uniqueIdx < groupedOffer.Count;
                    bool hover = rect.Contains(mouse.X, mouse.Y);

                    sb.Draw(SpriteCache.Pixel, rect, hover ? CFieldHover : CFieldBg);
                    DrawRectOutline(sb, rect, CFieldBorder);

                    if (filled)
                    {
                        var kvp = groupedOffer[uniqueIdx];
                        var item = _myOfferItems.First(i => i.TemplateId == kvp.Key);
                        if (hover) _hoverItem = item;
                        var spr = SpriteCache.ForItemType(item.Type);
                        if (spr != null)
                            sb.Draw(spr, new Rectangle(rect.X + 4, rect.Y + 4, cellSize - 8, cellSize - 8), Color.White);

                        if (kvp.Value > 1)
                            DrawText(sb, kvp.Value.ToString(), rect.X + cellSize - 14, rect.Y + cellSize - 14, new Color(230, 230, 120), font);
                    }
                }

            int goldY = offerGridY + 2 * (cellSize + Gap) + 12;
            DrawText(sb, "Золото:", cx, goldY, CGold, font);

            var goldRect = GetGoldInputRect();
            bool goldHover = goldRect.Contains(mouse.X, mouse.Y);
            sb.Draw(SpriteCache.Pixel, goldRect, _goldInputActive ? CGoldInputActive : (goldHover ? CFieldHover : CGoldInput));
            DrawRectOutline(sb, goldRect, _goldInputActive ? Color.Gold : CFieldBorder);

            string goldDisplay;
            if (_goldInputActive)
            {
                goldDisplay = _goldInputBuffer.ToString();
                if ((Environment.TickCount / 500) % 2 == 0)
                    goldDisplay += "|";
            }
            else
            {
                goldDisplay = _myGoldOffer > 0 ? _myGoldOffer.ToString() : "нажмите для ввода";
            }
            DrawText(sb, goldDisplay, goldRect.X + 6, goldY, Color.White, font);

            string goldLimit = $"/ {_myTotalGold}";
            DrawText(sb, goldLimit, goldRect.Right + 6, goldY, new Color(160, 160, 170), font);

            int rightX = cx + halfW + 16;
            DrawText(sb, $"Оффер {_otherName}:", rightX, cy, CGold, font);

            int theirGridY = cy + 18;
            var groupedTheir = GetGroupedTheirOffer();
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < InvCols; c++)
                {
                    int x = rightX + c * (cellSize + Gap);
                    int y = theirGridY + r * (cellSize + Gap);
                    var rect = new Rectangle(x, y, cellSize, cellSize);
                    bool hover = rect.Contains(mouse.X, mouse.Y);

                    sb.Draw(SpriteCache.Pixel, rect, CFieldBg);
                    DrawRectOutline(sb, rect, CFieldBorder);

                    int uniqueIdx = r * InvCols + c;
                    if (uniqueIdx < groupedTheir.Count)
                    {
                        var kvp = groupedTheir[uniqueIdx];
                        var item = _theirOfferItems.First(i => i.TemplateId == kvp.Key);
                        if (hover) _hoverItem = item;
                        var spr = SpriteCache.ForItemType(item.Type);
                        if (spr != null)
                            sb.Draw(spr, new Rectangle(rect.X + 4, rect.Y + 4, cellSize - 8, cellSize - 8), Color.White);

                        if (kvp.Value > 1)
                            DrawText(sb, kvp.Value.ToString(), rect.X + cellSize - 14, rect.Y + cellSize - 14, new Color(230, 230, 120), font);
                    }
                }

            int theirGoldY = theirGridY + 2 * (cellSize + Gap) + 12;
            DrawText(sb, $"Золото: {_theirGoldOffer}", rightX, theirGoldY, CGold, font);

            int by = ContentY + ContentH - 56;

            string myLabel = _iConfirmed ? "Вы: подтверждено" : "Вы: не подтверждено";
            DrawText(sb, myLabel, cx, by, _iConfirmed ? Color.LimeGreen : Color.Red, font);

            string theirLabel = _otherConfirmed ? $"{_otherName}: подтверждено" : $"{_otherName}: не подтверждено";
            DrawText(sb, theirLabel, rightX, by, _otherConfirmed ? Color.LimeGreen : Color.Red, font);

            by += 20;
            string cText = _iConfirmed ? "Отменить" : "Подтвердить";
            Color cBg = _iConfirmed ? CConfirmActive : CConfirm;
            var confirmBtn = new Rectangle(cx, by, 130, 26);
            bool confirmHover = confirmBtn.Contains(mouse.X, mouse.Y);
            sb.Draw(SpriteCache.Pixel, confirmBtn, confirmHover ? new Color(60, 150, 60) : cBg);
            var cSize = font.MeasureString(cText);
            sb.DrawString(font, cText, new Vector2(confirmBtn.X + (confirmBtn.Width - cSize.X) / 2, confirmBtn.Y + (confirmBtn.Height - cSize.Y) / 2), Color.White);

            var cancelBtn = new Rectangle(cx + 140, by, 100, 26);
            bool cancelHover = cancelBtn.Contains(mouse.X, mouse.Y);
            sb.Draw(SpriteCache.Pixel, cancelBtn, cancelHover ? CDangerHover : CDanger);
            var clSize = font.MeasureString("Отмена");
            sb.DrawString(font, "Отмена", new Vector2(cancelBtn.X + (cancelBtn.Width - clSize.X) / 2, cancelBtn.Y + (cancelBtn.Height - clSize.Y) / 2), Color.White);

            if (pressed(confirmBtn, mouse))
                ConfirmRequested?.Invoke();
            if (pressed(cancelBtn, mouse))
            {
                CancelRequested?.Invoke();
                Visible = false;
            }

            if (_hoverItem != null)
                DrawTooltip(sb, _hoverItem, mouse);
        }

        private void DrawTooltip(SpriteBatch sb, TradeItemData item, MouseState mouse)
        {
            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (font == null) return;

            var lines = new List<string>
            {
                item.Name ?? "",
                $"Тип: {item.Type}",
                $"Цена: {item.Value} золота"
            };
            if (item.Attack > 0) lines.Add($"Атака: +{item.Attack}");
            if (item.Defense > 0) lines.Add($"Защита: +{item.Defense}");
            if (item.MaxHealthBonus > 0) lines.Add($"Здоровье: +{item.MaxHealthBonus}");
            if (item.HealAmount > 0) lines.Add($"Лечение: +{item.HealAmount}");
            if (!string.IsNullOrEmpty(item.Description))
                lines.Add(item.Description);

            int pad = 8;
            float tw = 0;
            foreach (var l in lines) tw = Math.Max(tw, font.MeasureString(l).X);
            int th = lines.Count * 18 + pad * 2;
            int tx = mouse.X + 16;
            int ty = mouse.Y + 16;
            int ww = (int)tw + pad * 2;
            var g = GameMain.Instance;
            if (g != null)
            {
                int bw = g.Graphics.PreferredBackBufferWidth;
                int bh = g.Graphics.PreferredBackBufferHeight;
                if (tx + ww > bw) tx = bw - ww - 4;
                if (ty + th > bh) ty = bh - th - 4;
            }

            sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, ww, th), new Color(20, 22, 30, 230));
            sb.Draw(SpriteCache.Pixel, new Rectangle(tx, ty, ww, 2), new Color(80, 120, 200));
            for (int i = 0; i < lines.Count; i++)
            {
                var color = i == 0 ? new Color(230, 220, 140) : Color.White;
                sb.DrawString(font, lines[i], new Vector2(tx + pad, ty + pad + i * 18), color);
            }
        }

        private void NotifyOfferChanged()
        {
            var entries = BuildOfferEntries();
            var grouped = entries
                .Select(e => $"{(e.TemplateId)} x{e.Quantity}")
                .ToList();
            Logger.Info($"ОБМЕН: отправка оффера на сервер: типов={entries.Count}, золото={_myGoldOffer}");
            foreach (var line in grouped)
                Logger.Debug($"ОБМЕН: оффер -> {line}");
            OfferChanged?.Invoke(entries, _myGoldOffer);
        }

        public List<TradeOfferEntry> BuildOfferEntries()
        {
            return _myOfferItems
                .GroupBy(i => i.TemplateId)
                .Select(gr => new TradeOfferEntry
                {
                    TemplateId = gr.Key,
                    Quantity = gr.Sum(i => Math.Max(1, i.Quantity))
                })
                .ToList();
        }

        private static bool pressed(Rectangle rect, MouseState mouse)
        {
            return rect.Contains(mouse.X, mouse.Y) && mouse.LeftButton == ButtonState.Pressed;
        }

        private static void DrawRectOutline(SpriteBatch sb, Rectangle rect, Color color, int t = 1)
        {
            sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, rect.Width, t), color);
            sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Bottom - t, rect.Width, t), color);
            sb.Draw(SpriteCache.Pixel, new Rectangle(rect.X, rect.Y, t, rect.Height), color);
            sb.Draw(SpriteCache.Pixel, new Rectangle(rect.Right - t, rect.Y, t, rect.Height), color);
        }

        private static void DrawText(SpriteBatch sb, string text, int x, int y, Color color, SpriteFont font)
        {
            if (!string.IsNullOrEmpty(text))
                sb.DrawString(font, text, new Vector2(x, y), color);
        }
    }
}
