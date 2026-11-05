using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace BlueforsAvalonia;

public sealed class MonitorViewModel : INotifyPropertyChanged
{
    private string _status = "选择 Logfiles 目录开始使用。";
    private string _source = "";
    private string _eventCount = "升温事件 0";
    public string Status
    {
        get => _status; set => Set(ref _status, value);
    }
    public string Source
    {
        get => _source; set => Set(ref _source, value);
    }
    public string EventCount
    {
        get => _eventCount; set => Set(ref _eventCount, value);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
