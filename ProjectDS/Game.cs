//Игровая логика: состояние мира, тики, ИИ, спавн, бой, снапшоты и буфер логов.
namespace ProjectDS;

public sealed class Game
{
    //Конфиг игры: лимиты, интервалы, дальности, параметры разделения и т.д.
    private readonly GameConfig _cfg;
    //Конструктор: принимаем конфиг и сохраняем.
    public Game(GameConfig cfg) => _cfg = cfg;

    //Список систем в игре: порядок важен, по индексу прыгаем.
    public List<GameStarSystem> Systems { get; } = new();
    //Индекс текущей системы в списке Systems.
    public int CurrentSystemIndex { get; private set; }
    //Текущая система по индексу.
    public GameStarSystem CurrentSystem => Systems[CurrentSystemIndex];

    //Корабль игрока: создаётся в Init().
    public Ship Player { get; private set; } = null!;

    //Корабли по системам: ключ - индекс системы, значение - список кораблей в этой системе.
    private readonly Dictionary<int, List<Ship>> _shipsBySystem = new();
    //Список кораблей текущей системы: это "живой" список, который меняется в тике.
    public List<Ship> Ships => _shipsBySystem[CurrentSystemIndex];

    //Счёт игрока: растёт за уничтожение врагов.
    public int Score { get; private set; }
    //Номер хода: увеличивается каждый Tick().
    public int Turn { get; private set; }
    //Флаг окончания игры: true, когда игрок уничтожен.
    public bool IsGameOver { get; private set; }

    //Выбранная цель игрока: Guid корабля или null.
    public Guid? SelectedTargetId { get; set; }

    //Генератор случайностей: спавн, урон, параметры кораблей.
    private readonly Random _rng = new();
    //Таймер спавна по системам: сколько ходов осталось до следующего спавна в каждой системе.
    private readonly Dictionary<int, int> _nextSpawnInTurnsBySystem = new();
    //Буфер логов: копим строки между снапшотами, потом "сливаем" в MakeSnapshotAndDrainLogs().
    private readonly List<string> _logBuffer = new();

    //Инициализация новой игры: создаём системы, игрока, сбрасываем счёт и состояние.
    public void Init()
    {
        //Сбрасываем список систем и создаём две тестовые.
        Systems.Clear();
        Systems.Add(new GameStarSystem("Система А", new Planet("Планета А1", new PointN(0.30, 0.50))));
        Systems.Add(new GameStarSystem("Система Б", new Planet("Планета Б1", new PointN(0.70, 0.55))));
        //Стартуем в первой системе.
        CurrentSystemIndex = 0;
        //Сбрасываем счёт и ход.
        Score = 0;
        Turn = 0;
        //Игра не окончена.
        IsGameOver = false;
        //Цель не выбрана.
        SelectedTargetId = null;
        //Сбрасываем корабли и таймеры спавна.
        _shipsBySystem.Clear();
        _nextSpawnInTurnsBySystem.Clear();
        //Создаём списки кораблей и таймеры спавна для каждой системы.
        for (int i = 0; i < Systems.Count; i++)
        {
            _shipsBySystem[i] = new List<Ship>();
            _nextSpawnInTurnsBySystem[i] = RandInt(_cfg.SpawnIntervalMinTurns, _cfg.SpawnIntervalMaxTurnsExclusive);
        }
        //Создаём игрока.
        Player = new Ship
        {
            Name = "Игрок",
            Faction = Faction.Player,
            Pos = new PointN(0.12, 0.18),
            Hp = 125,
            MaxHp = 125,
            Speed = 0.175,
            State = ShipState.Flying,
            AttackCooldownTurns = 0
        };
        //Добавляем игрока в текущую систему.
        Ships.Add(Player);
        //Сбрасываем лог и пишем стартовую строку.
        _logBuffer.Clear();
        Log("Игра началась. Клик по карте - задать курс.");
    }

