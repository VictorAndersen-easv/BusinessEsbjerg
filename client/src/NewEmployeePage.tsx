import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ApiError, createEmployee } from "./Api";
import { toIsoDate } from "./Format";
import ScheduleEditor from "./ScheduleEditor.tsx";
import { defaultWeek, scheduleProblem, toInput, type EditableDay } from "./ScheduleHelpers.ts";
import "./Pages.css";

/** System owner only: creates the employee, their login and their first weekly schedule in one go. */
export default function NewEmployeePage() {
    const navigate = useNavigate();

    const [name, setName] = useState("");
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [hireDate, setHireDate] = useState(toIsoDate(new Date()));
    const [days, setDays] = useState<EditableDay[]>(defaultWeek());

    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const problem = scheduleProblem(days);

    async function handleSubmit(e: FormEvent<HTMLFormElement>) {
        e.preventDefault();
        setError(null);
        setSubmitting(true);

        try {
            const created = await createEmployee({
                name: name.trim(),
                email: email.trim(),
                temporaryPassword: password,
                hireDate,
                days: toInput(days),
            });
            navigate("/employees", { state: { created: `${created.name} (${created.employeeNo})` } });
        } catch (err) {
            // The server's messages ("must end with @businessesbjerg.com", ...) are written to be shown as they are
            setError(err instanceof ApiError ? err.message : "Could not reach the server. Please try again.");
            setSubmitting(false);
        }
    }

    return (
        <>
            <div className="page-header">
                <h1>New employee</h1>
                <Link to="/employees">Back to employees</Link>
            </div>

            <form className="form-card" onSubmit={handleSubmit}>
                <div className="form-grid">
                    <div className="field">
                        <label htmlFor="name">Full name</label>
                        <input id="name" value={name} onChange={(e) => setName(e.target.value)} required />
                    </div>

                    <div className="field">
                        <label htmlFor="email">Email</label>
                        <input
                            id="email"
                            type="email"
                            placeholder="name@businessesbjerg.com"
                            value={email}
                            onChange={(e) => setEmail(e.target.value)}
                            required
                        />
                    </div>

                    <div className="field">
                        <label htmlFor="hireDate">Hire date</label>
                        <input
                            id="hireDate"
                            type="date"
                            value={hireDate}
                            onChange={(e) => setHireDate(e.target.value)}
                            required
                        />
                        <span className="hint">The first schedule starts on the Monday of this week.</span>
                    </div>

                    <div className="field">
                        <label htmlFor="password">Temporary password</label>
                        <input
                            id="password"
                            type="text"
                            autoComplete="off"
                            value={password}
                            onChange={(e) => setPassword(e.target.value)}
                            minLength={8}
                            required
                        />
                        <span className="hint">At least 8 characters. Tell the employee what it is.</span>
                    </div>
                </div>

                <ScheduleEditor days={days} onChange={setDays} disabled={submitting} />

                <div className="error" role="alert" aria-live="polite">
                    {error}
                </div>

                <div className="form-actions">
                    <button type="submit" className="primary" disabled={submitting || problem !== null}>
                        {submitting ? "Creating…" : "Create employee"}
                    </button>
                    {problem && <span className="hint">{problem}</span>}
                </div>
            </form>
        </>
    );
}