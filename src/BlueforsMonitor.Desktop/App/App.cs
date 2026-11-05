using Avalonia;
using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace BlueforsMonitor;

public class App : Application
{
    public override void Initialize()
    {
        Name = "Bluefors Monitor";
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
        UiTheme.Install(this);
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new FluentTheme());
        Styles.Add(new Style(x => x.OfType<Button>()) { Setters = { new Setter(TemplatedControl.FontSizeProperty, 12d), new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(5)) } });
        Styles.Add(new Style(x => x.OfType<TextBox>()) { Setters = { new Setter(TemplatedControl.FontSizeProperty, 12d), new Setter(TemplatedControl.MinHeightProperty, 34d) } });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(ContentControl.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch), new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 1)) } });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(new DataStore(Path.Combine(AppSettings.Root, "empty")));
            desktop.MainWindow = window;
            if (PackageCheck.DirectoryPath != null)
                window.Opened += async (_, _) => await PackageCheck.Run(window, desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
