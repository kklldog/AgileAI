import { expect, test } from '@playwright/test'

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
