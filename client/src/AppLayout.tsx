import { NavLink, Outlet } from "react-router-dom";
import { useAuth } from "./AuthContext";
import type { Role } from "./Types";
import "./Pages.css";

const ROLE_LABEL: Record<Role, string> = {
    EMPLOYEE: "Employee",
    ACCOUNTANT: "Accountant",
    SYSTEM_OWNER: "System owner",
};

interface NavItem {
    to: string;
    label: string;
    roles: Role[];
    end?: boolean;
}

// Add a line here for every new page that should appear in the menu
const NAV: NavItem[] = [
    { to: "/", label: "Overview", roles: ["EMPLOYEE", "ACCOUNTANT", "SYSTEM_OWNER"], end: true },
    { to: "/employees", label: "Employees", roles: ["ACCOUNTANT", "SYSTEM_OWNER"] },
];

/** The top bar with the menu, shared by every page after login. The page itself appears at <Outlet />. */
export default function AppLayout() {
    const { user, logout } = useAuth();
    if (!user) return null;

    return (
        <div className="app-shell">
            <header className="topbar">
                <strong>Business Esbjerg</strong>

                <nav className="main-nav" aria-label="Main">
                    {NAV.filter((item) => item.roles.includes(user.role)).map((item) => (
                        <NavLink
                            key={item.to}
                            to={item.to}
                            end={item.end}
                            className={({ isActive }) => (isActive ? "nav-link active" : "nav-link")}
                        >
                            {item.label}
                        </NavLink>
                    ))}
                </nav>

                <div className="topbar-right">
                    <span>{user.name}</span>
                    <span className="badge">{ROLE_LABEL[user.role]}</span>
                    <button onClick={logout}>Sign out</button>
                </div>
            </header>

            <main className="content">
                <Outlet />
            </main>
        </div>
    );
}