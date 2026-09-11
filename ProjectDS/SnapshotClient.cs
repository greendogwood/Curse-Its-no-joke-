//Клиент снапшотов: подключается к серверу, читает NDJSON и поднимает события снапшотов/инфо/ошибок.
using System.Net.Sockets;
using System.Text.Json;

namespace ProjectDS.Net;

public sealed class SnapshotClient : IDisposable
{
    //TCP-клиент: держит соединение с сервером.
    private TcpClient? _tcp;
    //Источник отмены: останавливает приёмный цикл.
    private CancellationTokenSource? _cts;
    //Задача приёма: читает строки из сети и парсит сообщения.
    private Task? _rxTask;

    //Событие снапшота: вызывается из фонового потока.
    public event Action<GameSnapshot>? OnSnapshot;
    //Событие инфо: текстовые сообщения от сервера.
    public event Action<string>? OnInfo;
    //Событие ошибки: проблемы сети/парсинга/соединения.
    public event Action<string>? OnError;

    //Флаг подключения: true, если TcpClient считает себя подключённым.
    public bool IsConnected => _tcp?.Connected == true;

    //Подключиться к серверу: создаём TcpClient, стартуем приёмный цикл, отправляем hello.
    public async Task ConnectAsync(string host, int port)
    {
        //Если уже подключены (или считаем, что подключены), повторно не лезем.
        if (_tcp != null) return;
        //Создаём токен отмены для приёмного цикла.
        _cts = new CancellationTokenSource();
        //Создаём TCP-клиент.
        _tcp = new TcpClient();
        //Подключаемся к серверу.
        await _tcp.ConnectAsync(host, port);
        //Стартуем приёмный цикл в фоне.
        _rxTask = Task.Run(() => RxLoopAsync(_cts.Token));
        //Отправляем hello: сервер может игнорировать, но пусть знает, что мы живые.
        try
        {
            await Ndjson.WriteLineAsync(_tcp.GetStream(), new NetHello(), _cts.Token);
        }
        catch
        {
            //Если hello не ушёл, не валим клиента: главное, чтобы снапшоты приходили.
        }
    }

    //Приёмный цикл: читаем строки NDJSON, парсим JSON и вызываем события.
    private async Task RxLoopAsync(CancellationToken ct)
    {
        try
        {
            //Берём поток из TCP-клиента.
            var stream = _tcp!.GetStream();
            //Читаем строки до отмены или разрыва соединения.
            await foreach (var line in Ndjson.ReadLinesAsync(stream, ct))
            {
                try
                {
                    //Парсим JSON, чтобы понять тип сообщения.
                    using var doc = JsonDocument.Parse(line);
                    if (!doc.RootElement.TryGetProperty("type", out var typeEl)) continue;
                    var type = typeEl.GetString();
                    //Снапшот: десериализуем и отдаём наружу.
                    if (type == "snapshot")
                    {
                        var msg = JsonSerializer.Deserialize<NetSnapshotMsg>(line, Ndjson.JsonOpts);
                        if (msg?.Snapshot != null) OnSnapshot?.Invoke(msg.Snapshot);
                    }
                    //Инфо: десериализуем и отдаём наружу.
                    else if (type == "info")
                    {
                        var info = JsonSerializer.Deserialize<NetInfo>(line, Ndjson.JsonOpts);
                        if (info != null) OnInfo?.Invoke(info.Message);
                    }
                }
                catch
                {
                    //Плохая строка: игнорируем эту ошибку природы.
                }
            }
        }
        catch (OperationCanceledException)
        {
            //Отмена: нормальный выход.
        }
        catch (Exception ex)
        {
            //Любая другая ошибка: сообщаем наружу.
            OnError?.Invoke(ex.Message);
        }
    }

    //Отключиться: отменяем приём, закрываем сокет, ждём завершения задачи.
    public async Task DisconnectAsync()
    {
        //Сохраняем ссылки локально, чтобы не словить гонку.
        var cts = _cts;
        var task = _rxTask;
        //Сбрасываем поля: считаем, что клиент отключён.
        _cts = null;
        _rxTask = null;
        //Отменяем приёмный цикл.
        try { cts?.Cancel(); } catch { }
        //Закрываем TCP-клиент.
        try { _tcp?.Close(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _tcp = null;
        //Ждём завершения приёмной задачи.
        if (task != null)
        {
            try { await task; } catch { }
        }
    }

    //Dispose: синхронно отключаемся.
    public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
}