    //Один тик игры: кулдауны, спавн, ИИ, движение, разделение, смерть, валидация цели.
    public void Tick()
    {
        //Если игра окончена, тик не делаем.
        if (IsGameOver) return;
        //Увеличиваем номер хода.
        Turn++;
        //Берём список кораблей текущей системы.
        var ships = Ships;
        //1)Кулдауны атаки: уменьшаем у всех живых.
        foreach (var s in ships.Where(x => x.IsAlive))
            if (s.AttackCooldownTurns > 0) s.AttackCooldownTurns--;
        //2)Спавн только в текущей системе: уменьшаем таймер и при нуле спавним волну.
        _nextSpawnInTurnsBySystem[CurrentSystemIndex]--;
        if (_nextSpawnInTurnsBySystem[CurrentSystemIndex] <= 0)
        {
            SpawnByStrength();
            _nextSpawnInTurnsBySystem[CurrentSystemIndex] = RandInt(_cfg.SpawnIntervalMinTurns, _cfg.SpawnIntervalMaxTurnsExclusive);
        }
        //3)ИИ: враги и союзники принимают решения.
        foreach (var s in ships.Where(x => x.IsAlive && x.Faction != Faction.Player).ToList())
        {
            //На планете корабли не двигаются и не атакуют.
            if (s.State == ShipState.Landed) continue;
            //Враг атакует игрока/союзников, союзник атакует врагов.
            if (s.Faction == Faction.Enemy) EnemyAI(s);
            else AllyAI(s);
        }
        //4)Движение: летящие корабли идут к destination.
        foreach (var s in ships.Where(x => x.IsAlive && x.State == ShipState.Flying))
        {
            //Если destination задан, двигаемся к нему.
            if (s.Destination is PointN dst)
            {
                s.Pos = MathN.MoveTowards(s.Pos, dst, s.Speed);
                //Если почти дошли, сбрасываем destination.
                if (MathN.Dist(s.Pos, dst) < 0.004) s.Destination = null;
            }
        }
        //5)Разделение: раздвигаем корабли, чтобы не слипались в одну точку.
        if (_cfg.SeparationEnabled && _cfg.SeparationEveryNTicks > 0 && Turn % _cfg.SeparationEveryNTicks == 0)
            ApplySeparation();
        //6)Удаляем мёртвых (кроме игрока): игрок остаётся, чтобы UI мог показать "GAME OVER".
        ships.RemoveAll(s => !s.IsAlive && s.Faction != Faction.Player);
        //7)Смерть игрока: фиксируем game over и пишем лог.
        if (!Player.IsAlive && !IsGameOver)
        {
            IsGameOver = true;
            Log("ИГРА ОКОНЧЕНА: корабль игрока уничтожен.");
        }
        //8)Проверка выбранной цели: если цель исчезла/умерла/стала игроком, сбрасываем.
        if (SelectedTargetId != null)
        {
            var t = ships.FirstOrDefault(s => s.Id == SelectedTargetId.Value);
            if (t == null || !t.IsAlive || t.Faction == Faction.Player) SelectedTargetId = null;
        }
    }

    //Задать destination игроку: вызывается командой CmdSetDestination.
    public void SetPlayerDestination(PointN dest)
    {
        //После game over команды игнорируем.
        if (IsGameOver) return;
        //На планете курс задавать нельзя.
        if (Player.State == ShipState.Landed)
        {
            Log("Ты на планете. Сначала взлети.");
            return;
        }
        //Ставим destination и пишем лог.
        Player.Destination = dest;
        Log($"Курс задан: {dest.X:0.00}, {dest.Y:0.00}");
    }

