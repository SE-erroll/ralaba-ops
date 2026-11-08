export interface Session {
  token(): string | null;
  subscribe(listener: () => void): () => void;
  clear(): void;
  signIn(email: string, password: string): Promise<void>;
  signOut(): Promise<void>;
}
export function createSession(options: {
  url: string; key: string; fetcher?: typeof fetch; now?: () => number;
}): Session;
