//Сетевые сообщения: простые record-структуры для NDJSON протокола "hello/info/snapshot".
namespace ProjectDS.Net;

//Сообщение приветствия от клиента: сейчас сервер его не использует, но можно расширить.
public sealed record NetHello(
    //Тип сообщения: строковый маркер для роутинга.
    string Type = "hello",
    //Имя клиента: по умолчанию "spectator".
    string ClientName = "spectator"
);

//Сообщение снапшота: содержит тип и сам GameSnapshot.
public sealed record NetSnapshotMsg(
    //Тип сообщения: должен быть "snapshot".
    string Type,
    //Снапшот игры: состояние мира на текущий тик.
    GameSnapshot Snapshot
)
{
    //Фабрика сообщения снапшота: чтобы не писать руками "snapshot".
    public static NetSnapshotMsg From(GameSnapshot snap) => new("snapshot", snap);
}

//Информационное сообщение: сервер может отправлять статусные строки.
public sealed record NetInfo(
    //Тип сообщения: должен быть "info".
    string Type = "info",
    //Текст сообщения: например "connected".
    string Message = ""
);