    //Прыжок в другую систему: переносим игрока и часть преследователей.
    public void JumpToOtherSystem()
    {
        //После game over команды игнорируем.
        if (IsGameOver) return;
        //С планеты прыгать нельзя.
        if (Player.State == ShipState.Landed)
        {
            Log("Нельзя прыгнуть: корабль на планете. Взлети сначала.");
            return;
        }
        //Список кораблей текущей системы.
        var currentShips = Ships;
        //Преследователи: враги, которые гнались за игроком и находятся в радиусе атаки.
        var chasers = currentShips
            .Where(s => s.IsAlive && s.Faction == Faction.Enemy && s.State == ShipState.Flying)
            .Where(s => s.LastChaseTargetId == Player.Id)
            .Where(s => MathN.Dist(s.Pos, Player.Pos) <= _cfg.AttackRange)
            .ToList();
        //Убираем игрока из текущей системы.
        currentShips.Remove(Player);
        //Убираем преследователей из текущей системы.
        foreach (var c in chasers) currentShips.Remove(c);
        //Переключаем систему по кругу.
        CurrentSystemIndex = (CurrentSystemIndex + 1) % Systems.Count;
        //Список кораблей новой системы.
        var newShips = Ships;
        //Добавляем игрока в новую систему, если его там нет.
        if (!newShips.Contains(Player)) newShips.Add(Player);
        //Переносим преследователей в новую систему рядом со стартовой точкой игрока.
        foreach (var c in chasers)
        {
            c.Pos = new PointN(MathN.Clamp01(0.10 + Rand(-0.02, 0.02)), MathN.Clamp01(0.12 + Rand(-0.02, 0.02)));
            c.Destination = null;
            newShips.Add(c);
        }
        //Ставим игрока в стартовую точку новой системы.
        Player.Pos = new PointN(0.10, 0.12);
        Player.Destination = null;
        //Сбрасываем выбранную цель: в новой системе старый Id может быть неактуален.
        SelectedTargetId = null;
        //Пишем лог прыжка.
        Log($"Прыжок выполнен. Текущая: {CurrentSystem.Name} ({CurrentSystem.Planet.Name})." + (chasers.Count > 0 ? $" За тобой прыгнули враги: {chasers.Count}." : ""));
    }

    //Посадка/взлёт: если рядом с планетой, садимся; если на планете, взлетаем.
    public void ToggleLandTakeoff()
    {
        //После game over команды игнорируем.
        if (IsGameOver) return;
        //Планета текущей системы.
        var planet = CurrentSystem.Planet;
        //Расстояние до планеты.
        var dist = MathN.Dist(Player.Pos, planet.Pos);
        //Если летим, пытаемся сесть.
        if (Player.State == ShipState.Flying)
        {
            //Если далеко, посадка запрещена.
            if (dist > 0.06)
            {
                Log("Слишком далеко от планеты, чтобы сесть (подлети ближе).");
                return;
            }
            //Садимся: фиксируем позицию на планете и сбрасываем destination.
            Player.State = ShipState.Landed;
            Player.Pos = planet.Pos;
            Player.Destination = null;
            //Мирная зона: ренегаты в текущей системе снова становятся союзниками.
            int reverted = 0;
            foreach (var s in Ships.Where(s => s.IsAlive && s.Faction == Faction.Enemy && s.Name.StartsWith("Ренегат")))
            {
                s.Faction = Faction.Ally;
                s.Name = "Союзник";
                s.LastChaseTargetId = null;
                reverted++;
            }
            //Пишем лог посадки и "успокоения" союзников.
            Log($"Посадка на {planet.Name}. Враги не атакуют тебя на планете." + (reverted > 0 ? $" Союзники успокоились: {reverted}." : ""));
        }
        else
        {
            //Взлёт: ставим игрока чуть правее планеты.
            Player.State = ShipState.Flying;
            Player.Pos = new PointN(MathN.Clamp01(planet.Pos.X + 0.06), planet.Pos.Y);
            Log("Взлёт выполнен.");
        }
    }

    //Атака игрока: бьём выбранную цель или ближайшую, учитываем дальность и кулдаун.
    public void PlayerAttack()
    {
        //После game over команды игнорируем.
        if (IsGameOver) return;
        //На планете атаковать нельзя.
        if (Player.State == ShipState.Landed)
        {
            Log("Нельзя атаковать: корабль на планете.");
            return;
        }
        //Если кулдаун не ноль, атаковать нельзя.
        if (Player.AttackCooldownTurns > 0)
        {
            Log($"Оружие на перезарядке: {Player.AttackCooldownTurns} т.");
            return;
        }
        //Цель атаки: либо выбранная, либо ближайшая.
        Ship? target = null;
        //1)Если выбран Id, пытаемся найти цель в текущем списке.
        if (SelectedTargetId != null) target = Ships.FirstOrDefault(s => s.Id == SelectedTargetId.Value);
        //2)Если цель не найдена или невалидна, выбираем ближайшую и запоминаем её Id.
        if (target == null || !target.IsAlive || target.Faction == Faction.Player)
        {
            target = Ships.Where(s => s.IsAlive && s.Faction != Faction.Player).OrderBy(s => MathN.Dist(Player.Pos, s.Pos)).FirstOrDefault();
            SelectedTargetId = target?.Id;
        }
        //Если целей нет, выходим.
        if (target == null)
        {
            Log("Целей нет.");
            return;
        }
        //Проверяем дальность.
        var dist = MathN.Dist(Player.Pos, target.Pos);
        if (dist > _cfg.AttackRange)
        {
            Log($"Цель далеко ({dist:0.00}). Дальность {_cfg.AttackRange:0.00}.");
            return;
        }
        //Если атакуем союзника, он становится ренегатом.
        if (target.Faction == Faction.Ally) MakeRenegade(target);
        //Наносим урон цели.
        DealDamage(Player, target, dmg: Rand(5.0, 30.0));
    }

