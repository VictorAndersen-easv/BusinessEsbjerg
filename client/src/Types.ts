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