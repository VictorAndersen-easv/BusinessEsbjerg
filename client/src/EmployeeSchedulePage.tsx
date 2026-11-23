import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Link, useParams } from "react-router-dom";
import { ApiError, changeSchedule, getEmployees, getSchedules } from "./Api";
import DataTable, { type Column } from "./DataTable";
import { formatDate, formatHours, isMonday, nextMonday, weekdayShort } from "./Format";
import ScheduleEditor from "./ScheduleEditor.tsx";
import { defaultWeek, scheduleProblem, toEditable, toInput, type EditableDay } from "./ScheduleHelpers.ts";
import type { Employee, Schedule } from "./Types";
import "./Pages.css";

const columns: Column<Schedule>[] = [
    {
        header: "Period",
        cell: (s) => (
            <>
                {formatDate(s.validFrom)} – {s.validTo ? formatDate(s.validTo) : "now"}
                {s.validTo === null && <span className="tag">Current</span>}
            </>
        ),
    },
    { header: "Hours / week", align: "right", cell: (s) => formatHours(s.weeklyHours) },
    {
        header: "Hours per day",
        cell: (s) =>
            s.days.map((d) => `${weekdayShort(d.weekday - 1)} ${d.hours > 0 ? d.hours : "off"}`).join(" · "),
    },
];

/** Accountant or system owner: the schedule history of one employee, and a form for a new schedule. */
export default function EmployeeSchedulePage() {
    const employeeId = Number(useParams().employeeId);

    const [employee, setEmployee] = useState<Employee | null>(null);
    const [schedules, setSchedules] = useState<Schedule[]>([]);
    const [loading, setLoading] = useState(true);
    const [loadError, setLoadError] = useState<string | null>(null);

    const [startsOn, setStartsOn] = useState(nextMonday());
    const [days, setDays] = useState<EditableDay[]>(defaultWeek());
    const [submitting, setSubmitting] = useState(false);
    const [formError, setFormError] = useState<string | null>(null);
    const [saved, setSaved] = useState<string | null>(null);

    const fetchAll = useCallback(async () => {
        const [employees, history] = await Promise.all([getEmployees(), getSchedules(employeeId)]);
        return { employee: employees.find((e) => e.employeeId === employeeId) ?? null, history };
    }, [employeeId]);

    useEffect(() => {
        let cancelled = false;

        fetchAll()
            .then(({ employee, history }) => {
                if (cancelled) return;
                setEmployee(employee);
                setSchedules(history);
                // Start the form from the schedule in use now, so a small change is a small edit
                const current = history.find((s) => s.validTo === null);
                if (current) setDays(toEditable(current));
            })
            .catch((err) => {
                if (cancelled) return;
                setLoadError(err instanceof ApiError && err.status === 404 ? "Employee not found." : "Could not load the schedules.");
            })
            .finally(() => {
                if (!cancelled) setLoading(false);
            });

        return () => {
            cancelled = true;
        };
    }, [fetchAll]);

    const problem = scheduleProblem(days);
    const startProblem = startsOn !== "" && !isMonday(startsOn) ? "The new schedule must start on a Monday." : null;

    async function handleSubmit(e: FormEvent<HTMLFormElement>) {
        e.preventDefault();
        setFormError(null);
        setSaved(null);
        setSubmitting(true);

        try {
            await changeSchedule(employeeId, { startsOn, days: toInput(days) });
            setSchedules(await getSchedules(employeeId));
            setSaved(`New schedule saved. It starts on ${formatDate(startsOn)}.`);
        } catch (err) {
            setFormError(err instanceof ApiError ? err.message : "Could not reach the server. Please try again.");
        } finally {
            setSubmitting(false);
        }
    }

    if (loadError) {
        return (
            <>
                <p className="table-error" role="alert">{loadError}</p>
                <Link to="/employees">Back to employees</Link>
            </>
        );
    }

    return (
        <>
            <div className="page-header">
                <h1>Schedule{employee ? `: ${employee.name}` : ""}</h1>
                <Link to="/employees">Back to employees</Link>
            </div>

            <h2>History</h2>
            <DataTable
                caption="Schedule history"
                columns={columns}
                rows={schedules}
                getRowKey={(s) => s.scheduleId}
                loading={loading}
                emptyMessage="No schedules yet."
            />

            <form className="form-card" onSubmit={handleSubmit}>
                <h2>New schedule</h2>
                <p className="hint">
                    The current schedule ends the Sunday before the new one starts. Nothing is deleted: the old
                    schedule stays in the history above.
                </p>

                <div className="form-grid">
                    <div className="field">
                        <label htmlFor="startsOn">Starts on (a Monday)</label>
                        <input
                            id="startsOn"
                            type="date"
                            value={startsOn}
                            onChange={(e) => setStartsOn(e.target.value)}
                            required
                        />
                        {startProblem && <span className="error-text">{startProblem}</span>}
                    </div>
                </div>

                <ScheduleEditor days={days} onChange={setDays} disabled={submitting} />

                {saved && <p className="notice success">{saved}</p>}
                <div className="error" role="alert" aria-live="polite">
                    {formError}
                </div>

                <div className="form-actions">
                    <button
                        type="submit"
                        className="primary"
                        disabled={submitting || problem !== null || startProblem !== null}
                    >
                        {submitting ? "Saving…" : "Save new schedule"}
                    </button>
                    {(problem ?? startProblem) && <span className="hint">{problem ?? startProblem}</span>}
                </div>
            </form>
        </>
    );
}