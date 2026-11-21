import { defineConfig } from '@playwright/test'

// Browser flows of the smoke test (scripts/smoke/run.sh starts the API and the dev server and runs this).
// They run one at a time, in file order, against one seeded database: later files may rely on what the seeder created, not on earlier files.
export default defineConfig({
  testDir: './e2e',
  outputDir: './e2e/.results',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never', outputFolder: 'e2e/.report' }]] : [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5173',
    viewport: { width: 1360, height: 1000 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    // Locally a cached browser can be used instead of downloading one: E2E_CHROMIUM=/path/to/chrome
    launchOptions: { executablePath: process.env.E2E_CHROMIUM || undefined, args: ['--no-sandbox'] },
  },
})
