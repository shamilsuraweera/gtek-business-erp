import { ADMIN, CLERK, RUN, expect, signIn, signOut, test } from './support'

test('a clerk requests a tolerance change, the approver rejects one and approves another', async ({ page, api }) => {
  const order = await api.liveOrder('AP')
  const firstReason = `Vendor warned of a price rise ${RUN}`
  const secondReason = `Short shipment expected ${RUN}`
  const card = (reason: string) => page.locator('div.card', { hasText: reason }).last()

  await signIn(page, CLERK)
  await page.goto(`/purchase-orders/${order.id}`)
  await expect(page.getByRole('button', { name: /^Tolerances$/ })).toHaveCount(0)       // the clerk cannot set them directly
  await page.getByRole('button', { name: 'Request tolerance change' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.locator(`input[aria-label^="Price tolerance for ${order.sku}"]`).fill('6')
  await dialog.getByLabel('Why is this needed?').fill(firstReason)
  await dialog.getByRole('button', { name: 'Send request' }).click()
  await dialog.waitFor({ state: 'detached' })
  await expect(page.getByText(/TOL-\d+/).first()).toBeVisible()
  await expect(page.getByText('Pending').first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Approve' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Withdraw' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Request tolerance change' })).toHaveCount(0)   // one open request per order
  await signOut(page)

  await signIn(page, ADMIN)
  await page.getByRole('link', { name: 'Tolerance requests' }).click()
  await expect(card(firstReason).getByText('— → 6%')).toBeVisible()
  await card(firstReason).getByRole('button', { name: 'Reject' }).click()
  const rejecting = page.getByRole('dialog')
  await expect(rejecting.getByRole('button', { name: 'Reject request' })).toBeDisabled()    // a reason is required
  await rejecting.getByLabel('Reason for rejecting').fill('Contract price is fixed')
  await rejecting.getByRole('button', { name: 'Reject request' }).click()
  await rejecting.waitFor({ state: 'detached' })
  await expect(page.getByText(firstReason)).toHaveCount(0)                                  // it left the pending list
  await signOut(page)

  await signIn(page, CLERK)
  await page.goto(`/purchase-orders/${order.id}`)
  await expect(page.getByText('Contract price is fixed')).toBeVisible()
  await page.getByRole('button', { name: 'Request tolerance change' }).click()
  await dialog.locator(`input[aria-label^="Under-delivery tolerance for ${order.sku}"]`).fill('10')
  await dialog.getByLabel('Why is this needed?').fill(secondReason)
  await dialog.getByRole('button', { name: 'Send request' }).click()
  await dialog.waitFor({ state: 'detached' })
  await signOut(page)

  await signIn(page, ADMIN)
  await page.goto(`/purchase-orders/${order.id}`)
  await expect(page.getByText(secondReason)).toBeVisible()
  await page.getByRole('button', { name: 'Approve' }).click()
  await expect(page.getByText('Approved').first()).toBeVisible()
  await expect(page.getByText('order override').first()).toBeVisible()
})
