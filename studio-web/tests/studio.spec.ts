import { expect, test } from '@playwright/test'

test('dashboard renders polished overview', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'AgileAI.Studio' })).toBeVisible()
  await expect(page.getByText('Build your AI workspace with modern control and clean orchestration.')).toBeVisible()
  await page.screenshot({ path: 'screenshots/studio-overview.png', fullPage: true })
})

test('models page screenshot', async ({ page }) => {
  await page.goto('/models')
  await expect(page.getByRole('heading', { name: /Connect providers and publish the models/i })).toBeVisible()
  await page.screenshot({ path: 'screenshots/studio-models.png', fullPage: true })
})

test('agents page screenshot', async ({ page }) => {
  await page.goto('/agents')
  await expect(page.getByRole('heading', { name: /Compose reusable assistants/i })).toBeVisible()
  await page.screenshot({ path: 'screenshots/studio-agents.png', fullPage: true })
})

test('create agent and stream chat reply', async ({ page }) => {
  await page.goto('/agents')
  await page.getByTestId('create-agent').click()
  await page.getByTestId('agent-name-input').locator('input').fill('Playwright Helper')
  await page.getByTestId('agent-description-input').locator('textarea').fill('Used for end to end validation flows.')
  await page.getByTestId('agent-prompt-input').locator('textarea').fill('You are a concise assistant for testing AgileAI Studio.')
  await page.getByTestId('save-agent').click()
  await expect(page.getByTestId('agent-list').getByText('Playwright Helper').last()).toBeVisible()

  await page.goto('/chat')
  const agentSelect = page.getByTestId('agent-select')
  const matchingOptions = agentSelect.locator('option').filter({ hasText: 'Playwright Helper' })
  const agentValue = await matchingOptions.last().getAttribute('value')
  await agentSelect.selectOption(agentValue ?? '')
  await page.getByTestId('new-conversation').click()
  await expect(page.getByRole('heading', { name: 'Playwright Helper session', exact: true })).toBeVisible()
  await page.getByTestId('chat-input').locator('textarea').fill('Give me a short launch checklist.')
  await page.getByTestId('send-message').click()
  await page.waitForTimeout(1500)
  await page.screenshot({ path: 'screenshots/studio-chat.png', fullPage: true })
})

test('real GPT-5.4 chat screenshot', async ({ page }) => {
  await page.goto('/chat')
  await page.locator('button.conversation-link', { hasText: 'GPT-5.4 live verification' }).first().click()
  await expect(page.getByText('GPT-5.4 live verification').first()).toBeVisible()
  await page.waitForTimeout(800)
  await page.screenshot({ path: 'screenshots/studio-chat-gpt54.png', fullPage: true })
})

test('workspace file tools can search repository content', async ({ page }) => {
  await page.goto('/chat')
  const agentSelect = page.getByTestId('agent-select')
  const studioConcierge = await agentSelect.locator('option').filter({ hasText: 'Studio Concierge' }).first().getAttribute('value')
  await agentSelect.selectOption(studioConcierge ?? '')
  await page.getByTestId('new-conversation').click()
  await page.getByTestId('chat-input').locator('textarea').fill('Use the workspace tools to search for AgileAI.Studio and show the file path you found.')
  await page.getByTestId('send-message').click()
  await page.waitForTimeout(1500)
  await expect(page.getByText(/Workspace file tools are available/i)).toBeVisible()
})

test('model validation, agent edit, and conversation switching flow', async ({ page }) => {
  await page.goto('/models')
  await page.getByRole('button', { name: 'Test' }).first().click()
  await page.waitForTimeout(800)

  await page.goto('/agents')
  await page.getByRole('button', { name: 'Edit' }).first().click()
  const description = page.getByTestId('agent-description-input').locator('textarea')
  await description.fill('Updated through Playwright validation flow.')
  await page.getByTestId('save-agent').click()

  await page.goto('/chat')
  const conversationButtons = page.locator('button.conversation-link')
  const count = await conversationButtons.count()
  if (count > 1) {
    await conversationButtons.nth(1).click()
  }
  await page.waitForTimeout(700)
  await expect(page.getByText('Live chat')).toBeVisible()
})
