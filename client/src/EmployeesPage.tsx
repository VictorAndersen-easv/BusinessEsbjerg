import { useEffect, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { getEmployees } from "./Api";
import { useAuth } from "./AuthContext";
import DataTable, { type Column } from "./DataTable";
import { formatDate, formatHours } from "./Format";
import type { Employee } from "./Types";
import "./Pages.css";

const columns: Column<Employee>[] = [
    { header: "No.", cell: (e) => e.employeeNo },
    { header: "Name", cell: (e) => e.name },
    { header: "Email", cell: (e) => e.email },
    { header: "Hired", cell: (e) => formatDate(e.hireDate) },
    {
        header: "Hours / week",
        align: "right",
        cell: (e) => (e.currentWeeklyHours === null ? "–" : formatHours(e.currentWeeklyHours)),
    },
    { header: "Schedule", cell: (e) => <Link to={`/employees/${e.employeeId}`}>View / change</Link> },
];

export default function EmployeesPage() {
    const { user } = useAuth();
    const location = useLocation();
    // NewEmployeePage sends the name of the employee it just created along with the redirect
    const created = (location.state as { created?: string } | null)?.created;

    const [employees, setEmployees] = useState<Employee[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;

        getEmployees()
            .then((data) => {
                if (!cancelled) setEmployees(data);
            })
            .catch(() => {
                if (!cancelled) setError("Could not load the employees. Please try again.");
            })
            .finally(() => {
                if (!cancelled) setLoading(false);
            });

        return () => {
            cancelled = true;
        };
    }, []);

    return (
        <>
            <div className="page-header">
                <h1>Employees</h1>
                {user?.role === "SYSTEM_OWNER" && (
                    <Link to="/employees/new" className="button-link primary-link">
                        New employee
                    </Link>
                )}
            </div>

            {created && <p className="notice success">Created {created}.</p>}

            {error ? (
                <p className="table-error" role="alert">
                    {error}
                </p>
            ) : (
                <DataTable
                    caption="Employees"
                    columns={columns}
                    rows={employees}
                    getRowKey={(e) => e.employeeId}
                    loading={loading}
                    emptyMessage="There are no employees yet."
                />
            )}
        </>
    );
}