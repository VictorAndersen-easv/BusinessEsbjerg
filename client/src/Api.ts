import type { LoginResponse, Role } from "./Types.ts";
import type {
    ChangeScheduleInput,
    CreateEmployeeInput,
    Employee,
    Schedule,
} from "./Types";
import type { AccountBalance } from "./Types";

/**
 * Set to false once the .NET backend has a POST /api/auth/login endpoint.
 * While true, login() never touches the network.
 */
const USE_MOCK = false;
const API_BASE = "http://localhost:5117/api";

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
    const role: Role = local.startsWith("accountant")
        ? "ACCOUNTANT"
        : local.startsWith("owner")
            ? "SYSTEM_OWNER"
            : "EMPLOYEE";

    return {
        token: "mock-token",
        user: {
            accountId: 1,
            employeeId: role === "EMPLOYEE" ? 1 : null,
            name: local.charAt(0).toUpperCase() + local.slice(1),
            email,
            role,
        },
    };
}

import type { HolidayRequest } from "./Types";

const MOCK_REQUESTS: HolidayRequest[] = [
    { requestId: 1, startDate: "2027-02-15", endDate: "2027-02-19", account: "WEEKS_1_5", totalHours: 37,  status: "APPROVED",              isMainHoliday: false },
    { requestId: 2, startDate: "2027-07-05", endDate: "2027-07-23", account: "WEEKS_1_5", totalHours: 111, status: "SUBMITTED",             isMainHoliday: true  },
    { requestId: 3, startDate: "2026-10-23", endDate: "2026-10-23", account: "WEEKS_1_5", totalHours: 7,   status: "APPROVED",              isMainHoliday: false },
    { requestId: 4, startDate: "2027-05-10", endDate: "2027-05-14", account: "WEEK_6",    totalHours: 37,  status: "REJECTED",              isMainHoliday: false },
    { requestId: 5, startDate: "2027-03-08", endDate: "2027-03-09", account: "WEEKS_1_5", totalHours: 15,  status: "CANCELLED_BY_EMPLOYEE", isMainHoliday: false },
];

export async function getMyRequests(): Promise<HolidayRequest[]> {
    return request<HolidayRequest[]>("/requests/mine");
}

export function getEmployees(): Promise<Employee[]> {
    return request<Employee[]>("/employees");
}

export function createEmployee(input: CreateEmployeeInput): Promise<Employee> {
    return request<Employee>("/employees", { method: "POST", body: JSON.stringify(input) });
}

export function getSchedules(employeeId: number): Promise<Schedule[]> {
    return request<Schedule[]>(`/employees/${employeeId}/schedules`);
}

export function changeSchedule(employeeId: number, input: ChangeScheduleInput): Promise<Schedule> {
    return request<Schedule>(`/employees/${employeeId}/schedules`, {
        method: "POST",
        body: JSON.stringify(input),
    });
}

export function getMyBalances(): Promise<AccountBalance[]> {
    return request<AccountBalance[]>("/balances/mine");
}