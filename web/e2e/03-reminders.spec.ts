import { ADMIN, expect, signIn, test } from './support'

// The API runs the reminders once shortly after it starts (scripts/smoke/run.sh), and the seeded demo data gives each something to report.
test('the scheduled reminders reached the notification bell and link to where to act', async ({ page }) => {
  await signIn(page, ADMIN)
  await page.getByRole('button', { name: /Notifications/ }).click()
  const panel = page.getByRole('region', { name: 'Notifications' })
  await expect(panel.getByText(/invoices? (is|are) overdue/)).toBeVisible()
  await expect(panel.getByText(/bills? (is|are) overdue/)).toBeVisible()
  await expect(panel.getByText(/items? (is|are) low on stock/)).toBeVisible()
  await expect(panel.getByText(/accounting periods? (is|are) still open/)).toBeVisible()

  await panel.getByText(/items? (is|are) low on stock/).click()
  await expect(page).toHaveURL(/\/items$/)
  await expect(page.getByRole('heading', { name: 'Items & stock' })).toBeVisible()
})
