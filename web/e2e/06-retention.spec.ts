import { ADMIN, expect, signIn, test } from './support'

test('a category can have its own notification retention, which persists and can be cleared', async ({ page }) => {
  await signIn(page, ADMIN)
  await page.goto('/admin')
  await page.getByRole('tab', { name: 'Notifications' }).click()
  await expect(page.getByRole('heading', { name: 'By category' })).toBeVisible()
  const row = (name: string) => page.locator('tr', { has: page.getByText(name, { exact: true }) })
  await expect(row('approval')).toBeVisible()
  await expect(row('low-stock')).toBeVisible()

  await page.getByLabel('approval read days').fill('10')
  await row('approval').getByRole('button', { name: 'Save' }).click()
  await expect(row('approval').getByText('own rule')).toBeVisible()
  await expect(row('approval').getByText(/^10 \//)).toBeVisible()

  // Everyone is told about the exception in the bell panel.
  await page.getByRole('button', { name: /Notifications/ }).click()
  await expect(page.getByRole('region', { name: 'Notifications' }).getByText(/Exceptions: Approval requests 10 days after read/)).toBeVisible()
  await page.keyboard.press('Escape')

  await page.reload()
  await page.getByRole('tab', { name: 'Notifications' }).click()
  await expect(page.getByLabel('approval read days')).toHaveValue('10')
  await expect(page.getByLabel('decision read days')).toHaveValue('')               // another category is unaffected

  // Longer than the unread period it inherits: refused, and nothing is saved.
  await page.getByLabel('approval read days').fill('3000')
  await row('approval').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText(/must not be shorter than read days/).first()).toBeVisible()

  await page.reload()
  await page.getByRole('tab', { name: 'Notifications' }).click()
  await row('approval').getByRole('button', { name: 'Inherit' }).click()
  await expect(page.getByLabel('approval read days')).toHaveValue('')
  await expect(row('approval').getByText('own rule')).toHaveCount(0)
})