    //Сделать союзника ренегатом: меняем фракцию и заставляем преследовать игрока.
    private void MakeRenegade(Ship ally)
    {
        //Если это не союзник, ничего не делаем.
        if (ally.Faction != Faction.Ally) return;
        //Меняем фракцию и имя.
        ally.Faction = Faction.Enemy;
        ally.Name = "Ренегат";
        //Запоминаем, что он гонится за игроком.
        ally.LastChaseTargetId = Player.Id;
        //Пишем лог.
        Log("Союзник стал враждебным!");
    }

    //ИИ врага: выбирает ближайшую цель (игрок или союзник) и атакует/преследует.
    private void EnemyAI(Ship enemy)
    {
        //Кандидаты: игрок и союзники, но игрок на планете неуязвим и не является целью.
        var candidates = Ships.Where(s => s.IsAlive && (s.Faction == Faction.Player || s.Faction == Faction.Ally))
                              .Where(s => !(s.Faction == Faction.Player && s.State == ShipState.Landed))
                              .ToList();
        //Если целей нет, выходим.
        if (candidates.Count == 0) return;
        //Берём ближайшую цель.
        var target = candidates.OrderBy(s => MathN.Dist(enemy.Pos, s.Pos)).First();
        //Дистанция до цели.
        var dist = MathN.Dist(enemy.Pos, target.Pos);
        //Если в радиусе атаки и кулдаун ноль, атакуем.
        if (dist <= _cfg.AttackRange && enemy.AttackCooldownTurns == 0)
        {
            DealDamage(enemy, target, dmg: Rand(1.6, 3.2));
            return;
        }
        //Иначе преследуем цель.
        enemy.Destination = target.Pos;
        //Если цель игрок, помечаем преследование, чтобы враг мог прыгнуть вместе с игроком.
        enemy.LastChaseTargetId = (target.Faction == Faction.Player) ? Player.Id : null;
    }

    //ИИ союзника: выбирает ближайшего врага и атакует/преследует.
    private void AllyAI(Ship ally)
    {
        //Ищем ближайшего врага.
        var target = Ships.Where(s => s.IsAlive && s.Faction == Faction.Enemy).OrderBy(s => MathN.Dist(ally.Pos, s.Pos)).FirstOrDefault();
        //Если врагов нет, выходим.
        if (target == null) return;
        //Дистанция до цели.
        var dist = MathN.Dist(ally.Pos, target.Pos);
        //Если в радиусе атаки и кулдаун ноль, атакуем.
        if (dist <= _cfg.AttackRange && ally.AttackCooldownTurns == 0)
        {
            DealDamage(ally, target, dmg: Rand(1.4, 2.8));
            return;
        }
        //Иначе преследуем врага.
        ally.Destination = target.Pos;
    }

