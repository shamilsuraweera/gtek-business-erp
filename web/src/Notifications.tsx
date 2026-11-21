import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useApiMutation, useApiQuery } from './hooks'
import type { AppNotification } from './api/types'

/** How often the unread count is refreshed while the page is open. */
const POLL_MS = 30_000

function timeAgo(iso: string): string {
  const seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000))
  if (seconds < 60) return 'just now'
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours} h ago`
  return new Date(iso).toLocaleDateString()
}

/** The notification bell: unread count in the top bar, latest messages in a panel. Messages are the signed-in user's own. */
export function NotificationBell() {
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const count = useApiQuery<{ count: number }>('/notifications/unread-count', { refetchInterval: POLL_MS })
  const list = useApiQuery<AppNotification[]>('/notifications?take=20', { enabled: open, refetchInterval: open ? POLL_MS : undefined })
  const read = useApiMutation<string>((id) => ({ path: `/notifications/${id}/read` }))
  const readAll = useApiMutation<void>(() => ({ path: '/notifications/read-all' }))
  const unread = count.data?.count ?? 0

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    const onDown = (e: MouseEvent) => { if (root.current && !root.current.contains(e.target as Node)) setOpen(false) }
    window.addEventListener('keydown', onKey)
    window.addEventListener('mousedown', onDown)
    return () => { window.removeEventListener('keydown', onKey); window.removeEventListener('mousedown', onDown) }
  }, [open])

  const choose = (n: AppNotification) => {
    if (!n.isRead) read.mutate(n.id)
    setOpen(false)
    if (n.link) navigate(n.link)
  }

  return (
    <div className="bell" ref={root}>
      <button className="btn small bell-btn" aria-haspopup="true" aria-expanded={open} onClick={() => setOpen((v) => !v)}
        aria-label={unread > 0 ? `Notifications, ${unread} unread` : 'Notifications'}>
        <span aria-hidden="true">&#128276;</span>
        {unread > 0 && <span className="bell-count">{unread > 99 ? '99+' : unread}</span>}
      </button>
      {open && (
        <div className="bell-panel" role="region" aria-label="Notifications">
          <div className="spread bell-head">
            <strong>Notifications</strong>
            <button className="btn small" disabled={unread === 0 || readAll.isPending} onClick={() => readAll.mutate()}>Mark all read</button>
          </div>
          {list.isLoading ? <p className="muted bell-empty">Loading…</p> : (list.data ?? []).length === 0
            ? <p className="muted bell-empty">Nothing yet.</p>
            : (
              <ul className="bell-list">
                {(list.data ?? []).map((n) => (
                  <li key={n.id}>
                    <button className={`bell-item${n.isRead ? '' : ' unread'}`} onClick={() => choose(n)}>
                      <span className="bell-title">{n.title}</span>
                      {n.body && <span className="bell-body">{n.body}</span>}
                      <span className="muted bell-time">{timeAgo(n.createdAt)}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
        </div>
      )}
    </div>
  )
}
