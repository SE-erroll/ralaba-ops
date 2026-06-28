import { useEffect, useState } from "react";
import type { Receipt } from "../types/receipt";
import { receiptsApi } from "../lib/api";
import ReceiptCard from "../components/ReceiptCard";

export default function ReceiptsPage() {
  const [receipts, setReceipts] = useState<Receipt[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    receiptsApi
      .getAll()
      .then(setReceipts)
      .catch((err) => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="p-6">Loading receipts...</div>;
  if (error) return <div className="p-6 text-red-600">Error: {error}</div>;

  return (
    <div className="p-6">
      <h1 className="text-2xl font-semibold">Receipts</h1>
      <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {receipts.map((r) => (
          <ReceiptCard key={r.id} receipt={r} />
        ))}
      </div>
    </div>
  );
}