    //Спавн волны: размер зависит от "силы" сторон и лимитов.
    private void SpawnByStrength()
    {
        //Считаем всех живых в текущей системе.
        int aliveTotal = Ships.Count(s => s.IsAlive);
        //Если достигли лимита, спавн пропускаем.
        if (aliveTotal >= _cfg.MaxTotal)
        {
            Log($"Спавн пропущен: лимит {_cfg.MaxTotal} кораблей.");
            return;
        }
        //Количество врагов и союзников.
        int enemiesCount = Ships.Count(s => s.IsAlive && s.Faction == Faction.Enemy);
        int alliesCount = Ships.Count(s => s.IsAlive && s.Faction == Faction.Ally);
        //Сила врагов и союзников: суммарное HP.
        double enemyPower = Ships.Where(s => s.IsAlive && s.Faction == Faction.Enemy).Sum(s => s.Hp);
        double allyPower = Ships.Where(s => s.IsAlive && s.Faction == Faction.Ally).Sum(s => s.Hp);
        //Сила игрока: HP, если жив.
        double playerPower = Player.IsAlive ? Player.Hp : 0;
        //Сила "добра": союзники + игрок.
        double goodPower = allyPower + playerPower;
        //Разница сил: положительная значит "враги сильнее".
        double diff = enemyPower - goodPower;
        //Базовый размер волны: специально большой для стресс-теста.
        int baseWave = _rng.Next(3, 10);
        //Бонус к волне, если враги сильно доминируют.
        int bonus = 0;
        if (diff > 10) bonus = 2;
        if (diff > 25) bonus = 4;
        if (diff > 45) bonus = 6;
        //Итоговый размер волны: ограничиваем сверху.
        int wave = Math.Min(baseWave + bonus, 20);
        //Ограничиваем волной оставшееся место до MaxTotal.
        wave = Math.Min(wave, _cfg.MaxTotal - aliveTotal);
        //Сколько реально заспавнили.
        int spawned = 0;
        //Спавним корабли по одному, выбирая фракцию "умно".
        for (int i = 0; i < wave; i++)
        {
            //Выбираем фракцию с учётом баланса.
            var faction = RollFactionSmart(enemyPower, goodPower, enemiesCount, alliesCount);
            //Пересчитываем количества, потому что список мог измениться.
            enemiesCount = Ships.Count(s => s.IsAlive && s.Faction == Faction.Enemy);
            alliesCount = Ships.Count(s => s.IsAlive && s.Faction == Faction.Ally);
            //Если упёрлись в лимит фракции, переключаемся на другую.
            if (faction == Faction.Enemy && enemiesCount >= _cfg.MaxEnemies) faction = Faction.Ally;
            if (faction == Faction.Ally && alliesCount >= _cfg.MaxAllies) faction = Faction.Enemy;
            //Пересчитываем снова, чтобы корректно проверить break.
            enemiesCount = Ships.Count(s => s.IsAlive && s.Faction == Faction.Enemy);
            alliesCount = Ships.Count(s => s.IsAlive && s.Faction == Faction.Ally);
            //Если обе стороны упёрлись, прекращаем спавн.
            if (faction == Faction.Enemy && enemiesCount >= _cfg.MaxEnemies) break;
            if (faction == Faction.Ally && alliesCount >= _cfg.MaxAllies) break;
            //Создаём корабль.
            var ship = MakeRandomShip(faction);
            //Слегка отталкиваем от соседей, чтобы не появлялся прямо в куче.
            ship.Pos = NudgeAwayFromNeighbors(ship.Pos, radius: _cfg.SeparationRadius, step: _cfg.SeparationStrength * 1.5);
            //Добавляем в список текущей системы.
            Ships.Add(ship);
            spawned++;
        }
        //Если что-то заспавнили, пишем лог.
        if (spawned > 0)
        {
            Log($"Спавн: +{spawned} | враги:{Ships.Count(s => s.IsAlive && s.Faction == Faction.Enemy)} союзники:{Ships.Count(s => s.IsAlive && s.Faction == Faction.Ally)}");
        }
    }

    //Выбор фракции для спавна: пытаемся балансировать силы и количество.
    private Faction RollFactionSmart(double enemyPower, double goodPower, int enemiesCount, int alliesCount)
    {
        //Разница сил: положительная значит "враги сильнее".
        double diff = enemyPower - goodPower;
        //Разница количества: положительная значит "врагов больше".
        int countDiff = enemiesCount - alliesCount;
        //Базовая вероятность врага.
        double pEnemy = 0.55;
        //Если враги сильнее, уменьшаем шанс врага.
        pEnemy -= Clamp(diff / 120.0, -0.25, 0.25);
        //Если врагов больше, уменьшаем шанс врага.
        pEnemy -= Clamp(countDiff / 40.0, -0.20, 0.20);
        //Ограничиваем вероятность.
        pEnemy = Clamp(pEnemy, 0.15, 0.85);
        //Кидаем монетку.
        return _rng.NextDouble() < pEnemy ? Faction.Enemy : Faction.Ally;
    }

