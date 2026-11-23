import {
    createContext,
    useCallback,
    useContext,
    useMemo,
    useState,
    type ReactNode,
} from "react";
import * as api from "./Api.ts";
import type { LoginResponse, User } from "./Types";

const STORAGE_KEY = "be.auth";

interface AuthContextValue {
    user: User | null;
    login: (email: string, password: string) => Promise<void>;
    logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function loadStored(): LoginResponse | null {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        return raw ? (JSON.parse(raw) as LoginResponse) : null;
    } catch {
        return null;
    }
}

export function AuthProvider({ children }: { children: ReactNode }) {
    const [user, setUser] = useState<User | null>(() => {
        const stored = loadStored();
        api.setAuthToken(stored?.token ?? null);
        return stored?.user ?? null;
    });

    const login = useCallback(async (email: string, password: string) => {
        const result = await api.login(email, password);
        api.setAuthToken(result.token);
        localStorage.setItem(STORAGE_KEY, JSON.stringify(result));
        setUser(result.user);
    }, []);

    const logout = useCallback(() => {
        api.setAuthToken(null);
        localStorage.removeItem(STORAGE_KEY);
        setUser(null);
    }, []);

    const value = useMemo(() => ({ user, login, logout }), [user, login, logout]);

    return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
    const ctx = useContext(AuthContext);
    if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
    return ctx;
}