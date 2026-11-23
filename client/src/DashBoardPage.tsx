import { useAuth } from "./AuthContext";
import RequestsTable from "./RequestsTable";

/** The start page. The top bar now lives in AppLayout. */
export default function DashBoardPage() {
    const { user } = useAuth();
    if (!user) return null;

    return (
        <>
            <h1>Welcome, {user.name}</h1>

            {user.role === "EMPLOYEE" ? (
                <section>
                    <h2>My holiday requests</h2>
                    <RequestsTable />
                </section>
            ) : (
                <p>Use the menu to manage employees and their working schedules. Approving holiday comes next.</p>
            )}
        </>
    );
}