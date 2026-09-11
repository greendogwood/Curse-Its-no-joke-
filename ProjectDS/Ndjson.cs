//Утилиты NDJSON: сериализация объектов в JSON-строку с переводом строки и чтение построчно из Stream.
using System.IO;
using System.Text;
using System.Text.Json;

namespace ProjectDS.Net;

public static class Ndjson
{
    //Настройки JSON: camelCase, без форматирования, чтобы сообщения были компактные.
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    //Сериализовать объект в одну строку NDJSON: JSON + "\n".
    public static byte[] SerializeLine<T>(T obj)
    {
        //Сериализуем объект в JSON.
        var json = JsonSerializer.Serialize(obj, JsonOpts);
        //Возвращаем UTF-8 байты с переводом строки.
        return Encoding.UTF8.GetBytes(json + "\n");
    }

    //Записать одну строку NDJSON в поток: пишем байты и flush.
    public static async Task WriteLineAsync<T>(Stream stream, T obj, CancellationToken ct)
    {
        //Готовим байты строки.
        var bytes = SerializeLine(obj);
        //Пишем в поток.
        await stream.WriteAsync(bytes, 0, bytes.Length, ct);
        //Флашим, чтобы сообщение ушло сразу.
        await stream.FlushAsync(ct);
    }

    //Читать NDJSON построчно из потока: возвращаем строки без пустых.
    public static async IAsyncEnumerable<string> ReadLinesAsync(Stream stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        //Reader оставляем поток открытым: закрывает его владелец (TcpClient/сервер).
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        //Читаем, пока не отменили.
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                //ReadLineAsync без ct, поэтому используем WaitAsync(ct).
                line = await reader.ReadLineAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                //Отмена: выходим.
                yield break;
            }
            //Если null, значит поток закрыт (клиент отключился).
            if (line == null) yield break;
            //Пустые строки игнорируем.
            if (string.IsNullOrWhiteSpace(line)) continue;
            //Отдаём строку наружу.
            yield return line;
            //Ну и да. Насчёт yield. Удобная вещь. Возвращение элементов по очереди, без создания промежуточных коллекций в памяти
        }
    }
}
