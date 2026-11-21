import { test as base, expect, type APIRequestContext, type Page } from '@playwright/test'

export const ADMIN = { user: 'admin', password: 'Admin#123456' }
export const CLERK = { user: 'apclerk', password: 'Clerk#1234567' }
export const RUN = String(Date.now()).slice(-6)

/** The API through the dev server's proxy, for setting up data a test then uses in the browser. */
export class Api {
  constructor(private readonly request: APIRequestContext) {}

  private tokens = new Map<string, string>()
  companyId = ''

  async as(who: { user: string; password: string }) {
    if (!this.tokens.has(who.user)) {
      const login = await this.request.post('/api/v1/auth/login', { data: { userName: who.user, password: who.password } })
      expect(login.ok(), `sign in as ${who.user}`).toBeTruthy()
      this.tokens.set(who.user, (await login.json()).accessToken)
      if (!this.companyId) {
        const companies = await (await this.request.get('/api/v1/auth/me/companies', { headers: { Authorization: `Bearer ${this.tokens.get(who.user)}` } })).json()
        this.companyId = companies.find((c: { code: string }) => c.code === 'GTEK-DEMO').id
      }
    }
    const token = this.tokens.get(who.user)!
    const send = async (method: 'get' | 'post' | 'put', path: string, data?: unknown) => {
      const response = await this.request[method](`/api/v1${path}`, {
        headers: { Authorization: `Bearer ${token}`, 'X-Company-Id': this.companyId }, data,
      })
      const text = await response.text()
      return { status: response.status(), body: text ? JSON.parse(text) : null }
    }
    return {
      get: (path: string) => send('get', path),
      post: (path: string, data?: unknown) => send('post', path, data ?? {}),
      put: (path: string, data?: unknown) => send('put', path, data),
    }
  }

  /** A released purchase order of a brand-new vendor and item, ready for a tolerance request. */
  async liveOrder(tag: string) {
    const admin = await this.as(ADMIN)
    const vendor = await admin.post('/purchasing/vendors', { code: `E2E${tag}${RUN}`, name: `E2E vendor ${tag} ${RUN}` })
    const item = await admin.post('/inventory/items', { sku: `E2E${tag}-${RUN}`, name: `E2E item ${tag}`, unitPrice: 2000, unitCost: 1000 })
    expect([vendor.status, item.status]).toEqual([201, 201])
    const order = await admin.post('/purchasing/orders', { vendorId: vendor.body.id, lines: [{ itemId: item.body.id, quantity: 10, unitCost: 1000 }] })
    expect(order.status).toBe(201)
    await admin.post(`/purchasing/orders/${order.body.id}/open`)
    await admin.post(`/purchasing/orders/${order.body.id}/release`)
    return { id: order.body.id as string, number: order.body.number as string, sku: item.body.sku as string }
  }
}

/**
 * Every test fails on an uncaught page error or a server error (5xx) from the API, however the assertions went.
 * Expected client errors (a 400 the test provokes) are not counted.
 */
export const test = base.extend<{ problems: string[]; api: Api }>({
  problems: [async ({ page }, use) => {
    const problems: string[] = []
    page.on('pageerror', (error) => problems.push(`page error: ${error.message}`))
    page.on('response', (response) => {
      if (response.url().includes('/api/') && response.status() >= 500) problems.push(`HTTP ${response.status()} ${response.request().method()} ${response.url()}`)
    })
    await use(problems)
    expect(problems, 'unexpected browser or server errors').toEqual([])
  }, { auto: true }],
  api: async ({ request }, use) => { await use(new Api(request)) },
})

export { expect }

export async function signIn(page: Page, who: { user: string; password: string }) {
  await page.goto('/')
  await page.getByLabel('User name').fill(who.user)
  await page.getByLabel('Password').fill(who.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
}

export async function signOut(page: Page) {
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
}
