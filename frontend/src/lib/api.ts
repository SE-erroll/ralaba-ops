import type { Receipt, CreateReceiptInput } from "../types/receipt";
import { auth } from "./auth";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000/api";

export async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const token = auth.token();
  if (!token) throw new Error("Please sign in to view receipts.");
  const headers = new Headers(options?.headers);
  headers.set("Content-Type", "application/json");
  headers.set("Authorization", `Bearer ${token}`);
  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...options,
    headers,
  });

  if (res.status === 401) {
    auth.clear();
    throw new Error("Your session ended. Please sign in again.");
  }
  if (res.status === 403) throw new Error("Your account does not have staff access. Please contact the owner.");

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
