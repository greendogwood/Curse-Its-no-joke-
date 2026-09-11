//Модель игры: фракции, состояния, планеты, системы, координаты, корабли и математика движения.
namespace ProjectDS;

//Фракция корабля: игрок, союзник или враг.
public enum Faction { Player, Ally, Enemy }
//Состояние корабля: летит или на планете.
public enum ShipState { Flying, Landed }

//Планета: имя и позиция в нормализованных координатах 0..1.
public sealed class Planet
{
    //Имя планеты.
    public string Name { get; }
    //Позиция планеты на карте 0..1.
    public PointN Pos { get; }
    //Конструктор планеты.
    public Planet(string name, PointN pos) { Name = name; Pos = pos; }
}

//Звёздная система: имя и планета.
public sealed class GameStarSystem
{
    //Имя системы.
    public string Name { get; }
    //Планета системы.
    public Planet Planet { get; }
    //Конструктор системы.
    public GameStarSystem(string name, Planet planet) { Name = name; Planet = planet; }
}

//Нормализованная точка 0..1: используется для позиций и destination.
public readonly record struct PointN(double X, double Y)
{
    //Сложение точки и вектора: получить новую точку.
    public static PointN operator +(PointN a, VecN b) => new(a.X + b.X, a.Y + b.Y);
    //Разность двух точек: получить вектор.
    public static VecN operator -(PointN a, PointN b) => new(a.X - b.X, a.Y - b.Y);
}

//Нормализованный вектор: используется для движения и расстояний.
public readonly record struct VecN(double X, double Y)
{
    //Длина вектора.
    public double Len => Math.Sqrt(X * X + Y * Y);
    //Нормализация: получить единичный вектор.
    public VecN Norm()
    {
        var l = Len;
        return l < 1e-9 ? new VecN(0, 0) : new VecN(X / l, Y / l);
    }
    //Умножение вектора на скаляр.
    public static VecN operator *(VecN v, double k) => new(v.X * k, v.Y * k);
}

//Корабль: сущность игры, которая двигается, атакует и может умереть.
public sealed class Ship
{
    //Уникальный Id корабля: используется для выбора цели и снапшотов.
    public Guid Id { get; } = Guid.NewGuid();
    //Имя корабля: "Игрок", "Враг", "Союзник", "Ренегат".
    public string Name { get; set; } = "Ship";
    //Фракция корабля.
    public Faction Faction { get; set; }
    //Позиция корабля на карте 0..1.
    public PointN Pos { get; set; }
    //Точка назначения: если null, корабль не летит к цели.
    public PointN? Destination { get; set; }
    //Текущее HP.
    public double Hp { get; set; } = 10;
    //Максимальное HP.
    public double MaxHp { get; set; } = 10;
    //Скорость движения за тик.
    public double Speed { get; set; } = 0.06;
    //Состояние: летит или на планете.
    public ShipState State { get; set; } = ShipState.Flying;
    //Кулдаун атаки в ходах.
    public int AttackCooldownTurns { get; set; } = 0;
    //Id цели преследования: нужен, чтобы враги могли прыгнуть за игроком.
    public Guid? LastChaseTargetId { get; set; }
    //Флаг жизни: жив, пока Hp > 0.
    public bool IsAlive => Hp > 0;
    //Строковое представление: удобно для отладки.
    public override string ToString() => $"{Name} [{Faction}] HP:{Hp:0.0}/{MaxHp:0.0}";
}

//Математика для нормализованной карты: расстояния, движение и clamp.
public static class MathN
{
    //Расстояние между точками.
    public static double Dist(PointN a, PointN b) => (a - b).Len;

    //Движение к цели с ограничением шага: возвращает новую точку.
    public static PointN MoveTowards(PointN from, PointN to, double maxStep)
    {
        var v = to - from;
        var len = v.Len;
        if (len < 1e-9) return from;
        if (len <= maxStep) return to;
        var n = v.Norm();
        return from + n * maxStep;
    }

    //Зажим значения в диапазон 0..1.
    public static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);
}
