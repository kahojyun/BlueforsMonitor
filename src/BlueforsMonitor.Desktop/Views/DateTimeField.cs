using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;

namespace BlueforsMonitor;

public sealed class DateTimeField : StackPanel
{
    public CalendarDatePicker Date { get; } = new() { MinWidth = 150, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "yyyy-MM-dd", FirstDayOfWeek = DayOfWeek.Monday };
    public TimePicker Time { get; } = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 1 };
    public DateTime? Value => TryGetValue(out var value, out _) ? value : null;
    public DateTimeField()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        // Avalonia otherwise silently restores the previous date after a parse error.
        // Clear it so applying an invalid edit cannot navigate to stale input.
        Date.DateValidationError += (_, _) => Date.SelectedDate = null;
        Children.Add(Date);
        Children.Add(Time);
        ToolTip.SetTip(Date, "日期：yyyy-MM-dd；日志时间 UTC+08:00");
        ToolTip.SetTip(Time, "24 小时制；日志时间 UTC+08:00");
    }
    public bool TryGetValue(out DateTime value, out string error)
    {
        value = default;
        // Validate the edited text too: SelectedDate may still contain the previous valid date.
        if (!DateTime.TryParseExact(Date.Text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var day))
        {
            error = "请选择日期，或按 yyyy-MM-dd 输入有效日期。";
            return false;
        }
        if (Time.SelectedTime is not TimeSpan time)
        {
            error = "请选择时间。";
            return false;
        }
        value = day.Date + time;
        error = "";
        return true;
    }
    public void Set(double unix)
    {
        // The date panel starts collapsed. CalendarDatePicker needs its calendar part
        // before Text parsing, even when it has not participated in layout yet.
        Date.ApplyTemplate();
        var value = Clock.At(unix);
        Date.SelectedDate = value.Date;
        Date.DisplayDate = value.Date;
        Date.Text = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Time.SelectedTime = new TimeSpan(value.Hour, value.Minute, 0);
    }
}
