import type { LoginResponse, Role } from "./Types.ts";

/**
 * Set to false once the .NET backend has a POST /api/auth/login endpoint.
 * While true, login() never touches the network.
 */
const USE_MOCK = false;
const API_BASE = "http://localhost:<port>/api";

export class ApiError extends Error {
    status: number;

    constructor(status: number, message: string) {
        super(message);
        this.status = status;
    }
}

let authToken: string | null = null;

export function setAuthToken(token: string | null) {
    authToken = token;
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
    const headers = new Headers(options.headers);
    headers.set("Content-Type", "application/json");
    if (authToken) headers.set("Authorization", `Bearer ${authToken}`);

    const res = await fetch(`${API_BASE}${path}`, { ...options, headers });

    if (!res.ok) {
        // ASP.NET Core returns RFC 7807 ProblemDetails: { title, detail, status }
        let message = res.statusText || "Something went wrong.";
        try {
            const body = await res.json();
            message = body.detail ?? body.title ?? message;
        } catch {
            /* response had no JSON body */
        }
        throw new ApiError(res.status, message);
    }

    if (res.status === 204) return undefined as T;
    return (await res.json()) as T;
}

export function login(email: string, password: string): Promise<LoginResponse> {
    if (USE_MOCK) return mockLogin(email, password);
    return request<LoginResponse>("/auth/login", {
        method: "POST",
        body: JSON.stringify({ email, password }),
    });
}

// ---------------------------------------------------------------------
// Mock backend: password is "demo". The email decides the role:
//   accounting@... -> ACCOUNTING, admin@... -> ADMIN, anything else -> EMPLOYEE
// ---------------------------------------------------------------------
async function mockLogin(email: string, password: string): Promise<LoginResponse> {
    await new Promise((resolve) => setTimeout(resolve, 600));

    if (password !== "demo") {
        throw new ApiError(401, "Incorrect email or password.");
    }

    const local = email.split("@")[0] ?? "user";
    const role: Role = local.startsWith("accounting")
        ? "ACCOUNTING"
        : local.startsWith("admin")
            ? "ADMIN"
            : "EMPLOYEE";

    return {
        token: "mock-token",
        user: {
            employeeId: 1,
            name: local.charAt(0).toUpperCase() + local.slice(1),
            email,
            role,
        },
    };
}