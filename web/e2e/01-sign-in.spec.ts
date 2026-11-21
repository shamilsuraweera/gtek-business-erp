import { ADMIN, CLERK, expect, signIn, signOut, test } from './support'

test('a wrong password is refused with a message', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel('User name').fill(ADMIN.user)
  await page.getByLabel('Password').fill('wrong-password-123')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('alert')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toHaveCount(0)
})

test('the administrator sees the demo company, its figures and every area', async ({ page }) => {
  await signIn(page, ADMIN)
  await expect(page.getByLabel('Company')).toContainText('GTEK Demo (Pvt) Ltd')
  await expect(page.getByText('Receivables ageing')).toBeVisible()
  await expect(page.getByText('Cash & bank')).toBeVisible()
  for (const area of ['Sales orders', 'Purchase orders', 'Items & stock', 'Ledger & reports', 'Administration'])
    await expect(page.getByRole('link', { name: area })).toBeVisible()
  await signOut(page)
})

test('the accounts-payable clerk sees only what their role allows', async ({ page }) => {
  await signIn(page, CLERK)
  await expect(page.getByRole('link', { name: 'Purchase orders' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Administration' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Ledger & reports' })).toHaveCount(0)
  await signOut(page)
})
