import type { ReactNode } from "react";
import "./Tables.css";

/** Describes ONE column: its title and how to show a cell for a given row. */
export interface Column<T> {
    header: string;
    cell: (row: T) => ReactNode;
    align?: "left" | "right";
}

interface DataTableProps<T> {
    columns: Column<T>[];
    rows: T[];
    /** Something unique per row (usually the id), so React can track rows. */
    getRowKey: (row: T) => string | number;
    /** Hidden caption for screen readers, e.g. "My holiday requests". */
    caption?: string;
    loading?: boolean;
    emptyMessage?: string;
}

/**
 * A table that knows nothing about holidays. You give it columns and rows,
 * and it draws them. <T> means "whatever type of row you pass in".
 */
export default function DataTable<T>({
                                         columns,
                                         rows,
                                         getRowKey,
                                         caption,
                                         loading = false,
                                         emptyMessage = "Nothing to show yet.",
                                     }: DataTableProps<T>) {
    const alignClass = (c: Column<T>) => (c.align === "right" ? "num" : undefined);

    return (
        <div className="table-wrap">
            <table className="data-table">
                {caption && <caption className="sr-only">{caption}</caption>}
                <thead>
                <tr>
                    {columns.map((c) => (
                        <th key={c.header} scope="col" className={alignClass(c)}>
                            {c.header}
                        </th>
                    ))}
                </tr>
                </thead>
                <tbody>
                {loading ? (
                    <tr>
                        <td colSpan={columns.length} className="table-message">
                            Loading…
                        </td>
                    </tr>
                ) : rows.length === 0 ? (
                    <tr>
                        <td colSpan={columns.length} className="table-message">
                            {emptyMessage}
                        </td>
                    </tr>
                ) : (
                    rows.map((row) => (
                        <tr key={getRowKey(row)}>
                            {columns.map((c) => (
                                <td key={c.header} className={alignClass(c)}>
                                    {c.cell(row)}
                                </td>
                            ))}
                        </tr>
                    ))
                )}
                </tbody>
            </table>
        </div>
    );
}