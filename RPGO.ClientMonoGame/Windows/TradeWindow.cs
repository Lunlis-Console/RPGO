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
using RPGGame.Shared.Network;

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

        private int _scrollOffset;
        private int _maxScroll;
        private int _offerScroll;
        private int _offerMaxScroll;
        private int _theirScroll;
        private int _theirMaxScroll;

        private bool _goldInputActive;
        private StringBuilder _goldInputBuffer = new();
        private KeyboardState _prevKeyboard;

        private new MouseState _prevMouse;
        private int _prevScrollWheel;
        private bool _wasVisible;
        private TradeItemData? _hoverItem;

        // Drag-n-drop между инвентарём и оффером (как в окне склада)
        private int _dragFromPanel = -1; // 0 = инвентарь, 1 = мой оффер
        private int _dragIdx = -1;
        private Point _dragOffset;
        private Point _dragPos;
        private int _lastClickInvIdx = -1;
        private int _lastClickOfferIdx = -1;
        private TimeSpan _lastClickInvTime;
        private TimeSpan _lastClickOfferTime;

        public override bool IsDragging => _dragIdx >= 0;

        // Геометрия панелей
        private Rectangle _invPanelRect;
        private Rectangle _myOfferPanelRect;
        private Rectangle _theirOfferPanelRect;
        private int _invCellSize;
        private int _offerCellSize;
        private int _invVisibleRows;

        public event Action<List<TradeOfferEntry>, int>? OfferChanged;
        public event Action? ConfirmRequested;
        public event Action? CancelRequested;
        public event Action<string, int, int, Action<int>>? RequestQuantity;

        private static readonly Color CPanelBg = new(22, 24, 30);
        private static readonly Color CPanelBorder = new(60, 70, 90);
        private static readonly Color CFieldBg = new(35, 38, 48);
        private static readonly Color CFieldHover = new(55, 60, 80);
        private static readonly Color CFieldBorder = new(55, 60, 75);
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

        private const int InvCols = 10;
        private const int OfferCols = 6;
        private const int OfferRows = 3;
        private const int CellGap = 4;
        private const int PanelHeaderH = 28;
        private const int ScrollbarW = 10;
        private const int MiddleGap = 16;
        private const int BottomBarH = 44;

        public TradeWindow()
        {
            Title = "Обмен";
            Width = 900;
            Height = 620;
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
            _offerScroll = 0;
            _theirScroll = 0;
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
            var incoming = data.Offer?.Items;
            Logger.Debug($"ОБМЕН: пришёл trade_offer_update (IsFromMe), записей от сервера={incoming?.Count ?? 0}" +
                (incoming != null ? ", itemIds=[" + string.Join(",", incoming.Select(i => i.Id + "x" + i.Quantity)) + "]" : ""));
            _myOfferItems = ExpandItems(incoming);
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
                result.Add(it.WithQuantity(Math.Max(1, it.Quantity)));
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

        private void ComputeLayout()
        {
            int cw = ContentW;
            int ch = ContentH;
            int panelH = ch - BottomBarH;

            int invW = (int)(cw * 0.55f);
            int rightW = cw - invW - MiddleGap;

            _invPanelRect = new Rectangle(ContentX, ContentY, invW, panelH);
            _myOfferPanelRect = new Rectangle(ContentX + invW + MiddleGap, ContentY, rightW, panelH / 2 - 6);
            _theirOfferPanelRect = new Rectangle(ContentX + invW + MiddleGap, ContentY + panelH / 2 + 6, rightW, panelH - panelH / 2 - 6);

            _invCellSize = (invW - 2 * 8 - (InvCols - 1) * CellGap - ScrollbarW) / InvCols;
            _offerCellSize = (rightW - 2 * 8 - (OfferCols - 1) * CellGap - ScrollbarW) / OfferCols;

            int availH = _invPanelRect.Height - PanelHeaderH - 4;
            _invVisibleRows = Math.Max(1, (availH + CellGap) / (_invCellSize + CellGap));
        }

        private Rectangle GetInvSlotRect(int c, int r)
        {
            return new Rectangle(
                _invPanelRect.X + 8 + c * (_invCellSize + CellGap),
                _invPanelRect.Y + PanelHeaderH + r * (_invCellSize + CellGap),
                _invCellSize, _invCellSize);
        }

        private Rectangle GetMyOfferSlotRect(int c, int r)
        {
            return new Rectangle(
                _myOfferPanelRect.X + 8 + c * (_offerCellSize + CellGap),
                _myOfferPanelRect.Y + PanelHeaderH + r * (_offerCellSize + CellGap),
                _offerCellSize, _offerCellSize);
        }

        private Rectangle GetTheirOfferSlotRect(int c, int r)
        {
            return new Rectangle(
                _theirOfferPanelRect.X + 8 + c * (_offerCellSize + CellGap),
                _theirOfferPanelRect.Y + PanelHeaderH + r * (_offerCellSize + CellGap),
                _offerCellSize, _offerCellSize);
        }

        private Rectangle GetGoldInputRect()
        {
            int gy = _myOfferPanelRect.Y + PanelHeaderH + OfferRows * (_offerCellSize + CellGap) + 10;
            int labelW = 58;
            int reserve = 72;
            int x = _myOfferPanelRect.X + 8 + labelW;
            int w = _myOfferPanelRect.Width - 2 * 8 - labelW - reserve;
            return new Rectangle(x, gy, w, 22);
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
                _prevScrollWheel = mouse.ScrollWheelValue;
                _prevKeyboard = keyboard;
            }

            bool clicked = mouse.LeftButton == ButtonState.Pressed
                        && _prevMouse.LeftButton == ButtonState.Released;
            bool released = mouse.LeftButton == ButtonState.Released
                         && _prevMouse.LeftButton == ButtonState.Pressed;
            bool rightPressed = mouse.RightButton == ButtonState.Pressed
                             && _prevMouse.RightButton == ButtonState.Released;

            ComputeLayout();

            if (_goldInputActive)
            {
                HandleGoldInput(keyboard);

                if (clicked)
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

            var groupedInv = GetGroupedInventory();
            var groupedOffer = GetGroupedOffer();
            int invIdx = FindInvSlotAt(mouse.X, mouse.Y);
            int offerIdx = FindOfferSlotAt(mouse.X, mouse.Y);

            // ЛКМ: двойной клик — передать предмет, иначе — начать перетаскивание
            if (clicked && _dragIdx < 0)
            {
                if (invIdx >= 0 && invIdx < groupedInv.Count)
                {
                    var now = gameTime.TotalGameTime;
                    if (_lastClickInvIdx == invIdx && (now - _lastClickInvTime).TotalMilliseconds < 400)
                    {
                        TransferInventoryToOffer(groupedInv[invIdx].Key, false);
                        _lastClickInvIdx = -1;
                    }
                    else
                    {
                        _lastClickInvIdx = invIdx;
                        _lastClickInvTime = now;
                        _dragFromPanel = 0;
                        _dragIdx = invIdx;
                        int col = invIdx % InvCols;
                        int row = (invIdx - _scrollOffset) / InvCols;
                        var slot = GetInvSlotRect(col, row);
                        _dragOffset = new Point(mouse.X - slot.X, mouse.Y - slot.Y);
                        _dragPos = new Point(mouse.X, mouse.Y);
                    }
                }
                else if (offerIdx >= 0 && offerIdx < groupedOffer.Count)
                {
                    var now = gameTime.TotalGameTime;
                    if (_lastClickOfferIdx == offerIdx && (now - _lastClickOfferTime).TotalMilliseconds < 400)
                    {
                        TransferOfferToInventory(groupedOffer[offerIdx].Key, false);
                        _lastClickOfferIdx = -1;
                    }
                    else
                    {
                        _lastClickOfferIdx = offerIdx;
                        _lastClickOfferTime = now;
                        _dragFromPanel = 1;
                        _dragIdx = offerIdx;
                        int col = offerIdx % OfferCols;
                        int row = (offerIdx - _offerScroll) / OfferCols;
                        var slot = GetMyOfferSlotRect(col, row);
                        _dragOffset = new Point(mouse.X - slot.X, mouse.Y - slot.Y);
                        _dragPos = new Point(mouse.X, mouse.Y);
                    }
                }
                else
                {
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
            }

            if (_dragIdx >= 0 && mouse.LeftButton == ButtonState.Pressed)
            {
                _dragPos = new Point(mouse.X, mouse.Y);
            }

            if (_dragIdx >= 0 && released)
            {
                bool droppedOnOffer = _dragFromPanel == 0 && _myOfferPanelRect.Contains(mouse.X, mouse.Y);
                bool droppedOnInv = _dragFromPanel == 1 && _invPanelRect.Contains(mouse.X, mouse.Y);

                if (droppedOnOffer)
                {
                    var src = _dragFromPanel == 0 ? groupedInv : groupedOffer;
                    if (_dragIdx >= 0 && _dragIdx < src.Count)
                        TransferInventoryToOffer(src[_dragIdx].Key, false);
                }
                else if (droppedOnInv)
                {
                    var src = _dragFromPanel == 0 ? groupedInv : groupedOffer;
                    if (_dragIdx >= 0 && _dragIdx < src.Count)
                        TransferOfferToInventory(src[_dragIdx].Key, false);
                }
                _dragIdx = -1;
                _dragFromPanel = -1;
            }

            // ПКМ: передать предмет; Shift+ПКМ — весь стак (для стакаемых)
            if (rightPressed && _dragIdx < 0)
            {
                bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
                if (invIdx >= 0 && invIdx < groupedInv.Count)
                    TransferInventoryToOffer(groupedInv[invIdx].Key, shift);
                else if (offerIdx >= 0 && offerIdx < groupedOffer.Count)
                    TransferOfferToInventory(groupedOffer[offerIdx].Key, shift);
            }

            HandleScrollClick(mouse);
            HandleMouseWheel(mouse);

            _prevMouse = mouse;
            _prevScrollWheel = mouse.ScrollWheelValue;
            _prevKeyboard = keyboard;
            base.Update(gameTime, keyboard, mouse);
        }

        private int FindInvSlotAt(int mx, int my)
        {
            for (int r = 0; r < _invVisibleRows; r++)
                for (int c = 0; c < InvCols; c++)
                    if (GetInvSlotRect(c, r).Contains(mx, my))
                        return r * InvCols + c + _scrollOffset;
            return -1;
        }

        private int FindOfferSlotAt(int mx, int my)
        {
            for (int r = 0; r < OfferRows; r++)
                for (int c = 0; c < OfferCols; c++)
                    if (GetMyOfferSlotRect(c, r).Contains(mx, my))
                        return r * OfferCols + c + _offerScroll;
            return -1;
        }

        private static bool IsStackable(TradeItemData it) => it.MaxStack > 1;

        private void TransferInventoryToOffer(string itemId, bool wholeStack)
        {
            var item = GetAvailableInventory().FirstOrDefault(i => i.Id == itemId);
            if (item == null) return;
            int available = item.Quantity;
            if (available <= 0) return;

            Logger.Debug($"ОБМЕН: передача из инвентаря '{item.Name}' (id={itemId}), доступно={available}, wholeStack={wholeStack}");

            if (wholeStack)
            {
                AddToOffer(itemId, available);
                NotifyOfferChanged();
            }
            else if (IsStackable(item) && available > 1)
            {
                RequestQuantity?.Invoke(item.Name ?? "", available, 1, qty =>
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

        private void TransferOfferToInventory(string itemId, bool wholeStack)
        {
            int count = _myOfferItems.Where(o => o.Id == itemId).Sum(o => Math.Max(1, o.Quantity));
            if (count <= 0) return;

            Logger.Debug($"ОБМЕН: забор из оффера (id={itemId}), доступно={count}, wholeStack={wholeStack}");

            if (wholeStack)
            {
                var existing = _myOfferItems.FirstOrDefault(o => o.Id == itemId);
                if (existing != null) _myOfferItems.Remove(existing);
                NotifyOfferChanged();
            }
            else if (count > 1)
            {
                RequestQuantity?.Invoke("предмет", count, 1, qty =>
                {
                    RemoveFromOffer(itemId, qty);
                    NotifyOfferChanged();
                });
            }
            else
            {
                RemoveFromOffer(itemId, 1);
                NotifyOfferChanged();
            }
        }

        private void HandleMouseWheel(MouseState mouse)
        {
            int delta = mouse.ScrollWheelValue - _prevScrollWheel;
            if (delta == 0) return;

            int step = InvCols;
            if (_myOfferPanelRect.Contains(mouse.X, mouse.Y) && _offerMaxScroll > 0)
            {
                int stepO = OfferCols;
                if (delta < 0) _offerScroll = Math.Min(_offerMaxScroll, _offerScroll + stepO);
                else _offerScroll = Math.Max(0, _offerScroll - stepO);
            }
            else if (_theirOfferPanelRect.Contains(mouse.X, mouse.Y) && _theirMaxScroll > 0)
            {
                int stepO = OfferCols;
                if (delta < 0) _theirScroll = Math.Min(_theirMaxScroll, _theirScroll + stepO);
                else _theirScroll = Math.Max(0, _theirScroll - stepO);
            }
            else if (_invPanelRect.Contains(mouse.X, mouse.Y) && _maxScroll > 0)
            {
                if (delta < 0) _scrollOffset = Math.Min(_maxScroll, _scrollOffset + step);
                else _scrollOffset = Math.Max(0, _scrollOffset - step);
            }
        }

        private void HandleScrollClick(MouseState mouse)
        {
            bool justClicked = mouse.LeftButton == ButtonState.Pressed
                            && _prevMouse.LeftButton == ButtonState.Released;
            if (!justClicked) return;

            // Инвентарь
            int gridW = InvCols * _invCellSize + (InvCols - 1) * CellGap;
            if (_scrollOffset > 0)
            {
                var upBtn = new Rectangle(_invPanelRect.X + 8 + gridW + 4, _invPanelRect.Y + PanelHeaderH, 14, 14);
                if (upBtn.Contains(mouse.X, mouse.Y))
                    _scrollOffset = Math.Max(0, _scrollOffset - InvCols);
            }
            if (_scrollOffset < _maxScroll)
            {
                var dnBtn = new Rectangle(_invPanelRect.X + 8 + gridW + 4, _invPanelRect.Y + PanelHeaderH + _invVisibleRows * (_invCellSize + CellGap) - 14, 14, 14);
                if (dnBtn.Contains(mouse.X, mouse.Y))
                    _scrollOffset = Math.Min(_maxScroll, _scrollOffset + InvCols);
            }

            // Мой оффер
            int offerGridW = OfferCols * _offerCellSize + (OfferCols - 1) * CellGap;
            if (_offerScroll > 0)
            {
                var upBtn = new Rectangle(_myOfferPanelRect.X + 8 + offerGridW + 4, _myOfferPanelRect.Y + PanelHeaderH, 14, 14);
                if (upBtn.Contains(mouse.X, mouse.Y))
                    _offerScroll = Math.Max(0, _offerScroll - OfferCols);
            }
            if (_offerScroll < _offerMaxScroll)
            {
                var dnBtn = new Rectangle(_myOfferPanelRect.X + 8 + offerGridW + 4, _myOfferPanelRect.Y + PanelHeaderH + OfferRows * (_offerCellSize + CellGap) - 14, 14, 14);
                if (dnBtn.Contains(mouse.X, mouse.Y))
                    _offerScroll = Math.Min(_offerMaxScroll, _offerScroll + OfferCols);
            }

            // Оффер противника
            if (_theirScroll > 0)
            {
                var upBtn = new Rectangle(_theirOfferPanelRect.X + 8 + offerGridW + 4, _theirOfferPanelRect.Y + PanelHeaderH, 14, 14);
                if (upBtn.Contains(mouse.X, mouse.Y))
                    _theirScroll = Math.Max(0, _theirScroll - OfferCols);
            }
            if (_theirScroll < _theirMaxScroll)
            {
                var dnBtn = new Rectangle(_theirOfferPanelRect.X + 8 + offerGridW + 4, _theirOfferPanelRect.Y + PanelHeaderH + OfferRows * (_offerCellSize + CellGap) - 14, 14, 14);
                if (dnBtn.Contains(mouse.X, mouse.Y))
                    _theirScroll = Math.Min(_theirMaxScroll, _theirScroll + OfferCols);
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

        private void AddToOffer(string itemId, int qty)
        {
            var item = _myInventoryItems.FirstOrDefault(i => i.Id == itemId);
            if (item == null) return;
            var existing = _myOfferItems.FirstOrDefault(o => o.Id == itemId);
            if (existing != null)
            {
                existing.Quantity += qty;
                return;
            }
            _myOfferItems.Add(MakeTradeItem(item, qty));
        }

        private static TradeItemData MakeTradeItem(TradeItemData src, int qty) => src.WithQuantity(qty);

        private void RemoveFromOffer(string itemId, int qty)
        {
            var existing = _myOfferItems.FirstOrDefault(o => o.Id == itemId);
            if (existing == null) return;
            existing.Quantity -= qty;
            if (existing.Quantity <= 0)
                _myOfferItems.Remove(existing);
        }

        private List<KeyValuePair<string, int>> GetGroupedOffer()
        {
            return _myOfferItems
                .GroupBy(i => i.Id)
                .Select(g => new KeyValuePair<string, int>(g.Key!, g.Sum(i => Math.Max(1, i.Quantity))))
                .ToList();
        }

        private List<TradeItemData> GetAvailableInventory()
        {
            var offeredQty = _myOfferItems
                .GroupBy(i => i.Id)
                .ToDictionary(g => g.Key!, g => g.Sum(i => Math.Max(1, i.Quantity)));

            var result = new List<TradeItemData>();
            foreach (var inv in _myInventoryItems)
            {
                int avail = inv.Quantity - (offeredQty.TryGetValue(inv.Id ?? "", out var q) ? q : 0);
                if (avail <= 0) continue;
                result.Add(inv.WithQuantity(avail));
            }
            return result;
        }

        private List<KeyValuePair<string, int>> GetGroupedInventory()
        {
            return GetAvailableInventory()
                .GroupBy(i => i.Id)
                .Select(g => new KeyValuePair<string, int>(g.Key!, g.Sum(i => Math.Max(1, i.Quantity))))
                .ToList();
        }

        private List<KeyValuePair<string, int>> GetGroupedTheirOffer()
        {
            return _theirOfferItems
                .GroupBy(i => i.Id)
                .Select(g => new KeyValuePair<string, int>(g.Key!, g.Sum(i => Math.Max(1, i.Quantity))))
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
            UIHelper.DrawRectOutline(sb, new Rectangle(X, Y, Width, Height), new Color(80, 90, 110));

            var titleSize = font.MeasureString(Title);
            sb.DrawString(font, Title, new Vector2(X + 8, Y + (TitleH - titleSize.Y) / 2), Color.White);

            ComputeLayout();

            DrawInvPanel(sb, font, mouse);
            DrawMyOfferPanel(sb, font, mouse);
            DrawTheirOfferPanel(sb, font, mouse);

            int cx = ContentX;
            int by = ContentY + ContentH - BottomBarH + 6;

            string myLabel = _iConfirmed ? "Вы: подтверждено" : "Вы: не подтверждено";
            DrawText(sb, myLabel, cx, by, _iConfirmed ? Color.LimeGreen : Color.Red, font);

            string theirLabel = _otherConfirmed ? $"{_otherName}: подтверждено" : $"{_otherName}: не подтверждено";
            DrawText(sb, theirLabel, cx + 200, by, _otherConfirmed ? Color.LimeGreen : Color.Red, font);

            int rightX = ContentX + ContentW - 8;
            string cText = _iConfirmed ? "Отменить" : "Подтвердить";
            Color cBg = _iConfirmed ? CConfirmActive : CConfirm;
            var confirmBtn = new Rectangle(rightX - 240, by, 130, 26);
            bool confirmHover = confirmBtn.Contains(mouse.X, mouse.Y);
            sb.Draw(SpriteCache.Pixel, confirmBtn, confirmHover ? new Color(60, 150, 60) : cBg);
            var cSize = font.MeasureString(cText);
            sb.DrawString(font, cText, new Vector2(confirmBtn.X + (confirmBtn.Width - cSize.X) / 2, confirmBtn.Y + (confirmBtn.Height - cSize.Y) / 2), Color.White);

            var cancelBtn = new Rectangle(rightX - 100, by, 100, 26);
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

            if (_dragIdx >= 0)
            {
                var grouped = _dragFromPanel == 0 ? GetGroupedInventory() : GetGroupedOffer();
                if (_dragIdx < grouped.Count)
                {
                    var item = _dragFromPanel == 0
                        ? GetAvailableInventory().First(i => i.Id == grouped[_dragIdx].Key)
                        : _myOfferItems.First(i => i.Id == grouped[_dragIdx].Key);
                    var spr = SpriteCache.ForItem(item.Type, item.WeaponSubtype);
                    int sz = 36;
                    var dst = new Rectangle(_dragPos.X - _dragOffset.X, _dragPos.Y - _dragOffset.Y, sz, sz);
                    if (spr != null)
                    {
                        sb.Draw(spr, dst, Color.White);
                        var qFrame = SpriteCache.ForQualityFrame(ItemQualityExtensions.ParseFromDescription(item.Description));
                        if (qFrame != null)
                            sb.Draw(qFrame, dst, Color.White);
                    }
                    else
                        sb.Draw(SpriteCache.Pixel, dst, new Color(180, 140, 60, 200));
                    if (grouped[_dragIdx].Value > 1 && font != null)
                        sb.DrawString(font, grouped[_dragIdx].Value.ToString(),
                            new Vector2(dst.Right - 14, dst.Bottom - 14), Color.White);
                }
            }
        }

        private void DrawPanel(SpriteBatch sb, Rectangle rect, string header)
        {
            sb.Draw(SpriteCache.Pixel, rect, CPanelBg);
            DrawBorder(sb, rect, CPanelBorder, 2);
            var font = SpriteCache.FontSmall ?? SpriteCache.Font;
            if (font != null)
                sb.DrawString(font, header, new Vector2(rect.X + 8, rect.Y + 6), Color.White);
        }

        private void DrawInvPanel(SpriteBatch sb, SpriteFont font, MouseState mouse)
        {
            DrawPanel(sb, _invPanelRect, $"Ваш инвентарь ({GetAvailableInventory().Count} шт.)");

            var groupedInv = GetGroupedInventory();
            _maxScroll = Math.Max(0, (groupedInv.Count + InvCols - 1) / InvCols - _invVisibleRows);

            for (int r = 0; r < _invVisibleRows; r++)
                for (int c = 0; c < InvCols; c++)
                {
                    var rect = GetInvSlotRect(c, r);
                    int uniqueIdx = r * InvCols + c + _scrollOffset;
                    bool filled = uniqueIdx < groupedInv.Count;
                    bool hover = rect.Contains(mouse.X, mouse.Y);

                    sb.Draw(SpriteCache.Pixel, rect, hover ? CFieldHover : CFieldBg);
                    UIHelper.DrawRectOutline(sb, rect, CFieldBorder);

                    if (filled)
                    {
                        var item = GetAvailableInventory().First(i => i.Id == groupedInv[uniqueIdx].Key);
                        if (hover) _hoverItem = item;
                        var spr = SpriteCache.ForItem(item.Type, item.WeaponSubtype);
                        if (spr != null)
                        {
                            var iconRect = new Rectangle(rect.X + 4, rect.Y + 4, _invCellSize - 8, _invCellSize - 8);
                            sb.Draw(spr, iconRect, Color.White);
                            var qFrame = SpriteCache.ForQualityFrame(ItemQualityExtensions.ParseFromDescription(item.Description));
                            if (qFrame != null)
                                sb.Draw(qFrame, iconRect, Color.White);
                        }

                        int count = groupedInv[uniqueIdx].Value;
                        if (count > 1)
                            DrawText(sb, count.ToString(), rect.X + _invCellSize - 14, rect.Y + _invCellSize - 14, new Color(230, 230, 120), font);
                    }
                }

            int gridW = InvCols * _invCellSize + (InvCols - 1) * CellGap;
            if (_scrollOffset > 0)
            {
                var upBtn = new Rectangle(_invPanelRect.X + 8 + gridW + 4, _invPanelRect.Y + PanelHeaderH, 14, 14);
                DrawText(sb, "^", upBtn.X + 3, upBtn.Y, CLight, font);
            }
            if (_scrollOffset < _maxScroll)
            {
                var dnBtn = new Rectangle(_invPanelRect.X + 8 + gridW + 4, _invPanelRect.Y + PanelHeaderH + _invVisibleRows * (_invCellSize + CellGap) - 14, 14, 14);
                DrawText(sb, "v", dnBtn.X + 3, dnBtn.Y, CLight, font);
            }
        }

        private void DrawMyOfferPanel(SpriteBatch sb, SpriteFont font, MouseState mouse)
        {
            DrawPanel(sb, _myOfferPanelRect, "Ваш оффер");

            var groupedOffer = GetGroupedOffer();
            _offerMaxScroll = Math.Max(0, (groupedOffer.Count + OfferCols - 1) / OfferCols - OfferRows);
            _offerScroll = Math.Min(_offerScroll, _offerMaxScroll);

            for (int r = 0; r < OfferRows; r++)
                for (int c = 0; c < OfferCols; c++)
                {
                    var rect = GetMyOfferSlotRect(c, r);
                    int uniqueIdx = r * OfferCols + c + _offerScroll;
                    bool filled = uniqueIdx < groupedOffer.Count;
                    bool hover = rect.Contains(mouse.X, mouse.Y);

                    sb.Draw(SpriteCache.Pixel, rect, hover ? CFieldHover : CFieldBg);
                    UIHelper.DrawRectOutline(sb, rect, CFieldBorder);

                    if (filled)
                    {
                        var kvp = groupedOffer[uniqueIdx];
                        var item = _myOfferItems.First(i => i.Id == kvp.Key);
                        if (hover) _hoverItem = item;
                        var spr = SpriteCache.ForItem(item.Type, item.WeaponSubtype);
                        if (spr != null)
                        {
                            var iconRect = new Rectangle(rect.X + 4, rect.Y + 4, _offerCellSize - 8, _offerCellSize - 8);
                            sb.Draw(spr, iconRect, Color.White);
                            var qFrame = SpriteCache.ForQualityFrame(ItemQualityExtensions.ParseFromDescription(item.Description));
                            if (qFrame != null)
                                sb.Draw(qFrame, iconRect, Color.White);
                        }

                        if (kvp.Value > 1)
                            DrawText(sb, kvp.Value.ToString(), rect.X + _offerCellSize - 14, rect.Y + _offerCellSize - 14, new Color(230, 230, 120), font);
                    }
                }

            int offerGridW = OfferCols * _offerCellSize + (OfferCols - 1) * CellGap;
            if (_offerScroll > 0)
            {
                var upBtn = new Rectangle(_myOfferPanelRect.X + 8 + offerGridW + 4, _myOfferPanelRect.Y + PanelHeaderH, 14, 14);
                DrawText(sb, "^", upBtn.X + 3, upBtn.Y, CLight, font);
            }
            if (_offerScroll < _offerMaxScroll)
            {
                var dnBtn = new Rectangle(_myOfferPanelRect.X + 8 + offerGridW + 4, _myOfferPanelRect.Y + PanelHeaderH + OfferRows * (_offerCellSize + CellGap) - 14, 14, 14);
                DrawText(sb, "v", dnBtn.X + 3, dnBtn.Y, CLight, font);
            }

            // Золото
            int goldY = _myOfferPanelRect.Y + PanelHeaderH + OfferRows * (_offerCellSize + CellGap) + 10;
            DrawText(sb, "Золото:", _myOfferPanelRect.X + 8, goldY, CGold, font);

            var goldRect = GetGoldInputRect();
            bool goldHover = goldRect.Contains(mouse.X, mouse.Y);
            sb.Draw(SpriteCache.Pixel, goldRect, _goldInputActive ? CGoldInputActive : (goldHover ? CFieldHover : CGoldInput));
            UIHelper.DrawRectOutline(sb, goldRect, _goldInputActive ? Color.Gold : CFieldBorder);

            string goldDisplay;
            if (_goldInputActive)
            {
                goldDisplay = _goldInputBuffer.ToString();
                if ((Environment.TickCount / 500) % 2 == 0)
                    goldDisplay += "|";
            }
            else
            {
                goldDisplay = _myGoldOffer > 0 ? _myGoldOffer.ToString() : "0";
            }
            DrawText(sb, goldDisplay, goldRect.X + 6, goldY + 1, Color.White, font);

            int myGoldLeft;
            if (_goldInputActive && int.TryParse(_goldInputBuffer.ToString(), out int bufGold))
                myGoldLeft = _myTotalGold - bufGold;
            else
                myGoldLeft = _myTotalGold - _myGoldOffer;
            string goldLimit = $"/ {Math.Max(0, myGoldLeft)}";
            var limitSize = font.MeasureString(goldLimit);
            DrawText(sb, goldLimit, _myOfferPanelRect.Right - 8 - (int)limitSize.X, goldY + 1, new Color(160, 160, 170), font);
        }

        private void DrawTheirOfferPanel(SpriteBatch sb, SpriteFont font, MouseState mouse)
        {
            DrawPanel(sb, _theirOfferPanelRect, $"Оффер {_otherName}");

            var groupedTheir = GetGroupedTheirOffer();
            _theirMaxScroll = Math.Max(0, (groupedTheir.Count + OfferCols - 1) / OfferCols - OfferRows);
            _theirScroll = Math.Min(_theirScroll, _theirMaxScroll);

            for (int r = 0; r < OfferRows; r++)
                for (int c = 0; c < OfferCols; c++)
                {
                    var rect = GetTheirOfferSlotRect(c, r);
                    int uniqueIdx = r * OfferCols + c + _theirScroll;
                    bool filled = uniqueIdx < groupedTheir.Count;
                    bool hover = rect.Contains(mouse.X, mouse.Y);

                    sb.Draw(SpriteCache.Pixel, rect, hover ? CFieldHover : CFieldBg);
                    UIHelper.DrawRectOutline(sb, rect, CFieldBorder);

                    if (filled)
                    {
                        var kvp = groupedTheir[uniqueIdx];
                        var item = _theirOfferItems.First(i => i.Id == kvp.Key);
                        if (hover) _hoverItem = item;
                        var spr = SpriteCache.ForItem(item.Type, item.WeaponSubtype);
                        if (spr != null)
                        {
                            var iconRect = new Rectangle(rect.X + 4, rect.Y + 4, _offerCellSize - 8, _offerCellSize - 8);
                            sb.Draw(spr, iconRect, Color.White);
                            var qFrame = SpriteCache.ForQualityFrame(ItemQualityExtensions.ParseFromDescription(item.Description));
                            if (qFrame != null)
                                sb.Draw(qFrame, iconRect, Color.White);
                        }

                        if (kvp.Value > 1)
                            DrawText(sb, kvp.Value.ToString(), rect.X + _offerCellSize - 14, rect.Y + _offerCellSize - 14, new Color(230, 230, 120), font);
                    }
                }

            int offerGridW = OfferCols * _offerCellSize + (OfferCols - 1) * CellGap;
            if (_theirScroll > 0)
            {
                var upBtn = new Rectangle(_theirOfferPanelRect.X + 8 + offerGridW + 4, _theirOfferPanelRect.Y + PanelHeaderH, 14, 14);
                DrawText(sb, "^", upBtn.X + 3, upBtn.Y, CLight, font);
            }
            if (_theirScroll < _theirMaxScroll)
            {
                var dnBtn = new Rectangle(_theirOfferPanelRect.X + 8 + offerGridW + 4, _theirOfferPanelRect.Y + PanelHeaderH + OfferRows * (_offerCellSize + CellGap) - 14, 14, 14);
                DrawText(sb, "v", dnBtn.X + 3, dnBtn.Y, CLight, font);
            }

            int goldY = _theirOfferPanelRect.Y + PanelHeaderH + OfferRows * (_offerCellSize + CellGap) + 10;
            DrawText(sb, $"Золото: {_theirGoldOffer}", _theirOfferPanelRect.X + 8, goldY, CGold, font);
        }

        private void DrawTooltip(SpriteBatch sb, TradeItemData item, MouseState mouse)
        {
            var lines = ItemTooltip.BuildLinesForTrade(
                item.Name ?? "", item.Type ?? "", item.Value,
                item.Attack, item.Defense, item.MaxHealthBonus, item.HealAmount, item.RestoreMana,
                item.Description ?? "");
            var g = GameMain.Instance;
            int wRight = g?.Graphics.PreferredBackBufferWidth ?? 1920;
            int wBottom = g?.Graphics.PreferredBackBufferHeight ?? 1080;
            TooltipRenderer.Draw(sb, lines, mouse, wRight, wBottom);
        }

        private void NotifyOfferChanged()
        {
            var entries = BuildOfferEntries();
            var grouped = entries
                .Select(e => $"{(e.ItemId)} x{e.Quantity}")
                .ToList();
            Logger.Info($"ОБМЕН: отправка оффера на сервер: типов={entries.Count}, золото={_myGoldOffer}");
            foreach (var line in grouped)
                Logger.Debug($"ОБМЕН: оффер -> {line}");
            OfferChanged?.Invoke(entries, _myGoldOffer);
        }

        public List<TradeOfferEntry> BuildOfferEntries()
        {
            return _myOfferItems
                .GroupBy(i => i.Id)
                .Select(gr => new TradeOfferEntry
                {
                    ItemId = gr.Key ?? "",
                    Quantity = gr.Sum(i => Math.Max(1, i.Quantity))
                })
                .ToList();
        }

        private static bool pressed(Rectangle rect, MouseState mouse)
        {
            return rect.Contains(mouse.X, mouse.Y) && mouse.LeftButton == ButtonState.Pressed;
        }

        private static void DrawBorder(SpriteBatch sb, Rectangle r, Color color, int thickness)
        {
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, r.Width, thickness), color);
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.X, r.Y, thickness, r.Height), color);
            sb.Draw(SpriteCache.Pixel, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);
        }

        private static new void DrawText(SpriteBatch sb, string text, int x, int y, Color color, SpriteFont font)
            => UIHelper.DrawText(sb, text, x, y, color, font);
    }
}
