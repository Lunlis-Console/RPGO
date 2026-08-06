# Промпт для следующей сессии: Личное хранилище (Storage Chest)

> Прочитай этот файл целиком перед реализацией. Все факты ниже проверены в коде —
> не выдумывай имена файлов, сообщений, ресурсов и таблиц, если их нет в этом списке.

## Задача
Добавить персональное постоянное хранилище (склад) для предметов игрока:

1. На главной карте рядом с торговцем стоит сундук-склад (`Chest_SS.png`, кадры 0–1).
2. Клик по сундуку открывает окно хранилища (двухпанельное: инвентарь слева ⇄ склад справа)
   с переносом предметов и диалогом количества для стакаемых.
3. Хранилище персональное и персистентное (переживает рестарт сервера), лимит слотов.
4. Сундук инстанса (в подземельях) перерисовать с кадров-прямоугольников на кадры 2–3
   (2 = закрыт, 3 = открыт). Остальные кадры спрайт-листа (4–11) не трогать.

## Подтверждённые решения пользователя (менять не нужно)
- Сундук-склад стоит рядом с торговцем (свободная клетка рядом с `Merchant (50,50)`).
- Хранилище персональное, ограниченное по слотам (по умолчанию 60, константа в `Balance`).
- UI — двухпанельное окно инвентарь⇄склад, для стакаемых — диалог количества.
- Сундук инстанса: кадры 2–3 (закрыт/открыт).

## Проверенные факты (якоря против галлюцинаций)

### Спрайт
- Файл: `LostAndDivine.ClientMonoGame/Content/Sprites/SpriteSheets/Chest_SS.png`, 768x64 px → 12 кадров по 64x64.
  **Карта кадров:** idx0 = склад закрыт, idx1 = склад открыт, idx2 = инстанс закрыт, idx3 = инстанс открыт.
- Ключ загрузки (embedded resource): `LostAndDivine.ClientMonoGame.Content.Sprites.SpriteSheets.Chest_SS.png`.
  В `LostAndDivine.ClientMonoGame.csproj` подключено `Content\Sprites\**\*.*` как EmbeddedResource.
  Паттерн загрузки как в `SpriteCache.cs:103-105`:
  `LoadTexture("chest_ss", "LostAndDivine.ClientMonoGame.Content.Sprites.SpriteSheets.Chest_SS.png")`.

### Карта/сущности (клиент, `Rendering/MapRenderer.cs`)
- `RebuildSpatialHash(WorldMap map)` (:443) — строит `_spatialHash[(x,y)] = List<EntityInfo>`.
  `EntityInfo { Type, Name, Level, Hp, MaxHp, X, Y, Id, Info }`.
  Мерчант добавляется в :467-476 (Type="merchant"). **Добавь туда же Type="storage_chest"**
  из нового поля `map.StorageChest` (аналогично мерчанту).
- Клик: `HandleClick` → `GetEntitiesAt` → `HandleSingleEntityClick`/`StartInteraction` →
  активация `ActivateSelection()` (:597) → событие `InteractRequested?.Invoke(entity, x, y)` (:608).
  ПКМ сразу инвокает `InteractRequested` (:822).
- Курсор: `GetCursorType` (:713), switch на :736 — добавь `"storage_chest" => "talk"`.
- Отрисовка статичных: `DrawStaticEntities` (:1313), мерчант/доска — `DrawStatic(...)`.
  Склад рисуй там же через кадры 0/1.
- Инстанс-сундук сейчас — прямоугольники в :1186-1199. **Замени на кадры 2/3**
  (IsLocked → idx2, иначе idx3).
- Подсветка выбора: :1814 (паттерн `if (_selectedEntityType == "merchant" ...)`) — добавь storage_chest.
- Клиентский путь `ClientPathfinding.FindPath` (:1217) исключает клетки мерчанта/доски по координатам
  и блокированные клетки через `isBlocked` (=`IsBlocked`, читает ObstacleData). Если клетка склада —
  препятствие в ObstacleData, клиентский поиск пути обойдёт её автоматически.

### Карта/объекты (сервер)
- `GameWorld.cs`: `GameMap.AddObstacle(x,y)` (:69), `IsObstacle` (:71), `GetObstacleData()` (:77)
  — отдаёт клиенту 1 для препятствий. **Клетку склада добавь через `AddObstacle`** — это закроет
  и клиентский курсор, и серверный pathfinding (`PathfindingService` :6 блокирует `IsObstacle`).
