import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "./AuthContext";
import type { Role } from "./Types";

/**
 * Only lets the listed roles into the pages inside it; everyone else goes back to the start page.
 * This is for a tidy experience only. The real protection is on the server, which checks the
 * role in the token on every request.
 */
export default function RequireRole({ allow }: { allow: Role[] }) {
  const { user } = useAuth();

  if (!user || !allow.includes(user.role)) {
    return <Navigate to="/" replace />;
  }
  return <Outlet />;
}