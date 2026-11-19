export type Role = "EMPLOYEE" | "ACCOUNTING" | "ADMIN";

export interface User {
    employeeId: number;
    name: string;
    email: string;
    role: Role;
}

export interface LoginResponse {
    token: string;
    user: User;
}

export type AccountCode = "WEEKS_1_5" | "WEEK_6";

export type RequestStatus =
    | "SUBMITTED"
    | "APPROVED"
    | "REJECTED"
    | "CANCELLED_BY_EMPLOYEE"
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