- `Program.cs`: после `merchant.Initialize()` (:81) позиция торговца известна. Там вычисли свободную
  клетку рядом с торговцем (например, сканируй кольцо клеток, не занятых `world.Map.IsObstacle`),
  добавь её в препятствия и сохрани координаты склада (в `GameMap` нового поля StorageChestX/Y
  или в новом сервисе).
- `Network/GameServer.cs` `BroadcastMapAsync` (:116): мерчант и доска отправляются только для зоны
  `zoneId == "main"` (:240-241), `InstanceChest` — только для инстансов (:280-285).
  **Добавь `mapData.StorageChest = new ChestPosition { X=..., Y=... }` для "main".**
- `Shared/Models/WorldMap.cs`: добавь `public ChestPosition? StorageChest { get; set; }`.
  `ChestPosition { X, Y, IsLocked }` уже есть.

### Взаимодействие (сервер)
- `MessageHandlers/InteractTargetHandler.cs` (:128+): для не-монстра/не-игрока — `player.Combat.Cancel()`,
  если `distToTarget <= Balance.InteractRange` → `player.Interaction.Begin(entityType, targetX, targetY, null)`
  и `ProcessPendingInteraction`. Для склада менять хендлер НЕ нужно — ветка generic уже обрабатывает
  Type="storage_chest" (MonsterId в запросе сервер игнорирует).
- `Services/InteractionService.cs` `ProcessPendingInteraction` (:28) — `switch (interactionType)` с кейсами
  "monster","merchant","board","npc","chest","collectible","loot_corpse","player","take_loot".
  **Добавь кейс "storage_chest"** → `await _svc.Storage.OpenFor(client, player);` (или эквивалент).
- Регистрация серверных обработчиков: `MessageHandlers/MessageHandlerRegistry.cs` `RegisterAll` (:23),
  паттерн `Register("type", new XxxHandler(svc))`. Хендлеры наследуют `BaseHandler`
  (доступны `Svc`, `World`, `SendToClient`, `SendError`, `BroadcastMapAsync`).

### Сообщения (единые имена — используй именно эти)
- Сервер→клиент: `storage_open` (payload: `Items` (List<Item>), `Slots` (int)), `storage_update`
  (payload: `Items` (List<Item>), плюс после deposit/withdraw также `inventory_response` через
  `Svc.Hub.SendInventoryAndStatus(client, player)`).
- Клиент→сервер: `storage_deposit` `{ ItemId, Quantity }`, `storage_withdraw` `{ ItemId, Quantity }`.

### Реестры сообщений (обе стороны)
- Сервер: `MessageHandlerRegistry.RegisterAll` (см. выше) + классы в `LostAndDivine.Server/MessageHandlers/`.
- Клиент: `Networking/ClientMessageHandlerRegistry.cs` — словарь `_handlers` (:14), хендлер вызывает
  `c.RaiseXxx(...)`. События объявлять в `Networking/GameClient.cs` (паттерн :46-112, `RaiseShopUpdated`
  :140 — `Ui(() => ShopUpdated?.Invoke(...))`). Подписка в `Screens/GameScreen.cs` (паттерн :374-379).

### База данных
- `Repositories/Db.cs`: `Db.Open()` (game.db runtime), `Db.OpenContent()` (content.db), `Db.Lock`.
- Миграции: FluentMigrator `[Migration(N)]`, классы в `LostAndDivine.Shared/Migrations/`, `ForwardOnlyMigration`.
  Следующий номер — **1032**. Пример: `1031_AddSessionTokens.cs`.
  **ВАЖНО:** `MigrationRunner.RunMigrations` прогоняет ВСЕ миграции на обеих БД (game.db и content.db).
  Создание таблицы в 1032 — ок (лишняя таблица в content.db не вредит).
  **НЕ добавляй seed-строки (NPC/предметы) в миграцию** — продублируется в обеих БД.
- Паттерн персистентности инвентаря: `Repositories/InventoryRepository.cs` (`lock (Db.Lock)`,
  колонки `item_id, name, type, value, ..., template_id, quantity, ...`).
  Для склада проще всего таблица `player_storage (player_name TEXT PRIMARY KEY, items_json TEXT NOT NULL)`
  с сериализацией `List<Item>` через `System.Text.Json.JsonSerializer.Serialize`
  (паттерн уже используется для `player_equipment.item_data` в `InventoryRepository.SaveEquipment` :243).
