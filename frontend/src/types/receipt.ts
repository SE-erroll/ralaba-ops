export type PaymentMethod = "Cash" | "GCash";
export type TransactionType = "Sale" | "Refund" | "Adjustment";

export interface Receipt {
  id: string;
  receiptNo: string | null;
  date: string;
  employee: string | null;
  customer: string | null;
  type: TransactionType;
  amount: number;
  paymentMethod: PaymentMethod;
  paid: boolean;
  paidDate: string | null;
  notes: string | null;
}

export interface CreateReceiptInput {
  date: string;
  amount: number;
  paymentMethod: PaymentMethod;
  receiptNo?: string;
  employee?: string;
  customer?: string;
  type?: TransactionType;
  paid?: boolean;
  paidDate?: string;
  notes?: string;
}
