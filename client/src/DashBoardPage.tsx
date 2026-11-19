import { useAuth } from "./AuthContext";
import type { Role } from "./Types";
import RequestsTable from "./RequestsTable.tsx"

const ROLE_LABEL: Record<Role, string> = {
    EMPLOYEE: "Employee",
    ACCOUNTING: "Accounting",
    ADMIN: "Administrator",
};

export default function DashboardPage() {
    const { user, logout } = useAuth();
    if (!user) return null;

    return (
        <div className="app-shell">
            <header className="topbar">
                <strong>Business Esbjerg</strong>
                <div className="topbar-right">
                    <span>{user.name}</span>
                    <span className="badge">{ROLE_LABEL[user.role]}</span>
                    <button onClick={logout}>Sign out</button>
                </div>
            </header>

            <main className="content">
                <h1>Welcome, {user.name}</h1>
                <section>
                    <h2>My holiday requests</h2>
                    <RequestsTable />
                </section>
            </main>
        </div>
    );
}