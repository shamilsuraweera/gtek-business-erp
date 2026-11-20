const BASE = '/api/v1'
const TOKEN_KEY = 'erp.token'
const COMPANY_KEY = 'erp.company'

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

function read(key: string, storage: Storage): string | null {
  try { return storage.getItem(key) } catch { return null }
}
function write(key: string, value: string | null, storage: Storage) {
  try { value === null ? storage.removeItem(key) : storage.setItem(key, value) } catch { /* storage unavailable */ }
}

export const session = {
  get token() { return read(TOKEN_KEY, sessionStorage) },
  set token(value: string | null) { write(TOKEN_KEY, value, sessionStorage) },
  get companyId() { return read(COMPANY_KEY, localStorage) },
  set companyId(value: string | null) { write(COMPANY_KEY, value, localStorage) },
}

type Options = { method?: string; body?: unknown; company?: boolean }

/** Thin fetch wrapper: adds the bearer token and X-Company-Id, and turns problem-details into ApiError. */
export async function api<T>(path: string, { method = 'GET', body, company = true }: Options = {}): Promise<T> {
  const headers: Record<string, string> = {}
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (session.token) headers.Authorization = `Bearer ${session.token}`
  if (company && session.companyId) headers['X-Company-Id'] = session.companyId

  const response = await fetch(`${BASE}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  if (response.status === 401) {
    window.dispatchEvent(new Event('erp:unauthorized'))
    throw new ApiError(401, 'Your session has expired. Please sign in again.')
  }
  if (!response.ok) {
    let message = `Request failed (${response.status})`
    try {
      const problem = await response.json()
      message = problem.detail ?? problem.title ?? message
      if (problem.errors) message = Object.values(problem.errors as Record<string, string[]>).flat().join(' ')
    } catch { /* non-JSON error body */ }
    throw new ApiError(response.status, message)
  }
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}
