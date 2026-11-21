import { ADMIN, CLERK, RUN, expect, signIn, test } from './support'

test('a request notifies the approver, who can open it and clear the bell', async ({ page, api }) => {
  const order = await api.liveOrder('NT')
  const clerk = await api.as(CLERK)
  const raised = await clerk.post(`/purchasing/orders/${order.id}/tolerance-requests`, {
    reason: `Notify test ${RUN}`, lines: [{ itemId: (await (await api.as(ADMIN)).get(`/purchasing/orders/${order.id}`)).body.lines[0].itemId, priceTolerancePercent: 5 }],
  })
  expect(raised.status).toBe(201)

  await signIn(page, ADMIN)
  const bell = page.getByRole('button', { name: /Notifications/ })
  await expect(bell).toContainText(/\d/)                                          // an unread count on the bell
  await bell.click()
  await page.getByRole('button', { name: 'Mark all read' }).click()
  await expect(page.getByRole('button', { name: 'Mark all read' })).toBeDisabled()          // nothing unread any more
  await page.keyboard.press('Escape')
  await page.reload()
  await expect(page.getByRole('button', { name: /Notifications/ })).not.toContainText(/\d/)

  // Read messages stay in the list, and opening one goes to the thing it is about.
  await page.getByRole('button', { name: /Notifications/ }).click()
  await page.getByText(`Tolerance request ${raised.body.number} needs a decision`).click()
  await expect(page).toHaveURL(/\/tolerance-requests$/)
})

test('email can be switched off overall and per category, and the choice persists', async ({ page }) => {
  await signIn(page, ADMIN)
  const bell = () => page.getByRole('button', { name: /Notifications/ })
  const category = (name: string) => page.getByLabel(`Email ${name}`)

  await bell().click()
  for (const name of ['approval', 'decision', 'overdue-invoice', 'overdue-bill', 'low-stock', 'period-close'])
    await expect(category(name)).toBeChecked()

  await category('approval').click()
  await expect(category('approval')).not.toBeChecked()
  await page.reload()
  await bell().click()
  await expect(category('approval')).not.toBeChecked()
  await expect(category('decision')).toBeChecked()

  const master = page.locator('.bell-foot input[type=checkbox]').first()
  await master.click()                                                              // email off: the category list goes away
  await expect(category('approval')).toHaveCount(0)
  await master.click()
  await expect(category('approval')).not.toBeChecked()                              // ...and the choice survived

  await expect(category('account')).toHaveCount(0)                                  // security notices are not a choice

  await category('approval').click()                                                // put it back for the other tests
  await expect(category('approval')).toBeChecked()
})
