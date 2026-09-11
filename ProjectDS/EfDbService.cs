//Сервис работы с БД через Entity Framework Core: создание сессии и вставка снапшотов.
using Microsoft.EntityFrameworkCore;
using ProjectDS.DbEf;
using ProjectDS.DbEf.Models;

namespace ProjectDS;

public sealed class EfDbService
{
    //Фабрика контекста EF: создаёт ProjectDsContext с нужной строкой подключения.
    private readonly ProjectDsContextFactory _factory = new();

    //Создать новую игровую сессию: игрок, сессия, привязка систем по индексам.
    public async Task<long> StartNewSessionAsync(string playerName, IReadOnlyList<GameStarSystem> systems)
    {
        //Создаём контекст EF.
        await using var db = _factory.Create();
        //Игрок: ищем по имени, берём первого по PlayerId для стабильности.
        var player = await db.Players.OrderBy(p => p.PlayerId).FirstOrDefaultAsync(p => p.Name == playerName);
        //Если игрока нет, создаём.
        if (player == null)
        {
            player = new Player { Name = playerName };
            db.Players.Add(player);
            await db.SaveChangesAsync();
        }
        //Сессия: создаём запись и сохраняем, чтобы получить SessionId.
        var session = new GameSession { PlayerId = player.PlayerId };
        db.GameSessions.Add(session);
        await db.SaveChangesAsync();
        //Связи SessionSystems: для каждой игровой системы ищем строку в справочнике StarSystems.
        for (int i = 0; i < systems.Count; i++)
        {
            //Игровая система из модели игры.
            var sys = systems[i];
            //Строка справочника StarSystems: должна существовать, иначе это ошибка данных.
            var sysRow = await db.StarSystems.FirstAsync(s => s.Name == sys.Name && s.PlanetName == sys.Planet.Name);
            //Добавляем связь сессии и системы по индексу.
            db.SessionSystems.Add(new SessionSystem
            {
                SessionId = session.SessionId,
                SystemIndex = i,
                SystemId = sysRow.SystemId
            });
        }
        //Сохраняем связи.
        await db.SaveChangesAsync();
        return session.SessionId;
    }

    //Вставить снапшот: Snapshots, SnapshotShips, SessionEvents.
    public async Task InsertSnapshotAsync(long sessionId, GameSnapshot snap)
    {
        //Создаём контекст EF.
        await using var db = _factory.Create();
        //Создаём строку снапшота.
        var row = new Snapshot
        {
            SessionId = sessionId,
            Turn = snap.Turn,
            CurrentSystemIndex = snap.CurrentSystemIndex,
            Score = snap.Score,
            PlayerIdGuid = snap.PlayerId,
            PlayerHp = snap.PlayerHp,
            PlayerMaxHp = snap.PlayerMaxHp,
            PlayerState = (byte)snap.PlayerState,
            PlayerCooldown = snap.PlayerCooldown,
            PlayerX = snap.PlayerX,
            PlayerY = snap.PlayerY,
            SelectedTargetId = snap.SelectedTargetId
        };
        //Добавляем снапшот и сохраняем, чтобы получить SnapshotId.
        db.Snapshots.Add(row);
        await db.SaveChangesAsync();
        //Добавляем корабли снапшота.
        foreach (var s in snap.Ships)
        {
            db.SnapshotShips.Add(new SnapshotShip
            {
                SnapshotId = row.SnapshotId,
                ShipId = s.Id,
                Name = s.Name,
                Faction = (byte)s.Faction,
                State = (byte)s.State,
                X = s.X,
                Y = s.Y,
                Hp = s.Hp,
                MaxHp = s.MaxHp
            });
        }
        //Добавляем события лога, если они есть.
        if (snap.Logs is { Count: > 0 })
        {
            foreach (var line in snap.Logs)
            {
                db.SessionEvents.Add(new SessionEvent
                {
                    SessionId = sessionId,
                    Turn = snap.Turn,
                    Type = "log",
                    Message = line
                });
            }
        }
        //Сохраняем корабли и события.
        await db.SaveChangesAsync();
        //Если EF опять решит "я не хочу", то █ ███████ ██ █████!
    }
}
