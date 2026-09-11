//Сервис работы с БД через чистый ADO.NET: создание сессии и вставка снапшотов с транзакциями.
using System.Configuration;
using System.Data;
using Microsoft.Data.SqlClient;

namespace ProjectDS;

public sealed class DbService
{
    //Строка подключения к SQL Server из App.config.
    private readonly string _cs;

    //Конструктор: читаем connectionString "ProjectDS" из конфигурации.
    public DbService()
    {
        _cs = ConfigurationManager.ConnectionStrings["ProjectDS"]?.ConnectionString
              ?? throw new InvalidOperationException("В App.config нет connectionString с именем 'ProjectDS'.");
    }

    //Создать новую игровую сессию: игрок, сессия, привязка систем по индексам.
    public async Task<long> StartNewSessionAsync(string playerName, IReadOnlyList<GameStarSystem> systems)
    {
        //Открываем соединение.
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync();
        //Начинаем транзакцию: либо всё создаётся, либо ничего.
        await using var tx = await con.BeginTransactionAsync();
        var stx = (SqlTransaction)tx;
        try
        {
            //Гарантируем, что игрок существует, и получаем его PlayerId.
            int playerId = await EnsurePlayerAsync(con, stx, playerName);
            //Создаём запись сессии и получаем SessionId.
            long sessionId = await CreateSessionAsync(con, stx, playerId);
            //Привязываем системы к сессии по индексам.
            for (int i = 0; i < systems.Count; i++)
            {
                //Получаем SystemId из справочника StarSystems.
                int systemId = await GetSystemIdAsync(con, stx, systems[i]);
                //Пишем связь SessionId + SystemIndex -> SystemId.
                await UpsertSessionSystemAsync(con, stx, sessionId, i, systemId);
            }
            //Коммитим транзакцию.
            await tx.CommitAsync();
            return sessionId;
        }
        catch
        {
            //Если что-то пошло не так, откатываем транзакцию.
            await tx.RollbackAsync();
            throw;
        }
    }

