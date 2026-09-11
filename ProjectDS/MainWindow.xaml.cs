//Главное окно игры: UI, управление раннером, лог, сохранение в БД и раздача снапшотов зрителям.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ProjectDS.Net;

namespace ProjectDS;

public partial class MainWindow : Window
{
    //Конфиг игры: интервалы тиков, лимиты спавна, дальность атаки, лимит логов и т.д.
    private readonly GameConfig _cfg = new();
    //Игровая модель: системы, корабли, логика тиков, команды.
    private readonly Game _game;
    //Раннер: фоновой цикл тиков, очередь команд, публикация снапшотов.
    private readonly GameRunner _runner;
    //Очередь строк лога для UI: ограничиваем размер, чтобы не раздувать память и текстблок.
    private readonly Queue<string> _logLines = new();
    //Кэш визуализаций кораблей: чтобы не пересоздавать эллипсы и подписи каждый тик.
    private readonly Dictionary<Guid, (Ellipse dot, System.Windows.Controls.TextBlock label)> _shipVisuals = new();
    //Последний снапшот: нужен для перерисовки при ресайзе и для хоткеев.
    private GameSnapshot? _last;
    //Визуал планеты: один эллипс на систему.
    private Ellipse? _planetEllipse;
    //Подпись планеты: один текст на систему.
    private System.Windows.Controls.TextBlock? _planetLabel;
    //Линия до точки назначения игрока: показываем, когда есть destination.
    private Line? _dstLine;
    //Флаг меню: когда меню открыто, игра заблокирована и хоткеи не работают.
    private bool _menuOpen = true;
    //Сервер снапшотов: раздаёт зрителям состояние игры по TCP.
    private readonly SnapshotServer _snapServer = new(port: 1234);
    //Сервис БД через EF: создаёт сессию и пишет снапшоты.
    private readonly EfDbService _db = new();
    //Id текущей игровой сессии в БД: 0 значит "не создано/не готово".
    private long _sessionId = 0;
    //Последний ход, который мы сохранили: защита от повторной записи одного и того же тика.
    private int _lastSavedTurn = -1;
    //Флаг готовности БД: пока false, снапшоты не пишем.
    private volatile bool _dbReady = false;

