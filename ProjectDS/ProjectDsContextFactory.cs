//Фабрика EF-контекста: создаёт ProjectDsContext с подключением из App.config.
using System.Configuration;
using Microsoft.EntityFrameworkCore;
using ProjectDS.DbEf;

namespace ProjectDS;

public sealed class ProjectDsContextFactory
{
    //Строка подключения к SQL Server из App.config.
    private readonly string _cs;

    //Конструктор: читаем connectionString "ProjectDS".
    public ProjectDsContextFactory()
    {
        _cs = ConfigurationManager.ConnectionStrings["ProjectDS"]?.ConnectionString
              ?? throw new InvalidOperationException("Нет connectionString 'ProjectDS' в App.config");
    }

    //Создать новый контекст EF: вызывается сервисами БД.
    public ProjectDsContext Create()
    {
        //Собираем опции контекста с провайдером SQL Server.
        var opt = new DbContextOptionsBuilder<ProjectDsContext>().UseSqlServer(_cs).Options;
        //Создаём контекст.
        return new ProjectDsContext(opt);
    }
}
