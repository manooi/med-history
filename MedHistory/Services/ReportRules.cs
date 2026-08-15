using System.Globalization;
using MedHistory.Models;

namespace MedHistory.Services;

/// <summary>
/// An allocation reduced to what the report reads: which row, which day it was planned for,
/// and how many doses that day's plan asks for. The name, the dose quantity and the stock link
/// are all irrelevant here — the report counts doses across every medication at once, so what a
/// row is called never changes its arithmetic.
/// </summary>
public readonly record struct ReportAllocation(int Id, DateOnly Day, MedSlots Slots);

/// <summary>
/// How much of one day's plan was worked through. <see cref="NoPlan"/> is not "nothing done" —
/// it is a day with nothing to do, which must not read as a missed day on the calendar.
/// </summary>
public enum DayProgress
{
    NoPlan,
    None,
    Partial,
    Full
}

/// <summary>
/// One day of the report: the doses its plan asked for, and how many of them were ticked off.
/// Both counts are slots, not medications — a day with two medications at two slots each asks
/// for four.
/// </summary>
public readonly record struct ReportDay(DateOnly Day, int Planned, int Ticked)
{
    /// <summary>
    /// Which of the four states the day is in. Ticked above planned still reads as
    /// <see cref="DayProgress.Full"/> rather than falling through to Partial: the count can only
    /// exceed the plan if the plan shrank under doses already logged, and a day whose every
    /// remaining slot is ticked is done whatever the history behind it.
    /// </summary>
    public DayProgress State => Planned == 0
        ? DayProgress.NoPlan
        : Ticked == 0
            ? DayProgress.None
            : Ticked >= Planned
                ? DayProgress.Full
                : DayProgress.Partial;

    /// <summary>"3/4" — empty on a day with nothing planned, where there is no fraction to show.</summary>
    public string ProgressLabel => Planned == 0 ? string.Empty : $"{Ticked}/{Planned}";
}

/// <summary>
/// One row of the calendar: seven cells, Monday first. A null cell is a day of the neighbouring
/// month, which the grid leaves blank rather than filling in — the report is one month at a
/// time, and a half-lit trailing week would invite reading it as data.
/// </summary>
public readonly record struct ReportWeek(IReadOnlyList<ReportDay?> Days);

/// <summary>
/// A month as the report page renders it: the grid, and the month's own totals. The totals count
/// the month's real days only, so they never pick up the blank cells the grid pads with.
/// </summary>
public sealed record ReportMonth(
    DateOnly FirstDay,
    IReadOnlyList<ReportWeek> Weeks,
    int Planned,
    int Ticked)
{
    public string Key => ReportRules.MonthKey(FirstDay);

    public string Label => ReportRules.MonthLabel(FirstDay);

    public string PreviousKey => ReportRules.MonthKey(FirstDay.AddMonths(-1));

    public string NextKey => ReportRules.MonthKey(FirstDay.AddMonths(1));

    /// <summary>How the month reads under its name — the same fraction the cells carry.</summary>
    public string ProgressLabel => Planned == 0 ? "nothing planned" : $"{Ticked}/{Planned} doses";
}

/// <summary>
/// Pure report rules — no clock, no database, no HTTP. Turns a month's allocations and the ticks
/// logged against them into a Monday-first calendar grid.
///
/// A day is bucketed by the day its allocation was planned for, never by when the tick's entry
/// says it happened. The report is a picture of a plan being worked through, and a retro tick —
/// ticking Tuesday's bedtime dose on Wednesday morning — belongs to Tuesday's plan wherever its
/// entry landed on the timeline. This is the one place the app reads a tick that way; the day
/// view scopes ticks to the day's own entries because it is showing that day's timeline.
///
/// What counts as ticked is otherwise decided exactly as the checklist decides it, through
/// <see cref="ChecklistRules.FindTick"/>: a link to an allocation that is not there, a slot name
/// that parses to nothing, and a slot the allocation does not actually plan all count for
/// nothing, and two entries on one slot count once. Anything else and the calendar would
/// disagree with the day pages it links to.
/// </summary>
public static class ReportRules
{
    public const int DaysPerWeek = 7;

    /// <summary>The URL segment form of a month, e.g. <c>/med-report/2026-08</c>.</summary>
    public const string MonthFormat = "yyyy-MM";

    /// <summary>Column headings, in the grid's own order. Monday first, as the weeks are built.</summary>
    public static readonly IReadOnlyList<string> WeekdayLabels =
        ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    /// <summary>The first of the month a day falls in — the canonical way a month is held.</summary>
    public static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

    public static string MonthKey(DateOnly month) =>
        month.ToString(MonthFormat, CultureInfo.InvariantCulture);

    /// <summary>How a month reads on screen, e.g. "August 2026".</summary>
    public static string MonthLabel(DateOnly month) =>
        month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads a month out of a URL segment, as the first of that month. Strict: exactly
    /// <c>yyyy-MM</c>, so "2026-8", "2026-08-01" and a stray space are all rejected rather than
    /// coerced into some nearby month the user did not ask for. That strictness is why it is the
    /// day parser doing the work on a padded value — one rule for what a well-formed date
    /// segment is, rather than two that can drift apart.
    /// </summary>
    public static bool TryParseMonth(string? value, out DateOnly month) =>
        DateOnly.TryParseExact(
            $"{value}-01", AppTime.DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out month);

