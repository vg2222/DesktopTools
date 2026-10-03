using System;
using System.Globalization;
namespace DesktopTools.Core;

public sealed class AutomationTriggerState
{
    private DateTime due,dailyDate;
    private bool present,startup,windowPending;
    public AutomationTriggerState(AutomationScript script,DateTime now,bool windowPresent,bool atStartup)
    {
        due=now.AddMinutes(Math.Max(1,script.IntervalMinutes));present=windowPresent;startup=atStartup&&script.Armed&&script.RunOnStartup;
        dailyDate=TimeOnly.TryParseExact(script.DailyTime,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time)&&TimeOnly.FromDateTime(now)>=time?now.Date:DateTime.MinValue;
    }
    public bool Take(AutomationScript script,DateTime now,bool windowPresent,bool canRun)
    {
        if(windowPresent&&!present)windowPending=true;present=windowPresent;if(!windowPresent)windowPending=false;
        bool daily=TimeOnly.TryParseExact(script.DailyTime,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time)&&dailyDate<now.Date&&TimeOnly.FromDateTime(now)>=time;
        bool interval=script.IntervalMinutes>0&&now>=due;
        if(!script.Armed||!canRun||!(startup||daily||interval||windowPending))return false;
        startup=false;windowPending=false;if(daily)dailyDate=now.Date;if(interval)due=now.AddMinutes(script.IntervalMinutes);return true;
    }
}
