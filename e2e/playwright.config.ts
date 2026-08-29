import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end configuration.
 *
 * Headless by default so CI and `npm test` behave identically. To watch a run, use
 * `npm run watch` (Playwright's UI mode — a time-travel debugger, far more useful than
 * staring at a headed browser) or `npm run slow` for a headed, single-worker run with no
 * timeout, which is the one to use when demonstrating something to a person.
 */

const BASE_URL = process.env.PDFWERK_URL ?? 'http://localhost:5272'

/** True when pointing at an already-running instance rather than starting one. */
const external = Boolean(process.env.PDFWERK_URL)

export default defineConfig({
  testDir: './tests',
  outputDir: './.artifacts',

  // A PDF render plus a network round trip is comfortably under this; summarisation, which
  // calls a third-party model, gets its own longer timeout at the test level.
  timeout: 30_000,
  expect: { timeout: 10_000 },

  fullyParallel: false,

  /**
   * One worker, deliberately.
   *
   * The service is rate limited per caller, and every worker shares the same address-derived
   * anonymous bucket. Running in parallel makes tests fail on each other's quota rather than
   * on their own behaviour, and those failures look like flakes.
   */
  workers: 1,

  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,

  reporter: process.env.CI
    ? [['github'], ['html', { open: 'never' }]]
    : [['list'], ['html', { open: 'never' }]],

  use: {
    baseURL: BASE_URL,

    // Traces and video are what make a failure diagnosable after the fact, which matters far
    // more than watching a passing run.
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',

    actionTimeout: 10_000,
    navigationTimeout: 20_000,
  },

  projects: [
    {
      name: 'api',
      testMatch: /api\.spec\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'chromium',
      testMatch: /ui\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1440, height: 900 },
      },
    },

    /**
     * The guided tour. Not in the default run — `npm test` is for catching regressions, and the
     * tour walks the same happy paths a third time. Ask for it with `--project=demo`.
     *
     * Video and trace are always on here because a recording is the point, not a diagnostic.
     */
    {
      name: 'demo',
      testMatch: /demo\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1440, height: 900 },
        video: 'on',
        trace: 'on',
        screenshot: 'off',
        actionTimeout: 60_000,

        /**
         * Slows every action so clicks and navigations can be followed by eye. DEMO_PACE scales
         * it alongside the caption timings in the spec, and 0 turns both off — which is what to
         * use if the tour is ever run as a smoke test rather than watched.
         */
        launchOptions: {
          slowMo: Math.round(220 * Number(process.env.DEMO_PACE ?? '1')),
        },
      },
    },
  ],

  /**
   * Starts the API when one is not already running.
   *
   * `reuseExistingServer` keeps the local loop fast — if you already have it up on 5272, the
   * suite attaches to that instead of fighting for the port. CI always starts its own.
   */
  webServer: external
    ? undefined
    : {
        command: 'dotnet run --project ../src/PdfWerk.Api --launch-profile http',
        url: `${BASE_URL}/health`,
        reuseExistingServer: !process.env.CI,
        timeout: 180_000,
        stdout: 'ignore',
        stderr: 'pipe',
      },
})
