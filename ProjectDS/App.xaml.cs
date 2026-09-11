//WPF-приложение: глобальные обработчики необработанных исключений и старт главного окна.
using System.Windows;

namespace ProjectDS;

public partial class App : Application
{
    //Конструктор приложения: подписываемся на глобальные исключения.
    public App()
    {
        //Исключения UI-потока: показываем MessageBox и помечаем как обработанные.
        DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show(e.Exception.ToString(), "Ошибка (UI)");
            e.Handled = true;
        };
        //Исключения домена: пытаемся показать MessageBox, но не гарантируем, что UI жив.
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                MessageBox.Show(e.ExceptionObject?.ToString() ?? "unknown", "Ошибка (Domain)");
            }
            catch
            {
                //Если даже MessageBox не показывается, значит всё совсем плохо, но мы хотя бы попытались.
            }
        };
    }

    //Старт приложения: создаём и показываем главное окно.
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        //Создаём главное окно.
        var w = new MainWindow();
        //Показываем окно.
        w.Show();
    }
}
