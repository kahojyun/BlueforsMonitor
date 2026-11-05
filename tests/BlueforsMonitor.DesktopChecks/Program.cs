using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using BlueforsAvalonia;
using ScottPlot.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppSettings.SettingsDirectory = Path.Combine(Path.GetTempPath(), "bluefors-ui-" + Guid.NewGuid().ToString("N"));
        return AppBuilder.Configure<CheckApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
public sealed class CheckApp : App
{
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = (MainWindow)lifetime.MainWindow!;
        window.Opened += async (_, _) =>
        {
            int result = 0;
            try { await Check(window); Console.WriteLine("PASS desktop: logs, themes, calendar, axes, events, follow"); }
            catch(Exception error) { Console.Error.WriteLine(error); result = 1; }
            finally { lifetime.Shutdown(result); }
        };
    }
    static T Field<T>(MainWindow w, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
    static object? Call(MainWindow w,string name,params object[] args) => typeof(MainWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(w,args);
    static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    static async Task Check(MainWindow w)
    {
        Require(!Field<DataStore>(w,"_store").HasData,"First launch must be empty");
        string logs=Path.Combine(AppSettings.Root,"logs");Directory.CreateDirectory(logs);
        string file=Path.Combine(logs,"CH6 T 26-11-05.log");
        File.WriteAllLines(file,Enumerable.Range(0,60).Select(i=>$"05-11-26,10:{i:00}:00,{(i>=20&&i<=22?"0.028":"0.012")}"));
        await (Task)Call(w,"OpenLogs",logs)!;
        var store=Field<DataStore>(w,"_store"); Require(store.Snapshot.Events.Length==1,"Event indexing");
        var plot=Field<AvaPlot>(w,"_plot"); await Task.Delay(250);
        float width=plot.Plot.LastRender.DataRect.Width;
        var checks=Field<Dictionary<string,CheckBox>>(w,"_checks");
        foreach(string key in new[]{"Flow","P2","P3","P4"}) checks[key].IsChecked=false;
        await Task.Delay(250);Require(plot.Plot.LastRender.DataRect.Width>width+150,"Hidden axes must release space");
        var theme=Field<ComboBox>(w,"_theme");
        theme.SelectedItem="浅色"; await Task.Delay(100);Require(!Field<bool>(w,"_dark"),"Light theme");
        theme.SelectedItem="深色"; await Task.Delay(100);Require(Field<bool>(w,"_dark"),"Dark theme");
        theme.SelectedItem="跟随系统";
        var from=Field<DateTimeField>(w,"_from");var to=Field<DateTimeField>(w,"_to");
        double start=Clock.Unix(new DateTime(2026,11,5,10,0,0));from.Set(start);to.Set(start+3600);
        from.Date.IsDropDownOpen=true; await Task.Delay(100);Require(from.Date.IsDropDownOpen,"Calendar popup");from.Date.IsDropDownOpen=false;
        Call(w,"ApplyDates");if(w.LastLoad!=null)await w.LastLoad;
        Require(Field<double>(w,"_left")==start,"Date apply");
        Call(w,"SelectEvent",store.Snapshot.Events[0]);Require(Field<double>(w,"_left")==start,"Selection must not jump");
        await (Task)Call(w,"ShowEvent",store.Snapshot.Events[0])!;
        Require(from.Value==Clock.At(start),"Replay must preserve entered dates");
        double before=Field<double>(w,"_right");
        File.AppendAllText(file,"05-11-26,11:00:00,0.013\n");await (Task)Call(w,"SyncCurrent",true)!;
        Require(Field<double>(w,"_right")==before,"Paused view moved");
        Field<CheckBox>(w,"_follow").IsChecked=true;if(w.LastLoad!=null)await w.LastLoad;
        File.AppendAllText(file,"05-11-26,11:01:00,0.013\n");await (Task)Call(w,"SyncCurrent",true)!;
        Require(Field<double>(w,"_right")==start+3660,"Follow did not advance");
    }
}
