//Конфиг игры: все параметры тиков, лимитов, боя, спавна, разделения и лога в одном месте.
namespace ProjectDS;

public sealed class GameConfig
{
    //Интервал тика: как часто раннер вызывает Tick().
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(1000);
    //Лимит врагов в системе: защита от бесконечного спавна.
    public int MaxEnemies { get; init; } = 100;
    //Лимит союзников в системе: защита от бесконечного спавна.
    public int MaxAllies { get; init; } = 100;
    //Общий лимит кораблей в системе: верхняя граница для всех фракций.
    public int MaxTotal { get; init; } = 201;
    //Минимальный интервал спавна в ходах.
    public int SpawnIntervalMinTurns { get; init; } = 1;
    //Максимальный интервал спавна.
    public int SpawnIntervalMaxTurnsExclusive { get; init; } = 2;
    //Кулдаун атаки по умолчанию: сколько ходов ждать после выстрела.
    public int AttackCooldownDefault { get; init; } = 1;
    //Дальность атаки: если цель дальше, атака не проходит.
    public double AttackRange { get; init; } = 0.2;
    //Включить разделение кораблей: раздвигаем, чтобы не слипались.
    public bool SeparationEnabled { get; init; } = true;
    //Как часто применять разделение: раз в N тиков.
    public int SeparationEveryNTicks { get; init; } = 10;
    //Радиус разделения: в пределах этого радиуса корабли начинают отталкиваться.
    public double SeparationRadius { get; init; } = 0.040;
    //Сила разделения: насколько сильно сдвигаем корабль за применение.
    public double SeparationStrength { get; init; } = 0.020;
    //Множитель активного радиуса: уменьшает реальную зону отталкивания.
    public double SeparationActiveRadiusFactor { get; init; } = 0.70;
    //Максимум строк лога в UI: старые строки выкидываются.
    public int MaxLogLines { get; init; } = 100;
}
