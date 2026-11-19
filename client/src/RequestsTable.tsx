import { useEffect, useState } from "react";
import { getMyRequests } from "./Api";
import DataTable, { type Column } from "./DataTable.tsx";
import type { AccountCode, HolidayRequest, RequestStatus } from "./Types";

const ACCOUNT_LABEL: Record<AccountCode, string> = {
    WEEKS_1_5: "Weeks 1–5",
    WEEK_6: "Week 6",
};

const STATUS_LABEL: Record<RequestStatus, string> = {
    SUBMITTED: "Pending",
    APPROVED: "Approved",
    REJECTED: "Rejected",
    CANCELLED_BY_EMPLOYEE: "Cancelled by you",
    CANCELLED_BY_EMPLOYER: "Cancelled by employer",
};

// "2027-02-15" -> "15 Feb 2027". The "T00:00:00" makes the browser treat it as a
// local date; without it, some time zones would show the day before.
function formatDate(iso: string): string {
    return new Date(`${iso}T00:00:00`).toLocaleDateString("en-GB", {
        day: "numeric",
        month: "short",
        year: "numeric",
    });
}

function formatHours(hours: number): string {
    return `${hours.toLocaleString("en-GB", { maximumFractionDigits: 2 })} h`;
}

const columns: Column<HolidayRequest>[] = [
    {
        header: "Period",
        cell: (r) => (
            <>
                {r.startDate === r.endDate
                    ? formatDate(r.startDate)
                    : `${formatDate(r.startDate)} – ${formatDate(r.endDate)}`}
                {r.isMainHoliday && <span className="tag">Main holiday</span>}
            </>
        ),
    },
    { header: "Account", cell: (r) => ACCOUNT_LABEL[r.account] },
    { header: "Hours", align: "right", cell: (r) => formatHours(r.totalHours) },
    {
        header: "Status",
        cell: (r) => (
            <span className={`status status-${r.status.toLowerCase()}`}>
        {STATUS_LABEL[r.status]}
      </span>
        ),
    },
];

export default function RequestsTable() {
    const [requests, setRequests] = useState<HolidayRequest[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    // Runs once when the table appears on screen
    useEffect(() => {
        let cancelled = false; // ignore the answer if the page was left in the meantime

        getMyRequests()
            .then((data) => {
                if (!cancelled) setRequests(data);
            })
            .catch(() => {
                if (!cancelled) setError("Could not load your requests. Please try again.");
            })
            .finally(() => {
                if (!cancelled) setLoading(false);
            });

        return () => {
            cancelled = true;
        };
    }, []);

    if (error) {
        return (
            <p className="table-error" role="alert">
                {error}
            </p>
        );
    }

    return (
        <DataTable
            caption="My holiday requests"
            columns={columns}
            rows={requests}
            getRowKey={(r) => r.requestId}
            loading={loading}
            emptyMessage="You haven't requested any holiday yet."
        />
    );
}