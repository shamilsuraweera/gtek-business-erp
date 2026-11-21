import { ADMIN, expect, signIn, test } from './support'

test('reorder levels show on the items page, can be edited, and flag items that are low on stock', async ({ page }) => {
  await signIn(page, ADMIN)
  await page.goto('/items')
  await expect(page.getByRole('columnheader', { name: 'Reorder at' })).toBeVisible()
  const printer = page.getByRole('row', { name: /PRN-100/ })
  await expect(printer.getByText('10', { exact: true }).first()).toBeVisible()      // seeded level; the printer starts below it

  const mouse = () => page.getByRole('row', { name: /MOU-001/ })
  await mouse().getByRole('button', { name: 'Edit' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Reorder level').fill('100000')                           // far above what is on hand
  await dialog.getByRole('button', { name: 'Save' }).click()
  await dialog.waitFor({ state: 'detached' })
  await expect(mouse().getByText('100,000').or(mouse().getByText('100000'))).toBeVisible()
  await expect(mouse().locator('.badge.warn')).toBeVisible()                        // on hand is below the level: flagged
  await expect(printer.locator('.badge.warn')).toBeVisible()                        // the printer starts below its seeded level

  // The dashboard card lists only the six lowest items, and zero-stock items from other tests compete for those places, so only its presence is asserted.
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Low stock' })).toBeVisible()

  await page.goto('/items')                                                         // put it back
  await mouse().getByRole('button', { name: 'Edit' }).click()
  await dialog.getByLabel('Reorder level').fill('')
  await dialog.getByRole('button', { name: 'Save' }).click()
  await dialog.waitFor({ state: 'detached' })
  await expect(mouse().locator('.badge.warn')).toHaveCount(0)                      // no level, plenty on hand: no longer flagged
})
