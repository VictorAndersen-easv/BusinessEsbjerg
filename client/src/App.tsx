import { BrowserRouter, Navigate, Outlet, Route, Routes, useLocation } from "react-router-dom";
import { AuthProvider, useAuth } from "./AuthContext";
import AppLayout from "./AppLayout";
import DashBoardPage from "./DashBoardPage";
import EmployeeSchedulePage from "./EmployeeSchedulePage";
import EmployeesPage from "./EmployeesPage";
import LoginPage from "./LoginPage";
import NewEmployeePage from "./NewEmployeePage";
import RequireRole from "./RequireRole";
import "./Styles.css";

/** Sends anonymous visitors to /login and remembers where they wanted to go. */
function RequireAuth() {
    const { user } = useAuth();
    const location = useLocation();

    if (!user) {
        return <Navigate to="/login" replace state={{ from: location.pathname }} />;
    }
    return <Outlet />;
}

export default function App() {
    return (
        <BrowserRouter>
            <AuthProvider>
                <Routes>
                    <Route path="/login" element={<LoginPage />} />

                    {/* Everything below needs a login, and gets the top bar from AppLayout */}
                    <Route element={<RequireAuth />}>
                        <Route element={<AppLayout />}>
                            <Route path="/" element={<DashBoardPage />} />

                            <Route element={<RequireRole allow={["ACCOUNTANT", "SYSTEM_OWNER"]} />}>
                                <Route path="/employees" element={<EmployeesPage />} />
                                <Route path="/employees/:employeeId" element={<EmployeeSchedulePage />} />
                            </Route>

                            <Route element={<RequireRole allow={["SYSTEM_OWNER"]} />}>
                                <Route path="/employees/new" element={<NewEmployeePage />} />
                            </Route>
                        </Route>
                    </Route>

                    <Route path="*" element={<Navigate to="/" replace />} />
                </Routes>
            </AuthProvider>
        </BrowserRouter>
    );
}