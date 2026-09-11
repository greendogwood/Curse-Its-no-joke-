//Сервер снапшотов: принимает TCP-клиентов и рассылает им снапшоты в формате NDJSON.
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace ProjectDS.Net;

public sealed class SnapshotServer : IDisposable
{
    //Порт сервера.
    private readonly int _port;
    //TCP-листенер: принимает подключения.
    private TcpListener? _listener;
    //Источник отмены: останавливает accept-цикл и клиентские циклы.
    private CancellationTokenSource? _cts;
    //Задача accept-цикла.
    private Task? _acceptLoop;
    //Список клиентов: потокобезопасный словарь по id.
    private readonly ConcurrentDictionary<int, ClientConn> _clients = new();
    //Счётчик id клиентов.
    private int _nextClientId = 1;

    //Конструктор: сохраняем порт.
    public SnapshotServer(int port = 1234)
    {
        _port = port;
    }

    //Флаг работы: true, если listener создан.
    public bool IsRunning => _listener != null;

    //Запуск сервера: поднимаем listener и accept-цикл.
    public void Start()
    {
        //Если уже запущен, повторно не стартуем.
        if (_listener != null) return;
        //Создаём токен отмены.
        _cts = new CancellationTokenSource();
        try
        {
            //Слушаем только localhost: это локальный зритель.
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            //Стартуем accept-цикл в фоне.
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            //Если сервер не поднялся, игра не должна падать из-за этой [вырезано цензурой].
            try { _listener?.Stop(); } catch { }
            _listener = null;
            try { _cts.Cancel(); } catch { }
            _cts = null;
            System.Diagnostics.Debug.WriteLine("SnapshotServer.Start failed: " + ex.Message);
        }
    }

    //Accept-цикл: принимает клиентов и запускает их read-циклы.
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        //Крутимся, пока не отменили.
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                //Ждём подключения.
                tcp = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                //Отмена: выходим.
                break;
            }
            catch
            {
                //Любая другая ошибка: продолжаем, если не отменили.
                if (ct.IsCancellationRequested) break;
                continue;
            }
            //Отключаем Nagle: меньше задержка на мелких сообщениях.
            tcp.NoDelay = true;
            //Выдаём id клиенту.
            var id = Interlocked.Increment(ref _nextClientId);
            //Создаём запись клиента.
            var conn = new ClientConn(id, tcp);
            //Добавляем в словарь.
            _clients[id] = conn;
            //Запускаем read-цикл клиента в фоне.
            _ = Task.Run(() => ClientReadLoopAsync(conn, ct));
        }
    }

    //Read-цикл клиента: читает входящие строки (hello) и держит соединение живым.
    private async Task ClientReadLoopAsync(ClientConn conn, CancellationToken serverCt)
    {
        //Связываем токены: если сервер остановился, клиент тоже должен остановиться.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        var ct = linked.Token;
        try
        {
            //Берём поток клиента.
            var stream = conn.Tcp.GetStream();
            //Отправляем инфо о подключении.
            await Ndjson.WriteLineAsync(stream, new NetInfo(Message: "connected"), ct);
            //Читаем строки от клиента: пока игнорируем содержимое.
            await foreach (var line in Ndjson.ReadLinesAsync(stream, ct))
            {
                //Пока зритель ничего не управляет: просто отмечаем, что клиент жив.
                conn.LastSeenUtc = DateTime.UtcNow;
            }
        }
        catch
        {
            //Любая ошибка: считаем, что клиент отвалился.
        }
        finally
        {
            //Удаляем клиента и закрываем сокет.
            RemoveClient(conn.Id);
        }
    }

    //Рассылка снапшота всем клиентам: пишем одну строку NDJSON.
    public void BroadcastSnapshot(GameSnapshot snap)
    {
        //Сериализуем сообщение один раз, чтобы не делать это для каждого клиента.
        var msgBytes = Ndjson.SerializeLine(NetSnapshotMsg.From(snap));
        //Проходим по всем клиентам.
        foreach (var kv in _clients)
        {
            var c = kv.Value;
            //Пишем каждому клиенту в фоне, чтобы один тормоз не стопорил остальных.
            _ = Task.Run(async () =>
            {
                try
                {
                    //Пишем в поток клиента.
                    var stream = c.Tcp.GetStream();
                    await stream.WriteAsync(msgBytes, 0, msgBytes.Length);
                    await stream.FlushAsync();
                }
                catch
                {
                    //Если запись не удалась, клиент мёртв, удаляем.
                    RemoveClient(c.Id);
                }
            });
        }
    }

    //Удалить клиента: убрать из словаря и закрыть соединение.
    private void RemoveClient(int id)
    {
        //Пытаемся удалить клиента из словаря.
        if (_clients.TryRemove(id, out var c))
        {
            //Закрываем TCP-клиент.
            try { c.Tcp.Close(); } catch { }
            try { c.Tcp.Dispose(); } catch { }
        }
    }

    //Остановить сервер: отменяем циклы, закрываем listener и всех клиентов.
    public void Stop()
    {
        //Сохраняем токен отмены.
        var cts = _cts;
        //Сбрасываем поле.
        _cts = null;
        //Отменяем циклы.
        try { cts?.Cancel(); } catch { }
        //Останавливаем listener.
        try { _listener?.Stop(); } catch { }
        _listener = null;
        //Удаляем всех клиентов.
        foreach (var id in _clients.Keys.ToList()) RemoveClient(id);
    }

    //Dispose: останавливаем сервер.
    public void Dispose() => Stop();

    //Запись клиента: id, TcpClient и время последней активности.
    private sealed class ClientConn
    {
        //Id клиента на сервере.
        public int Id { get; }
        //TCP-клиент соединения.
        public TcpClient Tcp { get; }
        //Последняя активность: можно использовать для таймаутов.
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        //Конструктор записи клиента.
        public ClientConn(int id, TcpClient tcp)
        {
            Id = id;
            Tcp = tcp;
        }
    }
}
