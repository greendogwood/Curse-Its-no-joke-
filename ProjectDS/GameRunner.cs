//Раннер игры: принимает команды, тикает игру в фоне, публикует снапшоты и защищает состояние семафором.
using System.Collections.Concurrent;

namespace ProjectDS;

public sealed class GameRunner : IDisposable
{
    //Ссылка на игру: здесь живёт вся логика и состояние.
    private readonly Game _game;
    //Конфиг раннера: в основном нужен интервал тика.
    private readonly GameConfig _cfg;
    //Семафор-гейт: не даём одновременно тикать и применять команды, потому что состояние гонки ну вообще не нужно, мне дедлока хватило
    private readonly SemaphoreSlim _gate = new(1, 1);
    //Очередь команд: UI и хоткеи кладут сюда команды, раннер их применяет.
    private readonly ConcurrentQueue<IGameCommand> _commands = new();
    //Источник отмены фонового цикла.
    private CancellationTokenSource? _cts;
    //Задача фонового цикла.
    private Task? _loopTask;

    //Событие снапшота: подписчик обязан помнить, что это может прилететь из фонового потока.
    public event Action<GameSnapshot>? OnSnapshot;

    //Конструктор: сохраняем ссылки на игру и конфиг.
    public GameRunner(Game game, GameConfig cfg)
    {
        _game = game;
        _cfg = cfg;
    }

    //Применить команды один раз без тика: удобно для мгновенного UI-отклика.
    public async Task PumpCommandsOnceAsync()
    {
        //Берём гейт: иначе можно одновременно тикнуть и применить команду, и получится ошибка природы.
        await _gate.WaitAsync();
        try
        {
            //Сливаем команды и применяем к игре.
            bool hadCmd = DrainCommands();
            //Если были команды, публикуем снапшот, чтобы UI обновился сразу.
            if (hadCmd) PublishSnapshot();
        }
        finally
        {
            //Отпускаем гейт в любом случае.
            _gate.Release();
        }
    }

    //Положить команду в очередь: потокобезопасно.
    public void Enqueue(IGameCommand cmd) => _commands.Enqueue(cmd);

    //Запуск фонового цикла: если уже запущен, повторно не стартуем.
    public void Start()
    {
        //Если задача уже работает, ничего не делаем.
        if (_loopTask is { IsCompleted: false }) return;
        //Создаём новый токен отмены.
        _cts = new CancellationTokenSource();
        //Стартуем фоновую петлю.
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    //Остановка без ожидания: просто отменяем и забываем ссылки.
    public void Stop()
    {
        //Отменяем токен.
        _cts?.Cancel();
        //Сбрасываем ссылки: это "жёсткая" остановка, без ожидания завершения.
        _cts = null;
        _loopTask = null;
    }

    //Остановка с ожиданием: корректно ждём завершения фоновой задачи.
    public async Task StopAsync()
    {
        //Сохраняем ссылки локально, чтобы не словить гонку при параллельных вызовах.
        var cts = _cts;
        var task = _loopTask;
        //Сбрасываем поля сразу: считаем, что раннер остановлен.
        _cts = null;
        _loopTask = null;
        //Если нечего останавливать, выходим.
        if (cts == null || task == null) return;
        //Отменяем цикл.
        cts.Cancel();
        try
        {
            //Ждём завершения: если там вылетело исключение, затерпим.
            await task;
        }
        catch
        {
            //Игру не валим из-за остановки: пусть молча заткнётся и уйдёт, у меня уже нет на это никаких моральных сил.
        }
    }

    //Сделать один тик в фоне: применяем команды, публикуем снапшот, тикаем игру, публикуем снапшот.
    public void StepOnce()
    {
        //Запускаем шаг в фоне, чтобы не блокировать UI.
        Task.Run(async () =>
        {
            //Берём гейт: тик и команды должны быть атомарны относительно друг друга.
            await _gate.WaitAsync();
            try
            {
                //Сначала применяем команды.
                bool hadCmd = DrainCommands();
                //Если команды были, публикуем снапшот до тика.
                if (hadCmd) PublishSnapshot();
                //Тикаем игру.
                _game.Tick();
                //Публикуем снапшот после тика.
                PublishSnapshot();
            }
            finally
            {
                //Отпускаем гейт.
                _gate.Release();
            }
        });
    }

    //Фоновая петля: применяем команды, тикаем игру, публикуем снапшоты, ждём интервал.
    private async Task LoopAsync(CancellationToken ct)
    {
        //Крутимся, пока не отменили.
        while (!ct.IsCancellationRequested)
        {
            //Берём гейт: защищаем игру от параллельного доступа.
            await _gate.WaitAsync(ct);
            try
            {
                //Применяем команды.
                bool hadCmd = DrainCommands();
                //Если команды были, публикуем снапшот до тика.
                if (hadCmd) PublishSnapshot();
                //Если игра не окончена, делаем тик.
                if (!_game.IsGameOver) _game.Tick();
                //Публикуем снапшот после тика.
                PublishSnapshot();
            }
            finally
            {
                //Отпускаем гейт.
                _gate.Release();
            }
            try
            {
                //Ждём интервал тика.
                await Task.Delay(_cfg.TickInterval, ct);
            }
            catch (TaskCanceledException)
            {
                //Отмена: выходим из цикла.
                break;
            }
        }
    }

    //Слить очередь команд: применяем все команды по порядку поступления.
    private bool DrainCommands()
    {
        //Флаг: были ли команды.
        bool any = false;
        //Пока есть команды, вытаскиваем и применяем.
        while (_commands.TryDequeue(out var cmd))
        {
            cmd.Apply(_game);
            any = true;
        }
        return any;
    }

    //Опубликовать снапшот: создаём снапшот и вызываем событие.
    private void PublishSnapshot()
    {
        //Снапшот включает логи, которые при этом "сливаются" из буфера.
        var snap = _game.MakeSnapshotAndDrainLogs();
        //Вызываем подписчиков: они сами должны маршалить в UI-поток.
        OnSnapshot?.Invoke(snap);
    }

    //Освобождение ресурсов: останавливаем раннер.
    public void Dispose()
    {
        //Останавливаем раннер синхронно: Dispose не async, поэтому ждём через GetResult().
        StopAsync().GetAwaiter().GetResult();
    }
}
