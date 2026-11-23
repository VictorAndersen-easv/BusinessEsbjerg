import { weekdayName } from "./Format";
import type { Schedule, ScheduleDayInput } from "./Types";

/** One weekday as typed in the form. Empty strings mean "day off". */
export interface EditableDay {
    start: string; // "08:00"
    end: string;
}

/** The company's standard week: Mon-Thu 7.5 h, Fri 7 h = 37 h. */
export function defaultWeek(): EditableDay[] {
    return [
        { start: "08:00", end: "15:30" },
        { start: "08:00", end: "15:30" },
        { start: "08:00", end: "15:30" },
        { start: "08:00", end: "15:30" },
        { start: "08:00", end: "15:00" },
    ];
}

function toMinutes(time: string): number | null {
    const match = /^(\d{2}):(\d{2})$/.exec(time);
    if (!match) return null;
    const [, hours, minutes] = match;
    return Number(hours) * 60 + Number(minutes);
}

/** Hours for one day (two decimals), or a problem text if the times don't make sense. */
export function dayHours(day: EditableDay): { hours: number; problem?: string } {
    if (!day.start && !day.end) return { hours: 0 };
    if (!day.start || !day.end) return { hours: 0, problem: "Fill in both times, or clear both." };

    const start = toMinutes(day.start);
    const end = toMinutes(day.end);
    if (start === null || end === null) return { hours: 0, problem: "Use the format 08:00." };
    if (end <= start) return { hours: 0, problem: "End must be after start." };

    return { hours: Math.round(((end - start) / 60) * 100) / 100 };
}

/** A short message if the week can't be saved yet, otherwise null. The server checks again. */
export function scheduleProblem(days: EditableDay[]): string | null {
    for (const [index, day] of days.entries()) {
        const { problem } = dayHours(day);
        if (problem) return `${weekdayName(index)}: ${problem}`;
    }
    if (days.every((day) => dayHours(day).hours === 0)) return "Add at least one working day.";
    return null;
}

/** What we send to the API: empty times become null. */
export function toInput(days: EditableDay[]): ScheduleDayInput[] {
    return days.map((day) => ({ start: day.start || null, end: day.end || null }));
}

/** Fill the form from an existing schedule, so a small change doesn't mean retyping the week. */
export function toEditable(schedule: Schedule): EditableDay[] {
    return [1, 2, 3, 4, 5].map((weekday) => {
        const day = schedule.days.find((d) => d.weekday === weekday);
        return { start: day?.start ?? "", end: day?.end ?? "" };
    });
}