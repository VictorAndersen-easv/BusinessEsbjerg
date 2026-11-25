import { useEffect, useState } from "react";
import { getMyBalances } from "./Api";
import { accountLabel, formatDate, formatDays, formatHours, toIsoDate } from "./Format";
import type { AccountBalance, BucketBalance } from "./Types";
import "./Balances.css";

function bucketNote(bucket: BucketBalance, today: string): string {
    return bucket.usageStart > today
        ? `Available from ${formatDate(bucket.usageStart)}`
        : `Use by ${formatDate(bucket.usageEnd)}`;
}

/** The first date on which some of the remaining holiday runs out, if any is left. */
function nextExpiry(account: AccountBalance, today: string): string | null {
    const dates = account.buckets
        .filter((b) => b.remainingDays > 0 && b.usageEnd >= today)
        .map((b) => b.usageEnd)
        .sort(); // "YYYY-MM-DD" strings sort in date order
    return dates[0] ?? null;
}

/** One card per holiday account (weeks 1-5 and week 6) with what the employee can use. */
export default function BalanceCards() {
    const [balances, setBalances] = useState<AccountBalance[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;

        getMyBalances()
            .then((data) => {
                if (!cancelled) setBalances(data);
            })
            .catch(() => {
                if (!cancelled) setError("Could not load your balance. Please try again.");
            })
            .finally(() => {
                if (!cancelled) setLoading(false);
            });

        return () => {
            cancelled = true;
        };
    }, []);

    const today = toIsoDate(new Date());

    if (error) {
        return (
            <p className="table-error" role="alert">
                {error}
            </p>
        );
    }
    if (loading) return <p className="hint">Loading…</p>;
    if (balances.length === 0) {
        return <p className="hint">No holiday has been earned yet. It is added at the end of every month.</p>;
    }

    return (
        <div className="balance-grid">
            {balances.map((account) => {
                const expiry = nextExpiry(account, today);
                const notUsableYet = Math.round((account.totalDays - account.availableDays) * 100) / 100;

                return (
                    <article key={account.account} className="balance-card">
                        <h3>{accountLabel(account.account)}</h3>

                        <p className="balance-main">
              <span className={account.availableDays < 0 ? "negative" : undefined}>
                {formatDays(account.availableDays)}
              </span>
                            <span className="balance-sub">{formatHours(account.availableHours)}</span>
                        </p>
                        <p className="hint">available now</p>

                        {notUsableYet > 0 && (
                            <p className="hint">+ {formatDays(notUsableYet)} earned, not usable yet</p>
                        )}
                        {expiry && <p className="expiry">Earliest expiry: {formatDate(expiry)}</p>}

                        <ul className="bucket-list">
                            {account.buckets.map((bucket) => (
                                <li key={bucket.bucketId}>
                                    <strong>
                                        {formatDate(bucket.earningStart)} – {formatDate(bucket.earningEnd)}
                                    </strong>
                                    <span>
                    {formatDays(bucket.remainingDays)} ({formatHours(bucket.remainingHours)}) left of{" "}
                                        {formatDays(bucket.earnedDays)}
                  </span>
                                    <span className="hint">{bucketNote(bucket, today)}</span>
                                </li>
                            ))}
                        </ul>
                    </article>
                );
            })}
        </div>
    );
}