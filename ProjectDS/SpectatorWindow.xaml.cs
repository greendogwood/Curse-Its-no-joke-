//Окно зрителя: подключается к SnapshotServer по TCP и рисует мир по приходящим снапшотам.
using ProjectDS.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ProjectDS;

public partial class SpectatorWindow : Window
{
    //Клиент снапшотов: подключение к серверу и приём NDJSON сообщений.
    private readonly SnapshotClient _client = new();
    //Очередь строк лога: ограничиваем размер, чтобы UI не пух.
    private readonly Queue<string> _logLines = new();
    //Кэш визуалов кораблей: чтобы не пересоздавать элементы каждый снапшот.
    private readonly Dictionary<Guid, (Ellipse dot, System.Windows.Controls.TextBlock label)> _shipVisuals = new();
    //Последний снапшот: нужен для перерисовки при ресайзе.
    private GameSnapshot? _last;
    //Визуал планеты: один эллипс.
    private Ellipse? _planetEllipse;
    //Подпись планеты: один текст.
    private System.Windows.Controls.TextBlock? _planetLabel;

    //Конструктор: подписываемся на события клиента и подключаемся при загрузке окна.
    public SpectatorWindow()
    {
        //Инициализация WPF-компонентов из XAML.
        InitializeComponent();
        //Инфо-сообщения от сервера: показываем в статусе.
        _client.OnInfo += msg => Dispatcher.Invoke(() => TxtInfo.Text = msg);
        //Ошибки клиента: показываем в статусе.
        _client.OnError += msg => Dispatcher.Invoke(() => TxtInfo.Text = "ERR: " + msg);
        //Снапшоты: применяем к UI.
        _client.OnSnapshot += snap => Dispatcher.Invoke(() => ApplySnapshot(snap));
        //Подключение при загрузке окна.
        Loaded += async (_, _) =>
        {
            try
            {
                //Показываем состояние подключения.
                TxtConn.Text = "connecting...";
                //Подключаемся к локальному серверу.
                await _client.ConnectAsync("127.0.0.1", 1234);
                //Если подключились, обновляем статус.
                TxtConn.Text = "connected to 127.0.0.1:1234";
            }
            catch (Exception ex)
            {
                //Если подключение не удалось, показываем ошибку.
                TxtConn.Text = "connect failed";
                TxtInfo.Text = ex.Message;
            }
        };
        //Ресайз окна: перерисовываем по последнему снапшоту.
        SizeChanged += (_, _) =>
        {
            if (_last != null) RenderFromSnapshot(_last);
        };
    }

    //Применить снапшот: обновить текстовую часть и перерисовать мир.
    private void ApplySnapshot(GameSnapshot snap)
    {
        //Сохраняем последний снапшот.
        _last = snap;
        //Добавляем строки лога из снапшота.
        foreach (var s in snap.Logs)
        {
            _logLines.Enqueue(s);
            //Ограничиваем лог, чтобы не раздувать UI.
            while (_logLines.Count > 100) _logLines.Dequeue();
        }
        //Обновляем текст лога.
        TxtLog.Text = string.Join(Environment.NewLine, _logLines);
        //Обновляем информацию о системе.
        TxtSystem.Text = $"Система: {snap.SystemName} / Планета: {snap.PlanetName}";
        //Обновляем счёт и ход.
        TxtScore.Text = $"Очки: {snap.Score} | Ход: {snap.Turn}";
        //Обновляем состояние игрока.
        TxtPlayer.Text = $"Игрок: HP {snap.PlayerHp:0.0}/{snap.PlayerMaxHp:0.0} | {snap.PlayerState}" + (snap.IsGameOver ? " | GAME OVER" : "");
        //Перерисовываем мир.
        RenderFromSnapshot(snap);
    }

    //Получить текущий размер канваса: защищаемся от нулевых размеров.
    private (double w, double h) CanvasSize()
    {
        //Ширина канваса: минимум 1.
        var w = Math.Max(1, WorldCanvas.ActualWidth);
        //Высота канваса: минимум 1.
        var h = Math.Max(1, WorldCanvas.ActualHeight);
        //Возвращаем размеры.
        return (w, h);
    }

    //Перевод нормализованных координат 0..1 в пиксели канваса.
    private Point ToPx(double nx, double ny, double w, double h) => new(nx * w, ny * h);

    //Перерисовка мира: планета и корабли.
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
        //Список живых Id: нужен, чтобы удалить визуалы отсутствующих кораблей.
        var aliveIds = new HashSet<Guid>(snap.Ships.Select(s => s.Id));
        //Находим визуалы, которых больше нет.
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
        //Рисуем все корабли из снапшота.
        foreach (var ss in snap.Ships) EnsureAndPlaceShip(ss, w, h);
        //Z-индексы: планета под кораблями.
        Panel.SetZIndex(_planetEllipse, 0);
        Panel.SetZIndex(_planetLabel, 0);
    }

    //Создать (если надо) и разместить визуал корабля: эллипс и подпись.
    private void EnsureAndPlaceShip(ShipSnapshot ss, double w, double h)
    {
        //Если визуала ещё нет, создаём.
        if (!_shipVisuals.TryGetValue(ss.Id, out var v))
        {
            var dot = new Ellipse();
            var label = new System.Windows.Controls.TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 11,
                IsHitTestVisible = false
            };
            //Сохраняем визуал в кэш.
            _shipVisuals[ss.Id] = (dot, label);
            v = (dot, label);
            //Добавляем на канвас.
            WorldCanvas.Children.Add(v.dot);
            WorldCanvas.Children.Add(v.label);
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
        //Обводка стандартная: зритель не выбирает цели.
        v.dot.Stroke = Brushes.Black;
        v.dot.StrokeThickness = 1;
        //Ставим точку по центру.
        Canvas.SetLeft(v.dot, posPx.X - size / 2);
        Canvas.SetTop(v.dot, posPx.Y - size / 2);
        //Подпись: имя и HP.
        v.label.Text = $"{ss.Name} {ss.Hp:0.0}";
        Canvas.SetLeft(v.label, posPx.X + 10);
        Canvas.SetTop(v.label, posPx.Y - 10);
    }

    //Закрытие окна: отключаемся от сервера и освобождаем ресурсы.
    protected override void OnClosed(EventArgs e)
    {
        //Отключаем клиента.
        _client.Dispose();
        //Вызываем базовую реализацию.
        base.OnClosed(e);
    }
}
