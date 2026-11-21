import { ADMIN, expect, signIn, test } from './support'

test('create an order, invoice it, pay it, and the ledger still balances', async ({ page }) => {
  await signIn(page, ADMIN)

  await page.getByRole('link', { name: 'Sales orders' }).click()
  await page.getByRole('link', { name: 'New order' }).click()
  await page.getByLabel('Customer').selectOption({ label: 'CUS-003 — Island Logistics' })
  await page.getByLabel('Item').first().selectOption({ label: 'KEY-001 — Wired Keyboard' })
  await page.getByLabel('Quantity').first().fill('3')
  await page.getByRole('button', { name: 'Save draft' }).click()

  await page.getByRole('button', { name: 'Confirm order' }).click()
  await page.getByRole('button', { name: 'Release for invoicing' }).click()
  await page.getByRole('button', { name: 'Create invoice' }).click()
  await expect(page.getByRole('button', { name: 'Record payment' }).first()).toBeVisible()

  await page.getByRole('button', { name: 'Record payment' }).first().click()
  await page.getByLabel('Amount').fill('5000')
  await page.getByRole('button', { name: 'Record payment' }).last().click()
  await expect(page.getByText('PAY-').first()).toBeVisible()

  // Paying more than is owed is refused with the server's message and nothing is recorded.
  await page.getByRole('button', { name: 'Record payment' }).first().click()
  await page.getByLabel('Amount').evaluate((element) => element.removeAttribute('max'))
  await page.getByLabel('Amount').fill('99999999')
  await page.getByRole('button', { name: 'Record payment' }).last().click()
  await expect(page.getByRole('alert')).toContainText(/exceeds/i)
  await page.keyboard.press('Escape')

  await page.getByRole('link', { name: 'Ledger & reports' }).click()
  await expect(page.getByText('Balanced', { exact: true })).toBeVisible()
})
