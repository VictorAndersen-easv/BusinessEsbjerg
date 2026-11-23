// ---------- Login ----------

export type Role = "EMPLOYEE" | "ACCOUNTANT" | "SYSTEM_OWNER";

export interface User {
    accountId: number;
    employeeId: number | null; // null for the accountant and the system owner
    name: string;
    email: string;
    role: Role;
}

export interface LoginResponse {
    token: string;
    user: User;
}

// ---------- Holiday requests ----------

export type AccountCode = "WEEKS_1_5" | "WEEK_6";

export type RequestStatus =
    | "SUBMITTED"
    | "APPROVED"
    | "REJECTED"
    | "CANCELLED_BY_EMPLOYEE"
    | "CANCELLATION_REQUESTED"
    | "CANCELLED"
    | "CANCELLED_BY_EMPLOYER";

export interface HolidayRequest {
    requestId: number;
    startDate: string; // "2027-02-15" (just the date, as text)
    endDate: string;
    account: AccountCode;
    totalHours: number;
    status: RequestStatus;
    isMainHoliday: boolean;
}

// ---------- Employees and schedules ----------

export interface Employee {
    employeeId: number;
    employeeNo: string;
    name: string;
    email: string;
    hireDate: string; // "2025-09-01"
    terminationDate: string | null;
    currentWeeklyHours: number | null;
}

export interface ScheduleDayInput {
    start: string | null; // "08:00"
    end: string | null;
}

export interface ScheduleDay {
    weekday: number; // 1 = Monday ... 5 = Friday
    start: string | null;
    end: string | null;
    hours: number;
}

export interface Schedule {
    scheduleId: number;
    validFrom: string;
    validTo: string | null; // null = still in use
    weeklyHours: number;
    days: ScheduleDay[];
}

export interface CreateEmployeeInput {
    name: string;
    email: string;
    temporaryPassword: string;
    hireDate: string;
    days: ScheduleDayInput[];
}

export interface ChangeScheduleInput {
    startsOn: string;
    days: ScheduleDayInput[];
}