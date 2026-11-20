import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, session } from './api/client'
import type { Company, LoginResponse, User } from './api/types'

interface AuthState {
  ready: boolean
  user: User | null
  companies: Company[]
  company: Company | null
  permissions: ReadonlySet<string>
  can: (permission: string) => boolean
  login: (userName: string, password: string) => Promise<void>
  logout: () => void
  selectCompany: (id: string) => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [ready, setReady] = useState(false)
  const [user, setUser] = useState<User | null>(null)
  const [companies, setCompanies] = useState<Company[]>([])
  const [permissions, setPermissions] = useState<ReadonlySet<string>>(new Set())
  const [companyId, setCompanyId] = useState<string | null>(session.companyId)

  const logout = useCallback(() => {
    session.token = null
    setUser(null)
    setCompanies([])
    setPermissions(new Set())
    queryClient.clear()
  }, [queryClient])

  const loadSession = useCallback(async () => {
    const [me, codes, myCompanies] = await Promise.all([
      api<User>('/auth/me', { company: false }),
      api<string[]>('/auth/me/permissions', { company: false }),
      api<Company[]>('/auth/me/companies', { company: false }),
    ])
    setUser(me)
    setPermissions(new Set(codes))
    setCompanies(myCompanies)
    // Keep the remembered company only if the user still has access; otherwise fall back to the first.
    const stored = session.companyId
    const next = myCompanies.find((c) => c.id === stored)?.id ?? myCompanies[0]?.id ?? null
    session.companyId = next
    setCompanyId(next)
  }, [])

  useEffect(() => {
    const onUnauthorized = () => logout()
    window.addEventListener('erp:unauthorized', onUnauthorized)
    return () => window.removeEventListener('erp:unauthorized', onUnauthorized)
  }, [logout])

  useEffect(() => {
    if (!session.token) { setReady(true); return }
    loadSession().catch(() => logout()).finally(() => setReady(true))
  }, [loadSession, logout])

  const login = useCallback(async (userName: string, password: string) => {
    const result = await api<LoginResponse>('/auth/login', { method: 'POST', body: { userName, password }, company: false })
    session.token = result.accessToken
    await loadSession()
  }, [loadSession])

  const selectCompany = useCallback((id: string) => {
    session.companyId = id
    setCompanyId(id)
    queryClient.clear()
  }, [queryClient])

  const value = useMemo<AuthState>(() => ({
    ready, user, companies, permissions,
    company: companies.find((c) => c.id === companyId) ?? null,
    can: (permission) => permissions.has(permission),
    login, logout, selectCompany,
  }), [ready, user, companies, permissions, companyId, login, logout, selectCompany])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}
