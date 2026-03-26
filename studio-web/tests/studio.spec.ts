import { expect, test } from '@playwright/test'

const realProviderName = `PW Real Provider ${Date.now()}`
const realModelName = `PW Real Model ${Date.now()}`
const realAgentName = `PW Real Agent ${Date.now()}`
const realEndpoint = 'http://192.168.0.126:8317'
const realApiKey = 'your-api-key-3'
const realModelKey = 'gpt-5.4'

test('shell navigation is streamlined and root redirects to models', async ({ page }) => {
  await page.goto('/')
  await page.waitForURL('**/models')

  await expect(page.getByRole('link', { name: 'Models' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Agents' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Overview' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Chat' })).toHaveCount(0)
  await expect(page.getByText('Version One')).toHaveCount(0)
  await expect(page.getByText(/^A$/)).toHaveCount(0)
})

test('theme toggle exists in top area and can switch theme', async ({ page }) => {
  await page.goto('/models')

  const themeButton = page.locator('.shell-main-bar button').first()
  await expect(themeButton).toBeVisible()

  const initialTheme = await page.locator('html').getAttribute('data-theme')
  await themeButton.click()
  const nextTheme = await page.locator('html').getAttribute('data-theme')
  expect(nextTheme).not.toBe(initialTheme)
})

test('right main area tiles full width on large screens', async ({ page }) => {
  await page.setViewportSize({ width: 1800, height: 1100 })
  await page.goto('/models')

  const geometry = await page.evaluate(() => {
    const main = document.querySelector('.shell-main')?.getBoundingClientRect()
    const content = document.querySelector('.shell-content')?.getBoundingClientRect()
    if (!main || !content) return null
    return {
      mainWidth: main.width,
      contentWidth: content.width,
      leftGap: content.left - main.left,
      rightGap: main.right - content.right,
    }
  })

  expect(geometry).not.toBeNull()
  expect(geometry!.contentWidth).toBeGreaterThan(1400)
  expect(Math.abs(geometry!.leftGap)).toBeLessThanOrEqual(28)
  expect(Math.abs(geometry!.rightGap)).toBeLessThanOrEqual(28)
})

test('models page uses provider-left and models-right layout', async ({ page }) => {
  await page.goto('/models')

  const columns = page.locator('.grid-two > .n-card')
  await expect(columns).toHaveCount(2)
  await expect(page.locator('.providers-panel')).toBeVisible()
  await expect(page.locator('.models-panel')).toBeVisible()

  const providerCards = page.locator('.provider-card')
  const providerCount = await providerCards.count()
  if (providerCount > 0) {
    await providerCards.first().click()
    await expect(page.locator('.provider-card.selected')).toHaveCount(1)
  }
})

test('agents page renders and cards navigate into chat', async ({ page }) => {
  await page.goto('/agents')
  await expect(page.getByRole('button', { name: 'Create agent' })).toBeVisible()

  const agentCards = page.locator('.agent-card')
  const agentCount = await agentCards.count()
  if (agentCount > 0) {
    await agentCards.first().click()
    await page.waitForURL(/\/chat\?agentId=/)
    await expect(page.getByTestId('chat-input')).toBeVisible()
    await expect(page.locator('.chat-layout')).toBeVisible()
  }
})

test('real provider flow can create provider model agent and send a chat message', async ({ page }) => {
  test.setTimeout(120_000)

  await page.goto('/models')
  await page.getByRole('button', { name: 'Add Provider' }).click()
  await page.getByTestId('provider-name-input').locator('input').fill(realProviderName)
  await page.locator('.modal-shell .n-base-selection').first().click()
  await page.getByText('OpenAI Compatible', { exact: true }).click()
  await page.getByTestId('provider-key-input').locator('input').fill(realApiKey)

  const providerInputs = page.locator('.modal-shell input')
  await providerInputs.nth(2).fill(realEndpoint)
  await providerInputs.nth(3).fill('openai')
  await providerInputs.nth(4).fill('chat/completions')
  await page.getByTestId('save-provider').click()
  await expect(page.getByText(realProviderName)).toBeVisible()

  await page.locator('.provider-card', { hasText: realProviderName }).click()
  await page.getByRole('button', { name: 'New model' }).click()
  await page.getByTestId('model-display-name-input').locator('input').fill(realModelName)
  await page.getByTestId('model-key-input').locator('input').fill(realModelKey)
  await page.getByTestId('save-model').click()
  await expect(page.locator('.model-card', { hasText: realModelName }).first()).toBeVisible()

  await page.goto('/agents')
  await page.getByTestId('create-agent').click()
  await page.getByTestId('agent-name-input').locator('input').fill(realAgentName)
  await page.getByTestId('agent-description-input').locator('textarea').fill('Playwright real endpoint validation agent.')
  await page.getByTestId('agent-prompt-input').locator('textarea').fill('You are a concise assistant. Reply in one short sentence.')
  await page.getByTestId('save-agent').click()
  await expect(page.locator('.agent-card', { hasText: realAgentName }).first()).toBeVisible()

  await page.locator('.agent-card', { hasText: realAgentName }).click()
  await page.waitForURL(/\/chat\?agentId=/)
  await expect(page.getByTestId('chat-input')).toBeVisible()

  const sendRow = page.locator('.chat-send-row')
  const composerMetrics = await sendRow.evaluate((element) => {
    const send = element.querySelector('[data-testid="send-message"]')?.getBoundingClientRect()
    const row = element.getBoundingClientRect()
    return send && row ? { buttonRightGap: row.right - send.right } : null
  })
  expect(composerMetrics).not.toBeNull()
  expect(composerMetrics!.buttonRightGap).toBeLessThan(8)

  await page.getByTestId('chat-input').locator('textarea').fill('Reply with exactly: Playwright real chat ok')
  await page.getByTestId('send-message').click()
  await expect(page.getByText(/Playwright real chat ok/i)).toBeVisible({ timeout: 90000 })
})
