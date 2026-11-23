const WEEKDAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];
const WEEKDAYS_SHORT = ["Mon", "Tue", "Wed", "Thu", "Fri"];

/** index 0 = Monday ... 4 = Friday */
export function weekdayName(index: number): string {
    return WEEKDAYS[index] ?? `Day ${index + 1}`;
}

export function weekdayShort(index: number): string {
    return WEEKDAYS_SHORT[index] ?? `D${index + 1}`;
}

// "2027-02-15" -> "15 Feb 2027". The "T00:00:00" makes the browser treat it as a
// local date; without it, some time zones would show the day before.
export function formatDate(iso: string): string {
    return new Date(`${iso}T00:00:00`).toLocaleDateString("en-GB", {
        day: "numeric",
        month: "short",
        year: "numeric",
    });
}

export function formatHours(hours: number): string {
    return `${hours.toLocaleString("en-GB", { maximumFractionDigits: 2 })} h`;
}

/** A Date as "YYYY-MM-DD" in the user's own time zone (toISOString would use UTC). */
export function toIsoDate(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, "0");
    const day = String(date.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
}

/** The first Monday after today, as "YYYY-MM-DD". */
export function nextMonday(from: Date = new Date()): string {
    const date = new Date(from);
    const daysToAdd = ((8 - date.getDay()) % 7) || 7; // getDay(): Sunday = 0, Monday = 1
    date.setDate(date.getDate() + daysToAdd);
    return toIsoDate(date);
}

export function isMonday(iso: string): boolean {
    return iso !== "" && new Date(`${iso}T00:00:00`).getDay() === 1;
}