    //Разделение: раздвигаем летящие корабли, чтобы они не слипались.
    private void ApplySeparation()
    {
        //Берём всех живых летящих кораблей.
        var movers = Ships.Where(s => s.IsAlive && s.State == ShipState.Flying).ToList();
        //Активный радиус: меньше базового, чтобы не толкать всех подряд.
        double activeRadius = _cfg.SeparationRadius * _cfg.SeparationActiveRadiusFactor;
        //Для каждого корабля считаем суммарный вектор отталкивания.
        foreach (var s in movers)
        {
            double pushX = 0, pushY = 0;
            int neighbors = 0;
            //Смотрим соседей.
            foreach (var other in movers)
            {
                if (other == s) continue;
                var dx = s.Pos.X - other.Pos.X;
                var dy = s.Pos.Y - other.Pos.Y;
                var dist2 = dx * dx + dy * dy;
                //Если почти совпали, пропускаем, чтобы не делить на ноль.
                if (dist2 < 1e-9) continue;
                var dist = Math.Sqrt(dist2);
                //Если далеко, не учитываем.
                if (dist > activeRadius) continue;
                //Вес: чем ближе, тем сильнее толкаем.
                var weight = (activeRadius - dist) / activeRadius;
                pushX += (dx / dist) * weight;
                pushY += (dy / dist) * weight;
                neighbors++;
            }
            //Если соседей нет, не двигаем.
            if (neighbors == 0) continue;
            //Нормализуем вектор отталкивания.
            var len = Math.Sqrt(pushX * pushX + pushY * pushY);
            if (len < 1e-9) continue;
            pushX /= len;
            pushY /= len;
            //Шаг: если корабль стоит без destination, толкаем слабее.
            double step = s.Destination is null ? _cfg.SeparationStrength * 0.6 : _cfg.SeparationStrength;
            //Применяем отталкивание и зажимаем в 0..1.
            s.Pos = new PointN(MathN.Clamp01(s.Pos.X + pushX * step), MathN.Clamp01(s.Pos.Y + pushY * step));
        }
    }

    //Лёгкое отталкивание точки спавна от соседей: чтобы новый корабль не появлялся впритык.
    private PointN NudgeAwayFromNeighbors(PointN pos, double radius, double step)
    {
        //Текущая точка, которую будем "подталкивать".
        var p = pos;
        //Делаем несколько итераций, чтобы чуть-чуть разойтись.
        for (int k = 0; k < 3; k++)
        {
            double pushX = 0, pushY = 0;
            int neighbors = 0;
            //Смотрим всех живых кораблей.
            foreach (var other in Ships.Where(s => s.IsAlive))
            {
                var dx = p.X - other.Pos.X;
                var dy = p.Y - other.Pos.Y;
                var dist2 = dx * dx + dy * dy;
                if (dist2 < 1e-9) continue;
                var dist = Math.Sqrt(dist2);
                if (dist > radius) continue;
                var weight = (radius - dist) / radius;
                pushX += (dx / dist) * weight;
                pushY += (dy / dist) * weight;
                neighbors++;
            }
            //Если соседей нет, выходим.
            if (neighbors == 0) break;
            //Нормализуем вектор.
            var len = Math.Sqrt(pushX * pushX + pushY * pushY);
            if (len < 1e-9) break;
            pushX /= len;
            pushY /= len;
            //Сдвигаем точку и зажимаем.
            p = new PointN(MathN.Clamp01(p.X + pushX * step), MathN.Clamp01(p.Y + pushY * step));
        }
        return p;
    }

    //Нанести урон: учитываем кулдаун атакующего, уменьшаем HP жертвы, начисляем очки за врага.
    private void DealDamage(Ship attacker, Ship victim, double dmg)
    {
        //Если кто-то уже мёртв, ничего не делаем.
        if (!attacker.IsAlive || !victim.IsAlive) return;
        //Если атакующий на кулдауне, ничего не делаем.
        if (attacker.AttackCooldownTurns > 0) return;
        //Ставим кулдаун атакующему.
        attacker.AttackCooldownTurns = _cfg.AttackCooldownDefault;
        //Снимаем HP жертве.
        victim.Hp -= dmg;
        //Логи урона не пишем, чтобы не засорять.
        if (victim.Hp <= 0)
        {
            //Фиксируем смерть.
            victim.Hp = 0;
            Log($"{victim.Name} уничтожен.");
            //Очки даём только если игрок убил врага.
            if (attacker.Faction == Faction.Player && victim.Faction == Faction.Enemy)
            {
                Score += 10;
                Log($"+10 очков. Счёт: {Score}");
            }
        }
    }