    //Вставить снапшот: запись в Snapshots, затем SnapshotShips, затем SessionEvents.
    public async Task InsertSnapshotAsync(long sessionId, GameSnapshot snap)
    {
        //Открываем соединение.
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync();
        //Начинаем транзакцию: снапшот и его корабли должны быть консистентны.
        await using var tx = await con.BeginTransactionAsync();
        var stx = (SqlTransaction)tx;
        try
        {
            //Id вставленного снапшота.
            long snapshotId;
            //SQL вставки снапшота с OUTPUT INSERTED.SnapshotId.
            const string sqlSnap = @"
INSERT INTO dbo.Snapshots
(SessionId, Turn, CurrentSystemIndex, Score,
 PlayerIdGuid, PlayerHp, PlayerMaxHp, PlayerState, PlayerCooldown, PlayerX, PlayerY,
 SelectedTargetId)
OUTPUT INSERTED.SnapshotId
VALUES
(@SessionId, @Turn, @CurrentSystemIndex, @Score,
 @PlayerIdGuid, @PlayerHp, @PlayerMaxHp, @PlayerState, @PlayerCooldown, @PlayerX, @PlayerY,
 @SelectedTargetId);";
            //Вставляем строку снапшота.
            await using (var cmd = new SqlCommand(sqlSnap, con, stx))
            {
                //Параметры сессии и состояния.
                cmd.Parameters.AddWithValue("@SessionId", sessionId);
                cmd.Parameters.AddWithValue("@Turn", snap.Turn);
                cmd.Parameters.AddWithValue("@CurrentSystemIndex", snap.CurrentSystemIndex);
                cmd.Parameters.AddWithValue("@Score", snap.Score);
                //Параметры игрока.
                cmd.Parameters.AddWithValue("@PlayerIdGuid", snap.PlayerId);
                cmd.Parameters.AddWithValue("@PlayerHp", snap.PlayerHp);
                cmd.Parameters.AddWithValue("@PlayerMaxHp", snap.PlayerMaxHp);
                cmd.Parameters.AddWithValue("@PlayerState", (byte)snap.PlayerState);
                cmd.Parameters.AddWithValue("@PlayerCooldown", snap.PlayerCooldown);
                cmd.Parameters.AddWithValue("@PlayerX", snap.PlayerX);
                cmd.Parameters.AddWithValue("@PlayerY", snap.PlayerY);
                //Выбранная цель может быть null.
                cmd.Parameters.AddWithValue("@SelectedTargetId", (object?)snap.SelectedTargetId ?? DBNull.Value);
                //Получаем SnapshotId.
                snapshotId = (long)(await cmd.ExecuteScalarAsync() ?? throw new Exception("Не удалось вставить snapshot (Snapshots)."));
            }
            //SQL вставки корабля снапшота.
            const string sqlShip = @"
INSERT INTO dbo.SnapshotShips
(SnapshotId, ShipId, Name, Faction, State, X, Y, Hp, MaxHp)
VALUES
(@SnapshotId, @ShipId, @Name, @Faction, @State, @X, @Y, @Hp, @MaxHp);";
            //Вставляем все корабли снапшота.
            foreach (var s in snap.Ships)
            {
                await using var cmd = new SqlCommand(sqlShip, con, stx);
                cmd.Parameters.AddWithValue("@SnapshotId", snapshotId);
                cmd.Parameters.AddWithValue("@ShipId", s.Id);
                cmd.Parameters.AddWithValue("@Name", s.Name);
                cmd.Parameters.AddWithValue("@Faction", (byte)s.Faction);
                cmd.Parameters.AddWithValue("@State", (byte)s.State);
                cmd.Parameters.AddWithValue("@X", s.X);
                cmd.Parameters.AddWithValue("@Y", s.Y);
                cmd.Parameters.AddWithValue("@Hp", s.Hp);
                cmd.Parameters.AddWithValue("@MaxHp", s.MaxHp);
                await cmd.ExecuteNonQueryAsync();
            }
            //Если есть логи, пишем их как события сессии.
            if (snap.Logs is { Count: > 0 })
            {
                const string sqlEv = @"
INSERT INTO dbo.SessionEvents(SessionId, Turn, Type, Message)
VALUES (@SessionId, @Turn, @Type, @Message);";
                foreach (var line in snap.Logs)
                {
                    await using var cmd = new SqlCommand(sqlEv, con, stx);
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    cmd.Parameters.AddWithValue("@Turn", snap.Turn);
                    cmd.Parameters.AddWithValue("@Type", "log");
                    cmd.Parameters.AddWithValue("@Message", line);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            //Коммитим транзакцию.
            await tx.CommitAsync();
        }
        catch
        {
            //Если БД снова устроит истерику, то в ответ истерику устрою уже я.
            await tx.RollbackAsync();
            throw;
        }
    }

    //Обеспечить наличие игрока: если есть, вернуть PlayerId, иначе вставить и вернуть новый.
    private static async Task<int> EnsurePlayerAsync(SqlConnection con, SqlTransaction tx, string playerName)
    {
        //Пробуем найти игрока по имени.
        const string sqlGet = "SELECT TOP(1) PlayerId FROM dbo.Players WHERE Name=@Name ORDER BY PlayerId;";
        await using (var cmd = new SqlCommand(sqlGet, con, tx))
        {
            cmd.Parameters.AddWithValue("@Name", playerName);
            var obj = await cmd.ExecuteScalarAsync();
            if (obj != null && obj != DBNull.Value) return (int)obj;
        }
        //Если не нашли, вставляем нового игрока.
        const string sqlIns = @"
INSERT INTO dbo.Players(Name)
OUTPUT INSERTED.PlayerId
VALUES (@Name);";
        await using (var cmd = new SqlCommand(sqlIns, con, tx))
        {
            cmd.Parameters.AddWithValue("@Name", playerName);
            return (int)(await cmd.ExecuteScalarAsync() ?? throw new Exception("Не удалось вставить игрока (Players)."));
        }
    }

    //Создать запись игровой сессии и вернуть SessionId.
    private static async Task<long> CreateSessionAsync(SqlConnection con, SqlTransaction tx, int playerId)
    {
        const string sql = @"
INSERT INTO dbo.GameSessions(PlayerId)
OUTPUT INSERTED.SessionId
VALUES (@PlayerId);";
        await using var cmd = new SqlCommand(sql, con, tx);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new Exception("Не удалось создать сессию (GameSessions)."));
    }

    //Получить SystemId из справочника StarSystems по имени системы и имени планеты.
    private static async Task<int> GetSystemIdAsync(SqlConnection con, SqlTransaction tx, GameStarSystem sys)
    {
        const string sql = @"
SELECT TOP(1) SystemId
FROM dbo.StarSystems
WHERE Name=@Name AND PlanetName=@PlanetName;";
        await using var cmd = new SqlCommand(sql, con, tx);
        cmd.Parameters.AddWithValue("@Name", sys.Name);
        cmd.Parameters.AddWithValue("@PlanetName", sys.Planet.Name);
        var obj = await cmd.ExecuteScalarAsync();
        if (obj == null || obj == DBNull.Value)
            throw new InvalidOperationException($"В dbo.StarSystems нет записи: {sys.Name} / {sys.Planet.Name}");
        return (int)obj;
    }

    //Upsert связи SessionSystems: если запись есть, обновляем, иначе вставляем.
    private static async Task UpsertSessionSystemAsync(SqlConnection con, SqlTransaction tx, long sessionId, int systemIndex, int systemId)
    {
        const string sql = @"
IF EXISTS (SELECT 1 FROM dbo.SessionSystems WHERE SessionId=@SessionId AND SystemIndex=@SystemIndex)
    UPDATE dbo.SessionSystems SET SystemId=@SystemId WHERE SessionId=@SessionId AND SystemIndex=@SystemIndex;
ELSE
    INSERT INTO dbo.SessionSystems(SessionId, SystemIndex, SystemId)
    VALUES (@SessionId, @SystemIndex, @SystemId);";
        await using var cmd = new SqlCommand(sql, con, tx);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        cmd.Parameters.AddWithValue("@SystemIndex", systemIndex);
        cmd.Parameters.AddWithValue("@SystemId", systemId);
        await cmd.ExecuteNonQueryAsync();
    }
}