    /// <summary>
    /// The whole report for one month: the grid and the totals. Allocations outside the month
    /// contribute nothing, so a caller that over-fetches cannot bend the month's numbers, and
    /// ticks belonging to no listed allocation are ignored the same way the checklist ignores
    /// them.
    /// </summary>
    public static ReportMonth BuildMonth(
        DateOnly month,
        IEnumerable<ReportAllocation> allocations,
        IEnumerable<ChecklistTick> ticks)
    {
        var first = FirstOfMonth(month);
        var days = TallyDays(allocations.Where(a => FirstOfMonth(a.Day) == first), ticks);

        return new ReportMonth(
            first,
            BuildWeeks(first, days),
            days.Sum(day => day.Planned),
            days.Sum(day => day.Ticked));
    }

    /// <summary>
    /// One tally per day that has anything planned, in day order. A day nothing is allocated to
    /// produces no row at all rather than a zero one — <see cref="BuildWeeks"/> fills those in,
    /// because only it knows which days the month has.
    /// </summary>
    public static IReadOnlyList<ReportDay> TallyDays(
        IEnumerable<ReportAllocation> allocations,
        IEnumerable<ChecklistTick> ticks)
    {
        // Grouped once rather than scanned per slot: every allocation only ever looks at the
        // ticks that name it.
        var byAllocation = ticks
            .Where(tick => tick.AllocationId is not null)
            .ToLookup(tick => tick.AllocationId!.Value);

        return allocations
            .GroupBy(allocation => allocation.Day)
            .Select(day => new ReportDay(
                day.Key,
                day.Sum(allocation => MedPlanRules.SlotCount(allocation.Slots)),
                day.Sum(allocation => TickedCount(allocation, byAllocation[allocation.Id]))))
            .OrderBy(day => day.Day)
            .ToList();
    }

    /// <summary>
    /// The month laid out as weeks of seven, Monday first, with the days before the first and
    /// after the last left blank. Days missing from <paramref name="days"/> are days with nothing
    /// planned; days given twice are a caller error, not something to reconcile here — see
    /// <see cref="TallyDays"/>, which is where they come from. A thin wrapper over
    /// <see cref="BuildWeeks{TCell}"/> — the grid shape is the same for every calendar report in
    /// the app, only what goes in each cell differs.
    /// </summary>
    public static IReadOnlyList<ReportWeek> BuildWeeks(DateOnly month, IEnumerable<ReportDay> days)
    {
        var planned = days.ToDictionary(day => day.Day);

        return BuildWeeks<ReportDay>(
                month, day => planned.TryGetValue(day, out var tally) ? tally : new ReportDay(day, 0, 0))
            .Select(week => new ReportWeek(week))
            .ToList();
    }

    /// <summary>
    /// The generic shape every month-calendar report in the app shares: seven columns, Monday
    /// first, the days before the first and after the last of the month left blank so the grid
    /// stays rectangular. <paramref name="cellFor"/> is asked for one cell per real day of the
    /// month, in order, and is never asked about a blank — it decides what a report shows for a
    /// day, this only decides where that goes.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<TCell?>> BuildWeeks<TCell>(
        DateOnly month, Func<DateOnly, TCell?> cellFor)
        where TCell : struct
    {
        var first = FirstOfMonth(month);
        var cells = new List<TCell?>();

        cells.AddRange(Enumerable.Repeat<TCell?>(null, LeadingBlanks(first)));

        for (var dayNumber = 1; dayNumber <= DateTime.DaysInMonth(first.Year, first.Month); dayNumber++)
        {
            cells.Add(cellFor(new DateOnly(first.Year, first.Month, dayNumber)));
        }

        // Padded out so the last row is seven cells wide like every other — a short row would
        // stretch its cells across the grid's columns.
        while (cells.Count % DaysPerWeek != 0)
        {
            cells.Add(null);
        }

        return Enumerable.Range(0, cells.Count / DaysPerWeek)
            .Select(week => (IReadOnlyList<TCell?>)cells.GetRange(week * DaysPerWeek, DaysPerWeek))
            .ToList();
    }

    /// <summary>
    /// Blank cells before the first of the month. Monday is column zero, so Sunday — which .NET
    /// numbers zero — is the last column, not the first.
    /// </summary>
    public static int LeadingBlanks(DateOnly first) => ((int)first.DayOfWeek + 6) % DaysPerWeek;

    /// <summary>
    /// How many of one allocation's slots were ticked. Only the slots the allocation actually
    /// plans are asked about, so a tick left behind by an edit that dropped a slot counts for
    /// nothing — the same slots the day view would draw controls for.
    /// </summary>
    private static int TickedCount(ReportAllocation allocation, IEnumerable<ChecklistTick> ticks) =>
        MedPlanRules.Each(allocation.Slots)
            .Count(slot => ChecklistRules.FindTick(ticks, allocation.Id, slot) is not null);
}
