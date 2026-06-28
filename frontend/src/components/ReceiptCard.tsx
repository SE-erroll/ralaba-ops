import type { Receipt } from "../types/receipt";

interface Props {
  receipt: Receipt;
}

export default function ReceiptCard({ receipt }: Props) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
      <div className="flex items-center justify-between">
        <span className="font-medium">{receipt.customer ?? "Walk-in"}</span>
        <span
          className={`rounded-full px-2 py-0.5 text-xs font-medium ${
            receipt.paymentMethod === "Cash"
              ? "bg-green-100 text-green-700"
              : "bg-blue-100 text-blue-700"
          }`}
        >
          {receipt.paymentMethod}
        </span>
      </div>
      <p className="mt-1 text-2xl font-semibold">
        ₱{receipt.amount.toLocaleString()}
      </p>
      <p className="mt-1 text-sm text-gray-500">
        {new Date(receipt.date).toLocaleString()}
        {receipt.employee ? ` · ${receipt.employee}` : ""}
      </p>
    </div>
  );
}
