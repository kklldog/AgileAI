import { expect, test } from '@playwright/test'

const realEndpoint = process.env.PW_REAL_ENDPOINT ?? ''
const realApiKey = process.env.PW_REAL_API_KEY ?? ''
const realModelKey = process.env.PW_REAL_MODEL_KEY ?? ''

test.describe('Screenshot Generation', () => {
  test.describe.configure({ mode: 'serial' })

  let sharedAgentId = ''

  test('take models screenshot', async ({ page }) => {
    await page.goto('/models')
    await page.waitForTimeout(1000)
    await page.screenshot({ path: 'screenshots/studio-models.png', fullPage: true })
  })

  test('take agents screenshot', async ({ page, request }) => {
    const agentCreateResponse = await request.post('http://127.0.0.1:5117/api/agents', {
      data: {
        name: 'Screenshot Agent',
        description: 'An agent created for screenshots.',
        systemPrompt: 'You are a helpful assistant.',
        temperature: 0.7,
        maxTokens: 1000,
        enableSkills: false,
        isPinned: false,
        selectedToolNames: [],
        allowedSkillNames: [],
      },
    })
    if (agentCreateResponse.ok()) {
      const agent = await agentCreateResponse.json()
      sharedAgentId = agent.id
    }

    await page.goto('/agents')
    await page.waitForTimeout(1000)
    await page.screenshot({ path: 'screenshots/studio-agents.png', fullPage: true })
  })

  test('take chat screenshot', async ({ page, request }) => {
    if (!sharedAgentId) {
       const agentsResponse = await request.get('http://127.0.0.1:5117/api/agents')
       if (agentsResponse.ok()) {
           const agents = await agentsResponse.json()
           if (agents.length > 0) {
               sharedAgentId = agents[0].id
           }
       }
    }

    if (sharedAgentId) {
      await page.goto(`/chat?agentId=${sharedAgentId}`)
      await page.waitForTimeout(1000)
      await page.getByTestId('chat-input').locator('textarea').fill('Hello! Please summarize how AgileAI Studio works.')
      await page.getByTestId('send-message').click()
      await expect(page.locator('[data-testid="message-assistant"]').last()).toBeVisible({ timeout: 10000 })
      await page.waitForTimeout(2000)
      await page.screenshot({ path: 'screenshots/studio-chat.png', fullPage: true })
    } else {
      console.log('Skipping chat screenshot: no agent found.')
    }
  })

  test('take real provider chat screenshot', async ({ page, request }) => {
    test.skip(!realEndpoint || !realApiKey || !realModelKey, 'Real provider test requires PW_REAL_ENDPOINT, PW_REAL_API_KEY, and PW_REAL_MODEL_KEY.')
    
    await page.goto('/models')
    await page.getByRole('button', { name: 'Add Provider' }).click()
    await page.getByTestId('provider-name-input').locator('input').fill('OpenAI Compatible API')
    await page.locator('.modal-shell .n-base-selection').first().click()
    await page.getByText('OpenAI Compatible', { exact: true }).click()
    await page.getByTestId('provider-key-input').locator('input').fill(realApiKey)
    
    const providerInputs = page.locator('.modal-shell input')
    await providerInputs.nth(2).fill(realEndpoint)
    await providerInputs.nth(3).fill('openai')
    await providerInputs.nth(4).fill('chat/completions')
    await page.getByTestId('save-provider').click()
    
    await page.locator('.provider-card', { hasText: 'OpenAI Compatible API' }).click()
    await page.getByRole('button', { name: 'New model' }).click()
    await page.getByTestId('model-display-name-input').locator('input').fill('GPT-5.4 Mock')
    await page.getByTestId('model-key-input').locator('input').fill(realModelKey)
    await page.getByTestId('save-model').click()

    await page.goto('/agents')
    await page.getByTestId('create-agent').click()
    await page.getByTestId('agent-name-input').locator('input').fill('Real Provider Agent')
    await page.getByTestId('agent-description-input').locator('textarea').fill('Agent for real provider screenshot.')
    await page.getByTestId('agent-prompt-input').locator('textarea').fill('You are a helpful assistant.')
    await page.getByTestId('save-agent').click()

    await page.locator('.agent-card', { hasText: 'Real Provider Agent' }).click()
    await page.waitForURL(/\/chat\?agentId=/)
    await page.waitForTimeout(1000)

    await page.getByTestId('chat-input').locator('textarea').fill('Can you explain the architecture of AgileAI Studio briefly?')
    await page.getByTestId('send-message').click()
    await expect(page.locator('[data-testid="message-assistant"]').last()).toBeVisible({ timeout: 10000 })
    await page.waitForTimeout(2000)

    await page.screenshot({ path: 'screenshots/studio-chat-gpt54.png', fullPage: true })
  })
})