    //Создать случайный корабль заданной фракции: параметры зависят от фракции.
    private Ship MakeRandomShip(Faction faction)
    {
        //HP и скорость зависят от фракции.
        var (hp, speed) = faction switch
        {
            Faction.Enemy => (Rand(8, 16), Rand(0.045, 0.070)),
            Faction.Ally => (Rand(9, 17), Rand(0.050, 0.075)),
            _ => (10.0, 0.06)
        };
        //Имя по фракции.
        var name = faction switch
        {
            Faction.Enemy => "Враг",
            Faction.Ally => "Союзник",
            _ => "Ship"
        };
        //Создаём корабль.
        return new Ship
        {
            Name = name,
            Faction = faction,
            Pos = RandEdgeSpawn(),
            Hp = hp,
            MaxHp = hp,
            Speed = speed,
            State = ShipState.Flying,
            AttackCooldownTurns = _rng.Next(0, 2),
            LastChaseTargetId = null
        };
    }

    //Случайная точка спавна по краям карты: чтобы корабли "влетали" в сцену.
    private PointN RandEdgeSpawn()
    {
        //Сторона: 0-лево, 1-право, 2-верх, 3-низ.
        int side = _rng.Next(4);
        //Параметр вдоль стороны.
        double t = _rng.NextDouble();
        //Отступ от края, чтобы не появляться ровно на границе.
        double inset = Rand(0.02, 0.06);
        //Дрожание, чтобы не было идеальной линии.
        double jitter = Rand(-0.02, 0.02);
        //Возвращаем точку по выбранной стороне.
        return side switch
        {
            0 => new PointN(inset, MathN.Clamp01(t + jitter)),
            1 => new PointN(1.0 - inset, MathN.Clamp01(t + jitter)),
            2 => new PointN(MathN.Clamp01(t + jitter), inset),
            _ => new PointN(MathN.Clamp01(t + jitter), 1.0 - inset),
        };
    }

    //Сделать снапшот и слить логи: UI и сеть получают только новые строки.
    public GameSnapshot MakeSnapshotAndDrainLogs()
    {
        //Собираем снапшоты всех живых кораблей.
        var ships = Ships.Where(s => s.IsAlive)
            .Select(s => new ShipSnapshot(s.Id, s.Name, s.Faction, s.State, s.Pos.X, s.Pos.Y, s.Hp, s.MaxHp, s.IsAlive))
            .ToList();
        //Копируем логи и очищаем буфер.
        var logs = _logBuffer.ToList();
        _logBuffer.Clear();
        //Есть ли destination у игрока.
        bool hasDst = Player.Destination is PointN;
        //Координаты destination, если есть.
        double dstX = hasDst ? Player.Destination!.Value.X : 0;
        double dstY = hasDst ? Player.Destination!.Value.Y : 0;
        //Формируем снапшот.
        return new GameSnapshot(
            CurrentSystemIndex,
            CurrentSystem.Name,
            CurrentSystem.Planet.Name,
            CurrentSystem.Planet.Pos.X,
            CurrentSystem.Planet.Pos.Y,
            Score,
            Turn,
            IsGameOver,
            Player.Id,
            Player.Hp,
            Player.MaxHp,
            Player.State,
            Player.AttackCooldownTurns,
            Player.Pos.X,
            Player.Pos.Y,
            hasDst,
            dstX,
            dstY,
            SelectedTargetId,
            ships,
            logs
        );
    }

    //Случайное целое в диапазоне [a, bExclusive).
    private int RandInt(int a, int bExclusive) => _rng.Next(a, bExclusive);
    //Случайное вещественное в диапазоне [a, b).
    private double Rand(double a, double b) => a + _rng.NextDouble() * (b - a);
    //Зажим значения в диапазон [a, b].
    private static double Clamp(double x, double a, double b) => x < a ? a : (x > b ? b : x);
    //Добавить строку в буфер логов.
    private void Log(string msg) => _logBuffer.Add(msg);
}