- Стак-логика: `InventoryHelper.AddItem`/`RemoveFromRecord`/`RemoveQuantity` (`LostAndDivine.Server/InventoryHelper.cs`),
  `Balance.MaxStackForType`, `InventoryRepository.SyncItemFromTemplate`.
  Лимит слотов склада — количество записей в списке: `Balance.StorageSlots` (например, 60).

### UI клиента (окна)
- Окна — подклассы `GameWindow` в `LostAndDivine.ClientMonoGame/Windows/`. Пример двупанельного + грид + drag +
  тултипы: `Windows/ShopWindow.cs` (GridCols/GridRows, `_slotRects`, `SpriteCache.ForItem`,
  `DrawTooltip`, `DropOnInventory`, `DragStateChanged`, `DrawScrollbar`). Паттерн инвентаря — `Windows/InventoryWindow.cs`.
- Диалог количества: `_input.OpenQuantity(itemName, max, pricePerUnit, onConfirm, showPrice, _quantityDialog, GameMain.Instance!)`
  (сигнатура в `Screens/GameInputHandler.cs:436`). Уже используется для buy/sell/drop (:396-421) —
  повтори для deposit/withdraw.
- Окна регистрируются в `GameScreen.cs` (`_windows.Add(_shopWindow);` :636). `WindowManager`:
  `Add`, `BringToFront`, `IsMouseOverVisibleWindow`.

## План реализации (порядок)
1. **Shared**: `WorldMap.StorageChest`, миграция `1032_AddPlayerStorage.cs` (таблица `player_storage`).
2. **Server**: `Balance.StorageSlots`; новый `StorageService` (+ поле в `GameServices` по паттерну
   `Trade`/`Collectibles` — прочитай `LostAndDivine.Server/GameServices.cs` и `Program.cs:116` перед добавлением
   параметра в конструктор); хендлеры `StorageOpen/Deposit/Withdraw` + регистрация в Registry;
   кейс `"storage_chest"` в `InteractionService`.
3. **Server/Program**: вычислить клетку склада рядом с торговцем, `world.Map.AddObstacle(...)`,
   передать координаты в `StorageService`.
4. **Server/GameServer**: в `BroadcastMapAsync` заполнить `mapData.StorageChest` для "main".
5. **Client**: `SpriteCache` — загрузка `chest_ss`; `WorldMap` уже содержит StorageChest.
   `MapRenderer` — entity в spatial hash, курсор "talk", отрисовка кадра 0/1, подсветка выбора,
   замена прямоугольников инстанс-сундука на кадры 2/3.
6. **Client**: события/хендлеры `storage_open`/`storage_update`; `StorageWindow` (двухпанельный),
   deposit/withdraw с `OpenQuantity`; регистрация окна в `GameScreen`.
7. Сборка + тесты + ручной тест.

## Верификация
- Сборка: `dotnet build LostAndDivine.Server` и `dotnet build LostAndDivine.ClientMonoGame` (проверь путь к .sln/csproj).
- Тесты: `dotnet test LostAndDivine.Tests` (сейчас 85/85 зелёных).
- Ручной: запустить сервер, зайти, подойти к сундуку рядом с торговцем, клик → окно открылось;
  положить стакаемый и не-стакаемый предмет; забрать; перезапустить сервер — предметы на месте.
- В этой среде **нет `rg`** — используй MCP-инструмент grep (не Select-String/rg в PowerShell).

## Чего НЕ делать
- Не выдумывай имена сообщений/таблиц/полей — только из списка выше.
- Не добавляй seed-строки в миграции (дублируются в обеих БД).
- Не коммить `LostAndDivine.Server/content.db` вслепую — код-сидинг предпочтительнее.
- Не меняй кадры 4–11 спрайт-листа и не трогай существующую логику торговца/доски.
- Не создавай `MerchantWindow.cs` (окна магазина нет такого — есть `ShopWindow.cs`).
- Не добавляй новое окно/сервис вне перечисленных директорий.
- Интеракт со складом — через существующий `interact_target` Type="storage_chest";
  отдельный клик-механизм НЕ нужен.

## Текущий статус (на момент написания)
- Задача авто-reconnect (предыдущая) — закоммичена и запушена (commit 3ff0188).
- По складу: только исследование, кода нет. Список выше — полный.
