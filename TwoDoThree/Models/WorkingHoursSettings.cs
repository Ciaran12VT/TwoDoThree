using TwoDoThree.ViewModels;

namespace TwoDoThree.Models;

public sealed class WorkingHoursSettings : ObservableObject
{
    private bool isOutOfHoursConfirmationEnabled;
    private TimeSpan workdayStart = new(9, 0, 0);
    private TimeSpan workdayEnd = new(17, 0, 0);
    private int confirmationIntervalMinutes = 15;
    private bool monday = true;
    private bool tuesday = true;
    private bool wednesday = true;
    private bool thursday = true;
    private bool friday = true;
    private bool saturday;
    private bool sunday;

    public bool IsOutOfHoursConfirmationEnabled
    {
        get => isOutOfHoursConfirmationEnabled;
        set => SetProperty(ref isOutOfHoursConfirmationEnabled, value);
    }

    public TimeSpan WorkdayStart
    {
        get => workdayStart;
        set => SetProperty(ref workdayStart, NormalizeTimeOfDay(value));
    }

    public TimeSpan WorkdayEnd
    {
        get => workdayEnd;
        set => SetProperty(ref workdayEnd, NormalizeTimeOfDay(value));
    }

    public int ConfirmationIntervalMinutes
    {
        get => confirmationIntervalMinutes;
        set => SetProperty(ref confirmationIntervalMinutes, Math.Clamp(value, 1, 1440));
    }

    public bool Monday
    {
        get => monday;
        set => SetProperty(ref monday, value);
    }

    public bool Tuesday
    {
        get => tuesday;
        set => SetProperty(ref tuesday, value);
    }

    public bool Wednesday
    {
        get => wednesday;
        set => SetProperty(ref wednesday, value);
    }

    public bool Thursday
    {
        get => thursday;
        set => SetProperty(ref thursday, value);
    }

    public bool Friday
    {
        get => friday;
        set => SetProperty(ref friday, value);
    }

    public bool Saturday
    {
        get => saturday;
        set => SetProperty(ref saturday, value);
    }

    public bool Sunday
    {
        get => sunday;
        set => SetProperty(ref sunday, value);
    }

    public bool IsWithinWorkingHours(DateTime localTime)
    {
        var timeOfDay = localTime.TimeOfDay;
        if (WorkdayStart == WorkdayEnd)
        {
            return IsWorkingDay(localTime.DayOfWeek);
        }

        if (WorkdayStart < WorkdayEnd)
        {
            return IsWorkingDay(localTime.DayOfWeek)
                   && timeOfDay >= WorkdayStart
                   && timeOfDay < WorkdayEnd;
        }

        var previousDay = localTime.Date.AddDays(-1).DayOfWeek;
        return (IsWorkingDay(localTime.DayOfWeek) && timeOfDay >= WorkdayStart)
               || (IsWorkingDay(previousDay) && timeOfDay < WorkdayEnd);
    }

    public bool IsWorkingDay(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => Monday,
            DayOfWeek.Tuesday => Tuesday,
            DayOfWeek.Wednesday => Wednesday,
            DayOfWeek.Thursday => Thursday,
            DayOfWeek.Friday => Friday,
            DayOfWeek.Saturday => Saturday,
            DayOfWeek.Sunday => Sunday,
            _ => false
        };
    }

    public void CopyFrom(WorkingHoursSettings source)
    {
        IsOutOfHoursConfirmationEnabled = source.IsOutOfHoursConfirmationEnabled;
        WorkdayStart = source.WorkdayStart;
        WorkdayEnd = source.WorkdayEnd;
        ConfirmationIntervalMinutes = source.ConfirmationIntervalMinutes;
        Monday = source.Monday;
        Tuesday = source.Tuesday;
        Wednesday = source.Wednesday;
        Thursday = source.Thursday;
        Friday = source.Friday;
        Saturday = source.Saturday;
        Sunday = source.Sunday;
    }

    private static TimeSpan NormalizeTimeOfDay(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value >= TimeSpan.FromDays(1)
            ? TimeSpan.FromDays(1).Subtract(TimeSpan.FromMinutes(1))
            : TimeSpan.FromMinutes(Math.Floor(value.TotalMinutes));
    }
}