    //Конструктор окна: поднимаем сервер зрителей, создаём игру, подписываемся на снапшоты, стартуем БД-сессию после загрузки.
    public MainWindow()
    {
        //Инициализация WPF-компонентов из XAML.
        InitializeComponent();
        //Запускаем сервер зрителей сразу: если не взлетит, игра всё равно должна жить.
        _snapServer.Start();
        //Создаём игру и стартовое состояние.
        _game = new Game(_cfg);
        _game.Init();
        //Создаём раннер: он будет тикать игру и публиковать снапшоты.
        _runner = new GameRunner(_game, _cfg);
        //Подписка на снапшоты раннера: приходит из фонового потока.
        _runner.OnSnapshot += snap =>
        {
            //Раздаём снапшот зрителям: это можно делать из фонового потока.
            _snapServer.BroadcastSnapshot(snap);
            //Сохранение в БД: раз в 10 ходов, только если БД готова и сессия создана.
            if (_dbReady && _sessionId != 0 && !snap.IsGameOver && snap.Turn % 10 == 0 && snap.Turn != _lastSavedTurn)
            {
                //Запоминаем ход, чтобы не сохранить его дважды при повторной публикации снапшота.
                _lastSavedTurn = snap.Turn;
                //Пишем в БД в фоне: UI не должен зависать из-за БД.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        //Сохраняем снапшот: корабли, события, состояние игрока.
                        await _db.InsertSnapshotAsync(_sessionId, snap);
                    }
                    catch (Exception ex)
                    {
                        //И да. Если оно снова отвалится, то я найду и убью всю её семью, родственников, собак, кошек, хомячков и тараканов!
                        //Пишем в лог.
                        Dispatcher.Invoke(() => _logLines.Enqueue("DB ERR: " + ex.Message));
                    }
                });
            }
            //UI обновляем только через Dispatcher: снапшот приходит из фонового потока.
            Dispatcher.Invoke(() => ApplySnapshot(snap));
        };
        //Первичная отрисовка: берём снапшот сразу после Init().
        ApplySnapshot(_game.MakeSnapshotAndDrainLogs());
        //Стартуем с меню: игра заблокирована, пока пользователь не нажмёт "Продолжить/Новая игра".
        SetMenuOpen(true);
        //Создаём БД-сессию после загрузки окна: так меньше шансов словить проблемы с жизненным циклом WPF.
        Loaded += async (_, _) => { await StartDbSessionSafeAsync(); };
    }

    //Создание новой БД-сессии безопасно: ошибки ловим и не валим игру.
    private async Task StartDbSessionSafeAsync()
    {
        try
        {
            //Пока создаём сессию, считаем БД неготовой.
            _dbReady = false;
            //Создаём новую сессию и сохраняем список систем этой игры.
            _sessionId = await _db.StartNewSessionAsync("Крис", _game.Systems);
            //Сбрасываем маркер сохранённого хода.
            _lastSavedTurn = -1;
            //Теперь можно писать снапшоты.
            _dbReady = true;
            //Пишем в лог UI, что сессия поднялась.
            _logLines.Enqueue($"DB: session {_sessionId} started");
            TxtLog.Text = string.Join(Environment.NewLine, _logLines);
        }
        catch (Exception ex)
        {
            //Если сессия не создалась, просто отключаем запись снапшотов.
            _dbReady = false;
            _sessionId = 0;
            _logLines.Enqueue("DB ERR (session): " + ex.Message);
            TxtLog.Text = string.Join(Environment.NewLine, _logLines);
        }
    }

    //Открыть/закрыть меню: блокируем игровой UI и кнопки, чтобы не ломать состояние.
    private void SetMenuOpen(bool open)
    {
        //Запоминаем состояние меню.
        _menuOpen = open;
        //Показываем/скрываем оверлей меню.
        MainMenuOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        //Блокируем игровой UI, когда меню открыто.
        GameRoot.IsEnabled = !open;
        //Кнопки управления раннером: выключаем в меню и при game over.
        BtnRun.IsEnabled = !open && (_last?.IsGameOver != true);
        BtnPause.IsEnabled = !open && (_last?.IsGameOver != true);
        BtnStep.IsEnabled = !open && (_last?.IsGameOver != true);
        //Рестарт разрешаем даже при game over, но не в меню.
        BtnRestart.IsEnabled = !open;
        //Атака запрещена в меню и при game over.
        BtnAttack.IsEnabled = !open && (_last?.IsGameOver != true);
        //Кнопка "Продолжить" в меню: запрещаем, если игра уже окончена.
        BtnMenuContinue.IsEnabled = !(_last?.IsGameOver == true);
    }

    //Применение снапшота к UI: обновляем текст, лог, кнопки и перерисовываем мир.
    private void ApplySnapshot(GameSnapshot snap)
    {
        //Сохраняем последний снапшот для хоткеев и перерисовки.
        _last = snap;
        //Добавляем новые строки лога из снапшота.
        foreach (var s in snap.Logs)
        {
            _logLines.Enqueue(s);
            //Ограничиваем лог по конфигу, чтобы не раздувать UI.
            while (_logLines.Count > _cfg.MaxLogLines) _logLines.Dequeue();
        }
        //Обновляем текст лога.
        TxtLog.Text = string.Join(Environment.NewLine, _logLines);
        //Текущая система и планета.
        TxtSystem.Text = $"Система: {snap.SystemName} / Планета: {snap.PlanetName}";
        //Очки и номер хода.
        TxtScore.Text = $"Очки: {snap.Score} | Ход: {snap.Turn}";
        //Расстояние до планеты: нужно для подсказки посадки.
        double dx = snap.PlayerX - snap.PlanetX;
        double dy = snap.PlayerY - snap.PlanetY;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        //Подсказка посадки: можно ли сесть или игрок уже на планете.
        string landHint = snap.PlayerState == ShipState.Flying ? (dist <= 0.06 ? " | LAND OK" : $" | LAND: FAR ({dist:0.00})") : " | ON PLANET";
        //Строка состояния игрока: HP, состояние, game over, кулдаун, подсказка посадки.
        TxtPlayer.Text = $"Игрок: HP {snap.PlayerHp:0.0}/{snap.PlayerMaxHp:0.0} | {snap.PlayerState}" + (snap.IsGameOver ? " | GAME OVER" : "") + $" | CD: {snap.PlayerCooldown}" + landHint;
        //Показываем выбранную цель (Guid) или "-".
        TxtTarget.Text = $"Цель: {(snap.SelectedTargetId == null ? "-" : snap.SelectedTargetId.ToString())}";
        //Если игра окончена, блокируем управление.
        if (snap.IsGameOver)
        {
            BtnRun.IsEnabled = false;
            BtnStep.IsEnabled = false;
            BtnPause.IsEnabled = false;
            BtnAttack.IsEnabled = false;
        }
        //Если меню открыто, обновляем его состояние кнопок (например после game over).
        if (_menuOpen) SetMenuOpen(true);
        //Перерисовываем мир по снапшоту.
        RenderFromSnapshot(snap);
    }

    //Получить текущий размер канваса: защищаемся от нулевых размеров.
    private (double w, double h) CanvasSize()
    {
        //Ширина канваса: минимум 1, чтобы не делить на 0.
        var w = Math.Max(1, WorldCanvas.ActualWidth);
        //Высота канваса: минимум 1, чтобы не делить на 0.
        var h = Math.Max(1, WorldCanvas.ActualHeight);
        //Возвращаем размеры.
        return (w, h);
    }

    //Перевод нормализованных координат 0..1 в пиксели канваса.
    private Point ToPx(double nx, double ny, double w, double h) => new(nx * w, ny * h);

    //Перевод пикселей канваса в нормализованные координаты 0..1.
    private PointN ToNorm(Point p, double w, double h)
    {
        //Защита от нулевых размеров.
        if (w < 1) w = 1;
        if (h < 1) h = 1;
        //Нормализуем и зажимаем в 0..1.
        return new PointN(Clamp01(p.X / w), Clamp01(p.Y / h));
    }

    //Зажим значения в диапазон 0..1.
    private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);

    //Перерисовка мира по снапшоту: планета, линия назначения, корабли.
    private void RenderFromSnapshot(GameSnapshot snap)
    {
        //Берём размеры канваса.
        var (w, h) = CanvasSize();
        //Создаём визуал планеты один раз.
        if (_planetEllipse == null)
        {
            _planetEllipse = new Ellipse
            {
                Width = 60,
                Height = 60,
                Fill = Brushes.SteelBlue,
                Stroke = Brushes.LightBlue,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            WorldCanvas.Children.Add(_planetEllipse);
        }
        //Создаём подпись планеты один раз.
        if (_planetLabel == null)
        {
            _planetLabel = new System.Windows.Controls.TextBlock
            {
                Foreground = Brushes.White,
                IsHitTestVisible = false
            };
            WorldCanvas.Children.Add(_planetLabel);
        }
        //Позиция планеты в пикселях.
        var planetPx = ToPx(snap.PlanetX, snap.PlanetY, w, h);
        //Ставим эллипс планеты по центру.
        Canvas.SetLeft(_planetEllipse, planetPx.X - 30);
        Canvas.SetTop(_planetEllipse, planetPx.Y - 30);
        //Обновляем подпись планеты.
        _planetLabel.Text = snap.PlanetName;
        Canvas.SetLeft(_planetLabel, planetPx.X - 40);
        Canvas.SetTop(_planetLabel, planetPx.Y + 35);
        //Создаём линию назначения один раз.
        if (_dstLine == null)
        {
            _dstLine = new Line
            {
                Stroke = Brushes.LightGreen,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                Opacity = 0.8,
                IsHitTestVisible = false
            };
            WorldCanvas.Children.Add(_dstLine);
        }
        //Показываем линию только если игра не окончена и у игрока есть destination.
        bool showLine = !snap.IsGameOver && snap.PlayerHp > 0 && snap.PlayerHasDestination;
        _dstLine.Visibility = showLine ? Visibility.Visible : Visibility.Collapsed;
        //Если линию показываем, обновляем её координаты.
        if (showLine)
        {
            var ppx = ToPx(snap.PlayerX, snap.PlayerY, w, h);
            var dpx = ToPx(snap.PlayerDstX, snap.PlayerDstY, w, h);
            _dstLine.X1 = ppx.X;
            _dstLine.Y1 = ppx.Y;
            _dstLine.X2 = dpx.X;
            _dstLine.Y2 = dpx.Y;
        }
        //Список живых Id из снапшота: нужен, чтобы удалить визуалы умерших/исчезнувших.
        var aliveIds = new HashSet<Guid>(snap.Ships.Select(s => s.Id));
        //Находим визуалы, которых больше нет в снапшоте.
        var toRemove = _shipVisuals.Keys.Where(id => !aliveIds.Contains(id)).ToList();
        //Удаляем визуалы отсутствующих кораблей.
        foreach (var id in toRemove)
        {
            if (_shipVisuals.TryGetValue(id, out var vv))
            {
                WorldCanvas.Children.Remove(vv.dot);
                WorldCanvas.Children.Remove(vv.label);
            }
            _shipVisuals.Remove(id);
        }
        //Сначала рисуем всех, кроме игрока: чтобы игрок был поверх.
        foreach (var ss in snap.Ships.Where(s => s.Id != snap.PlayerId)) EnsureAndPlaceShip(ss, snap, w, h);
        //Потом рисуем игрока, если он жив.
        var playerSnap = snap.Ships.FirstOrDefault(s => s.Id == snap.PlayerId);
        if (playerSnap != null && playerSnap.IsAlive) EnsureAndPlaceShip(playerSnap, snap, w, h);
        //Z-индексы: планета под кораблями, линия назначения над планетой.
        Panel.SetZIndex(_planetEllipse, 0);
        Panel.SetZIndex(_planetLabel, 0);
        Panel.SetZIndex(_dstLine, 1);
    }

    //Создать (если надо) и разместить визуал корабля: эллипс и подпись.
    private void EnsureAndPlaceShip(ShipSnapshot ss, GameSnapshot snap, double w, double h)
    {
        //Если визуала ещё нет, создаём и подписываемся на клик (для выбора цели).
        if (!_shipVisuals.TryGetValue(ss.Id, out var v))
        {
            var dot = new Ellipse();
            var label = new System.Windows.Controls.TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 11,
                IsHitTestVisible = false
            };
            //Tag хранит Id корабля: используем в обработчике клика.
            dot.Tag = ss.Id;
            //Клик по кораблю: выбираем цель (кроме игрока).
            dot.MouseLeftButtonDown += async (sender, e) =>
            {
                //Не даём клику провалиться на канвас (иначе будет "задать курс").
                e.Handled = true;
                //Без снапшота не знаем Id игрока и состояние.
                if (_last == null) return;
                //В меню клики по миру не работают.
                if (_menuOpen) return;
                //Проверяем источник события.
                if (e.Source is not FrameworkElement fe) return;
                //Достаём Id из Tag.
                if (fe.Tag is not Guid id) return;
                //Игрока нельзя выбрать целью.
                if (id == _last.PlayerId) return;
                //Кладём команду выбора цели и сразу применяем, чтобы UI обновился без ожидания тика.
                _runner.Enqueue(new CmdSelectTarget(id));
                await _runner.PumpCommandsOnceAsync();
            };
            //Сохраняем визуал в кэш.
            _shipVisuals[ss.Id] = (dot, label);
            v = (dot, label);
            //Добавляем на канвас.
            WorldCanvas.Children.Add(v.dot);
            WorldCanvas.Children.Add(v.label);
        }
        else
        {
            //Обновляем Tag на всякий случай: если визуал переиспользуется.
            v.dot.Tag = ss.Id;
        }
        //Цвет корабля по фракции.
        var color = ss.Faction switch
        {
            Faction.Player => Brushes.LimeGreen,
            Faction.Ally => Brushes.Gold,
            _ => Brushes.OrangeRed
        };
        //Размер точки: игрок крупнее.
        var size = (ss.Faction == Faction.Player) ? 18 : 14;
        //Позиция корабля в пикселях.
        var posPx = ToPx(ss.X, ss.Y, w, h);
        //Применяем размер и цвет.
        v.dot.Width = size;
        v.dot.Height = size;
        v.dot.Fill = color;
        //Подсветка выбранной цели: белая обводка.
        bool isSelected = snap.SelectedTargetId != null && snap.SelectedTargetId.Value == ss.Id;
        v.dot.Stroke = isSelected ? Brushes.White : Brushes.Black;
        v.dot.StrokeThickness = isSelected ? 3 : 1;
        //Курсор: по игроку не кликаем, по остальным можно выбрать цель.
        v.dot.Cursor = (ss.Faction == Faction.Player) ? Cursors.Arrow : Cursors.Hand;
        //Ставим точку по центру.
        Canvas.SetLeft(v.dot, posPx.X - size / 2);
        Canvas.SetTop(v.dot, posPx.Y - size / 2);
        //Подпись: имя и HP.
        v.label.Text = $"{ss.Name} {ss.Hp:0.0}";
        Canvas.SetLeft(v.label, posPx.X + 10);
        Canvas.SetTop(v.label, posPx.Y - 10);
    }

    //Клик по миру: задаём курс игроку в точку клика.
    private async void WorldCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        //В меню мир не кликается.
        if (_menuOpen) return;
        //Фокус на канвас: чтобы хоткеи работали.
        WorldCanvas.Focus();
        //Берём размеры и позицию клика.
        var (w, h) = CanvasSize();
        var p = e.GetPosition(WorldCanvas);
        //Переводим в нормализованные координаты.
        var dest = ToNorm(p, w, h);
        //Кладём команду и применяем сразу, чтобы линия назначения появилась мгновенно.
        _runner.Enqueue(new CmdSetDestination(dest));
        await _runner.PumpCommandsOnceAsync();
    }

    //Кнопка "Запуск": стартуем фоновый цикл раннера.
    private void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        //В меню запуск запрещён.
        if (_menuOpen) return;
        //После game over запуск запрещён.
        if (_last?.IsGameOver == true) return;
        //Стартуем раннер.
        _runner.Start();
        //Пишем в лог UI.
        _logLines.Enqueue("Запуск.");
        TxtLog.Text = string.Join(Environment.NewLine, _logLines);
    }

    //Кнопка "Пауза": останавливаем раннер.
    private async void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        //В меню пауза не нужна.
        if (_menuOpen) return;
        //Останавливаем раннер корректно.
        await _runner.StopAsync();
        //Пишем в лог UI.
        _logLines.Enqueue("Пауза.");
        TxtLog.Text = string.Join(Environment.NewLine, _logLines);
    }

    //Кнопка "Шаг": один тик игры.
    private void BtnStep_Click(object sender, RoutedEventArgs e)
    {
        //В меню шаг запрещён.
        if (_menuOpen) return;
        //После game over шаг запрещён.
        if (_last?.IsGameOver == true) return;
        //Делаем один шаг раннера.
        _runner.StepOnce();
        //Пишем в лог UI.
        _logLines.Enqueue("Шаг.");
        TxtLog.Text = string.Join(Environment.NewLine, _logLines);
    }

    //Кнопка "Рестарт": сбрасываем игру, UI и создаём новую БД-сессию.
    private async void BtnRestart_Click(object sender, RoutedEventArgs e)
    {
        //В меню рестарт запрещён (рестарт делается через меню "Новая игра").
        if (_menuOpen) return;
        //Останавливаем раннер, чтобы не тикал во время сброса.
        await _runner.StopAsync();
        //Возвращаем кнопки управления.
        BtnRun.IsEnabled = true;
        BtnStep.IsEnabled = true;
        BtnPause.IsEnabled = true;
        BtnAttack.IsEnabled = true;
        //Чистим лог UI.
        _logLines.Clear();
        TxtLog.Text = "";
        //Команда рестарта: пересоздаёт состояние игры.
        _runner.Enqueue(new CmdRestart());
        //Делаем шаг, чтобы рестарт применился и снапшот ушёл в UI.
        _runner.StepOnce();
        //Создаём новую БД-сессию под новую игру.
        await StartDbSessionSafeAsync();
    }

    //Кнопка "Сесть/Взлететь": переключаем состояние посадки игрока.
    private async void BtnLandTakeoff_Click(object sender, RoutedEventArgs e)
    {
        //В меню запрещено.
        if (_menuOpen) return;
        //Кладём команду и применяем сразу.
        _runner.Enqueue(new CmdToggleLandTakeoff());
        await _runner.PumpCommandsOnceAsync();
    }

    //Кнопка "Прыжок": прыгаем в другую систему.
    private async void BtnJump_Click(object sender, RoutedEventArgs e)
    {
        //В меню запрещено.
        if (_menuOpen) return;
        //Кладём команду и применяем сразу.
        _runner.Enqueue(new CmdJump());
        await _runner.PumpCommandsOnceAsync();
    }

    //Кнопка "Атака": атакуем выбранную или ближайшую цель.
    private async void BtnAttack_Click(object sender, RoutedEventArgs e)
    {
        //В меню запрещено.
        if (_menuOpen) return;
        //Кладём команду и применяем сразу.
        _runner.Enqueue(new CmdAttack());
        await _runner.PumpCommandsOnceAsync();
    }

    //Ресайз окна: перерисовываем мир по последнему снапшоту.
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        //Если снапшота ещё нет, рисовать нечего.
        if (_last != null) RenderFromSnapshot(_last);
    }

    //Хоткеи окна: Esc меню, Space атака, WASD/стрелки шагом с тиком.
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        //Игнорируем автоповтор клавиши.
        if (e.IsRepeat) return;
        //Esc: открыть/закрыть меню.
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            //Если меню открыто, закрываем и возвращаем фокус на канвас.
            if (_menuOpen)
            {
                SetMenuOpen(false);
                WorldCanvas.Focus();
            }
            else
            {
                //Если меню закрыто, ставим игру на паузу и открываем меню.
                await _runner.StopAsync();
                SetMenuOpen(true);
            }
            return;
        }
        //Если меню открыто, остальные хоткеи не работают.
        if (_menuOpen) return;
        //Без снапшота не знаем состояние.
        if (_last == null) return;
        //После game over хоткеи не работают.
        if (_last.IsGameOver) return;
        //Space: атака и сразу тик, чтобы увидеть результат.
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            _runner.Enqueue(new CmdAttack());
            await _runner.PumpCommandsOnceAsync();
            _runner.StepOnce();
            return;
        }
        //Смещение по X/Y для WASD/стрелок.
        int dx = 0, dy = 0;
        switch (e.Key)
        {
            case Key.W:
            case Key.Up:
                dy = -1;
                break;
            case Key.S:
            case Key.Down:
                dy = +1;
                break;
            case Key.A:
            case Key.Left:
                dx = -1;
                break;
            case Key.D:
            case Key.Right:
                dx = +1;
                break;
            default:
                return;
        }
        //Мы обработали клавишу движения.
        e.Handled = true;
        //Двигаем игрока шагом и пропускаем ход (делаем тик).
        await MoveAndSkipTurnAsync(dx, dy);
    }

    //Движение шагом: задаём destination рядом и делаем тик.
    private async Task MoveAndSkipTurnAsync(int dx, int dy)
    {
        //Без снапшота не знаем позицию.
        if (_last == null) return;
        //На планете шагом не двигаемся.
        if (_last.PlayerState == ShipState.Landed) return;
        //Шаг в нормализованных координатах.
        const double step = 0.06;
        //Новая точка назначения с зажимом в 0..1.
        var nx = Clamp01(_last.PlayerX + dx * step);
        var ny = Clamp01(_last.PlayerY + dy * step);
        //Задаём курс и применяем сразу.
        _runner.Enqueue(new CmdSetDestination(new PointN(nx, ny)));
        await _runner.PumpCommandsOnceAsync();
        //Делаем тик, чтобы движение/AI/спавн отработали.
        _runner.StepOnce();
    }

    //Пункт меню "Выход": закрываем окно.
    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    //Кнопка меню "Новая игра": рестарт, новая БД-сессия, закрыть меню.
    private async void BtnMenuNewGame_Click(object sender, RoutedEventArgs e)
    {
        //Останавливаем раннер, чтобы не тикал во время сброса.
        await _runner.StopAsync();
        //Чистим лог UI.
        _logLines.Clear();
        TxtLog.Text = "";
        //Команда рестарта: пересоздаёт состояние игры.
        _runner.Enqueue(new CmdRestart());
        //Делаем шаг, чтобы рестарт применился и снапшот ушёл в UI.
        _runner.StepOnce();
        //Создаём новую БД-сессию под новую игру.
        await StartDbSessionSafeAsync();
        //Закрываем меню и возвращаем фокус на канвас.
        SetMenuOpen(false);
        WorldCanvas.Focus();
    }

    //Кнопка меню "Продолжить": закрываем меню, если игра не окончена.
    private void BtnMenuContinue_Click(object sender, RoutedEventArgs e)
    {
        //Если игра окончена, продолжать нельзя.
        if (_last?.IsGameOver == true) return;
        //Закрываем меню и возвращаем фокус на канвас.
        SetMenuOpen(false);
        WorldCanvas.Focus();
    }

    //Кнопка меню "Выход": закрываем окно.
    private void BtnMenuExit_Click(object sender, RoutedEventArgs e) => Close();

    //Кнопка "Зритель": открываем окно зрителя, которое подключается к локальному серверу снапшотов.
    private void BtnOpenSpectator_Click(object sender, RoutedEventArgs e)
    {
        //Создаём окно зрителя.
        var w = new SpectatorWindow();
        //Делаем владельцем главное окно, чтобы окна группировались.
        w.Owner = this;
        //Показываем окно.
        w.Show();
    }

    //Закрытие окна: корректно останавливаем раннер и сервер зрителей.
    protected override void OnClosed(EventArgs e)
    {
        //Останавливаем раннер и освобождаем ресурсы.
        _runner.Dispose();
        //Останавливаем сервер зрителей.
        _snapServer.Dispose();
        //Вызываем базовую реализацию.
        base.OnClosed(e);
    }
}
