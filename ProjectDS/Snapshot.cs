//Снапшоты: сериализуемое состояние игры для UI, сети и сохранения в БД.
namespace ProjectDS;

//Снапшот корабля: минимальный набор данных для отрисовки и аналитики.
public sealed record ShipSnapshot(
    //Id корабля.
    Guid Id,
    //Имя корабля.
    string Name,
    //Фракция корабля.
    Faction Faction,
    //Состояние корабля: летит или на планете.
    ShipState State,
    //Позиция X 0..1.
    double X,
    //Позиция Y 0..1.
    double Y,
    //Текущее HP.
    double Hp,
    //Максимальное HP.
    double MaxHp,
    //Флаг жизни: обычно совпадает с Hp > 0, но оставлен явно.
    bool IsAlive
);

//Снапшот игры: всё, что нужно UI/зрителю/БД на один тик.
public sealed record GameSnapshot(
    //Индекс текущей системы.
    int CurrentSystemIndex,
    //Имя текущей системы.
    string SystemName,
    //Имя планеты текущей системы.
    string PlanetName,
    //Позиция планеты X 0..1.
    double PlanetX,
    //Позиция планеты Y 0..1.
    double PlanetY,
    //Счёт игрока.
    int Score,
    //Номер хода.
    int Turn,
    //Флаг окончания игры.
    bool IsGameOver,
    //Id игрока.
    Guid PlayerId,
    //HP игрока.
    double PlayerHp,
    //Максимальное HP игрока.
    double PlayerMaxHp,
    //Состояние игрока.
    ShipState PlayerState,
    //Кулдаун атаки игрока.
    int PlayerCooldown,
    //Позиция игрока X 0..1.
    double PlayerX,
    //Позиция игрока Y 0..1.
    double PlayerY,
    //Есть ли destination у игрока.
    bool PlayerHasDestination,
    //Destination X 0..1.
    double PlayerDstX,
    //Destination Y 0..1.
    double PlayerDstY,
    //Id выбранной цели или null.
    Guid? SelectedTargetId,
    //Список кораблей: обычно все живые, включая игрока.
    IReadOnlyList<ShipSnapshot> Ships,
    //Логи с прошлого снапшота: UI добавляет их в свой лог.
    IReadOnlyList<string> Logs
);
