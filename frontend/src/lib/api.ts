import type { Receipt, CreateReceiptInput } from "../types/receipt";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000/api";

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });

  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(body.error ?? `Request failed: ${res.status}`);
  }

  if (res.status === 204) return undefined as T;
  return res.json();
}

export const receiptsApi = {
  getAll: (params?: { from?: string; to?: string }) => {
    const query = params?.from && params?.to ? `?from=${params.from}&to=${params.to}` : "";
    return request<Receipt[]>(`/receipts${query}`);
  },
  getById: (id: string) => request<Receipt>(`/receipts/${id}`),
  create: (input: CreateReceiptInput) =>
    request<Receipt>("/receipts", { method: "POST", body: JSON.stringify(input) }),
  remove: (id: string) => request<void>(`/receipts/${id}`, { method: "DELETE" }),
};
