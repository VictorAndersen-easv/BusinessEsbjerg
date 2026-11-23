import { weekdayName } from "./Format";
import { dayHours, type EditableDay } from "./ScheduleHelpers.ts";
import "./Pages.css";

interface ScheduleEditorProps {
    days: EditableDay[];
    onChange: (days: EditableDay[]) => void;
    disabled?: boolean;
}

/**
 * Five rows (Monday-Friday) with a start and end time each. It shows the hours per day
 * and the weekly total while you type. The parent owns the data ("controlled component"):
 * this component only shows `days` and calls `onChange` with a new copy when something changes.
 */
export default function ScheduleEditor({ days, onChange, disabled = false }: ScheduleEditorProps) {
    function update(index: number, patch: Partial<EditableDay>) {
        onChange(days.map((day, i) => (i === index ? { ...day, ...patch } : day)));
    }

    function copyMondayToAll() {
        const monday = days[0];
        if (monday) onChange(days.map(() => ({ ...monday })));
    }

    const total = days.reduce((sum, day) => sum + dayHours(day).hours, 0);

    return (
        <fieldset className="schedule-editor" disabled={disabled}>
            <legend>Weekly schedule</legend>

            <table>
                <thead>
                <tr>
                    <th scope="col">Day</th>
                    <th scope="col">Start</th>
                    <th scope="col">End</th>
                    <th scope="col" className="hours">Hours</th>
                    <th scope="col"><span className="sr-only">Actions</span></th>
                </tr>
                </thead>
                <tbody>
                {days.map((day, index) => {
                    const name = weekdayName(index);
                    const { hours, problem } = dayHours(day);

                    return (
                        <tr key={name}>
                            <th scope="row">{name}</th>
                            <td>
                                <input
                                    type="time"
                                    aria-label={`${name} start`}
                                    value={day.start}
                                    onChange={(e) => update(index, { start: e.target.value })}
                                />
                            </td>
                            <td>
                                <input
                                    type="time"
                                    aria-label={`${name} end`}
                                    value={day.end}
                                    onChange={(e) => update(index, { end: e.target.value })}
                                />
                            </td>
                            <td className="hours">
                                {problem ? (
                                    <span className="error-text">{problem}</span>
                                ) : hours === 0 ? (
                                    "Day off"
                                ) : (
                                    hours.toFixed(2)
                                )}
                            </td>
                            <td>
                                <button
                                    type="button"
                                    className="link-button"
                                    onClick={() => update(index, { start: "", end: "" })}
                                >
                                    Clear
                                </button>
                            </td>
                        </tr>
                    );
                })}
                </tbody>
                <tfoot>
                <tr>
                    <td colSpan={3}>Total per week</td>
                    <td className="hours">{total.toFixed(2)} h</td>
                    <td />
                </tr>
                </tfoot>
            </table>

            <button type="button" className="link-button" onClick={copyMondayToAll}>
                Copy Monday to all weekdays
            </button>
        </fieldset>
    );
}