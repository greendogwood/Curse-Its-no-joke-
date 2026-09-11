//Команды игры: маленькие объекты, которые применяются к Game через GameRunner.
namespace ProjectDS;

//Интерфейс команды: любая команда должна уметь примениться к игре.
public interface IGameCommand
{
    //Применить команду к игре: меняет состояние Game.
    void Apply(Game game);
}

//Команда: задать destination игроку.
public sealed record CmdSetDestination(PointN Dest) : IGameCommand
{
    //Применение: вызываем API игры.
    public void Apply(Game game) => game.SetPlayerDestination(Dest);
}

//Команда: посадка/взлёт игрока.
public sealed record CmdToggleLandTakeoff() : IGameCommand
{
    //Применение: вызываем API игры.
    public void Apply(Game game) => game.ToggleLandTakeoff();
}

//Команда: прыжок в другую систему.
public sealed record CmdJump() : IGameCommand
{
    //Применение: вызываем API игры.
    public void Apply(Game game) => game.JumpToOtherSystem();
}

//Команда: атака игрока.
public sealed record CmdAttack() : IGameCommand
{
    //Применение: вызываем API игры.
    public void Apply(Game game) => game.PlayerAttack();
}

//Команда: рестарт игры.
public sealed record CmdRestart() : IGameCommand
{
    //Применение: пересоздаём состояние игры.
    public void Apply(Game game) => game.Init();
}

//Команда: выбрать цель по Id или сбросить выбор.
public sealed record CmdSelectTarget(Guid? TargetId) : IGameCommand
{
    //Применение: валидируем цель и сохраняем SelectedTargetId.
    public void Apply(Game game)
    {
        //Если TargetId null, просто сбрасываем выбор.
        if (TargetId == null)
        {
            game.SelectedTargetId = null;
            return;
        }
        //Ищем корабль по Id в текущей системе.
        var t = game.Ships.Find(s => s.Id == TargetId.Value);
        //Выбираем цель только если она существует, жива и не игрок.
        game.SelectedTargetId = (t != null && t.IsAlive && t.Faction != Faction.Player) ? TargetId : null;
    }
}
