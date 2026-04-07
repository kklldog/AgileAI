import { expect, test } from '@playwright/test'

const realProviderName = `PW Real Provider ${Date.now()}`
const realModelName = `PW Real Model ${Date.now()}`
const realAgentName = `PW Real Agent ${Date.now()}`
const realEndpoint = process.env.PW_REAL_ENDPOINT ?? ''
const realApiKey = process.env.PW_REAL_API_KEY ?? ''
const realModelKey = process.env.PW_REAL_MODEL_KEY ?? ''

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

test('agent create and edit can configure selected tools with default all checked', async ({ page, request }) => {
  await page.goto('/agents')
  await page.getByTestId('create-agent').click()
  const agentName = `PW Tools Agent ${Date.now()}`

  const toolsResponse = await request.get('http://127.0.0.1:5117/api/agent-tools')
  expect(toolsResponse.ok()).toBeTruthy()
  const tools = await toolsResponse.json()
  expect(tools.length).toBeGreaterThan(0)

  await page.locator('.agent-tools-collapse').getByText('Tools', { exact: true }).click()

  const toolCheckboxes = page.getByRole('checkbox').filter({ hasText: /.+/ })
  await expect(toolCheckboxes).toHaveCount(tools.length)

  for (const tool of tools) {
    await expect(page.getByRole('checkbox', { name: tool.name, exact: true })).toBeChecked()
  }

  await page.getByTestId('agent-name-input').locator('input').fill(agentName)
  await page.getByTestId('agent-description-input').locator('textarea').fill('Agent tool selection validation.')
  await page.getByTestId('agent-prompt-input').locator('textarea').fill('Use only configured tools.')

  if (tools.length > 0) {
    await page.getByRole('checkbox', { name: tools[0].name, exact: true }).uncheck()
  }

  await page.getByTestId('save-agent').click()

  const createdCard = page.locator('.agent-card', { hasText: agentName }).first()
  await expect(createdCard).toBeVisible()
  await createdCard.locator('button').first().click()
  await page.locator('.agent-tools-collapse').getByText('Tools', { exact: true }).click()

  if (tools.length > 0) {
    await expect(page.getByRole('checkbox', { name: tools[0].name, exact: true })).not.toBeChecked()
  }
  for (const tool of tools.slice(1)) {
    await expect(page.getByRole('checkbox', { name: tool.name, exact: true })).toBeChecked()
  }
})

test('scroll-loaded lists reveal more providers models and agents on demand', async ({ page, request }) => {
  test.setTimeout(120_000)
  await page.setViewportSize({ width: 1280, height: 720 })

  async function expectListCanExpand(
    itemLocator: ReturnType<typeof page.locator>,
    loadMoreLabel: string,
    expectedTotal: number,
    minimumInitialCount = 1,
  ) {
    await expect
      .poll(async () => await itemLocator.count(), {
        message: `${loadMoreLabel} list should render at least the initial batch`,
      })
      .toBeGreaterThanOrEqual(minimumInitialCount)

    const initialCount = await itemLocator.count()
    expect(initialCount).toBeLessThanOrEqual(expectedTotal)

    const loadMoreButton = page.getByRole('button', { name: loadMoreLabel })
    if (initialCount < expectedTotal) {
      await expect(loadMoreButton).toBeVisible()
      await loadMoreButton.click()
    }

    await expect(itemLocator).toHaveCount(expectedTotal)
  }

  const stamp = Date.now()
  const providerPrefix = `PW Scroll Provider ${stamp}`
  const modelPrefix = `PW Scroll Model ${stamp}`
  const agentPrefix = `PW Scroll Agent ${stamp}`

  const createdProviderIds: string[] = []

  for (let index = 0; index < 10; index += 1) {
    const providerCreateResponse = await request.post('http://127.0.0.1:5117/api/provider-connections', {
      data: {
        name: `${providerPrefix} ${index + 1}`,
        providerType: 1,
        apiKey: `demo-local-${index + 1}`,
        baseUrl: 'mock://studio/v1/',
        endpoint: null,
        providerName: null,
        relativePath: null,
        apiKeyHeaderName: null,
        authMode: null,
        apiVersion: null,
        isEnabled: true,
      },
    })

    expect(providerCreateResponse.ok()).toBeTruthy()
    const createdProvider = await providerCreateResponse.json()
    createdProviderIds.push(createdProvider.id)
  }

  const selectedProviderId = createdProviderIds[createdProviderIds.length - 1]

  for (let index = 0; index < 15; index += 1) {
    const modelCreateResponse = await request.post('http://127.0.0.1:5117/api/models', {
      data: {
        providerConnectionId: selectedProviderId,
        displayName: `${modelPrefix} ${index + 1}`,
        modelKey: `scroll-model-${index + 1}`,
        supportsStreaming: true,
        supportsTools: true,
        supportsVision: false,
        isEnabled: true,
      },
    })

    expect(modelCreateResponse.ok()).toBeTruthy()
  }

  const modelListResponse = await request.get('http://127.0.0.1:5117/api/models')
  expect(modelListResponse.ok()).toBeTruthy()
  const models = await modelListResponse.json()
  const selectedModel = models.find((item: { displayName: string; id: string }) => item.displayName === `${modelPrefix} 15`)
  expect(selectedModel).toBeTruthy()

  for (let index = 0; index < 15; index += 1) {
    const agentCreateResponse = await request.post('http://127.0.0.1:5117/api/agents', {
      data: {
        studioModelId: selectedModel.id,
        name: `${agentPrefix} ${index + 1}`,
        description: 'Playwright scroll loading validation agent.',
        systemPrompt: 'You validate incremental list rendering.',
        temperature: 0.6,
        maxTokens: 2048,
        enableSkills: false,
        isPinned: false,
        selectedToolNames: [],
        allowedSkillNames: [],
      },
    })

    expect(agentCreateResponse.ok()).toBeTruthy()
  }

  await page.goto('/models')

  const scrollProviderCards = page.locator('.provider-card').filter({ hasText: providerPrefix })
  await expectListCanExpand(scrollProviderCards, 'Load more providers', 10, 8)

  await page.locator('.provider-card', { hasText: `${providerPrefix} 10` }).click()

  const scrollModelCards = page.locator('.model-card').filter({ hasText: modelPrefix })
  await expectListCanExpand(scrollModelCards, 'Load more models', 15, 12)

  await page.goto('/agents')

  const scrollAgentCards = page.locator('.agent-card').filter({ hasText: agentPrefix })
  await expectListCanExpand(scrollAgentCards, 'Load more agents', 15)
})

test('real provider flow can create provider model agent and send a chat message', async ({ page }) => {
  test.skip(!realEndpoint || !realApiKey || !realModelKey, 'Real provider test requires PW_REAL_ENDPOINT, PW_REAL_API_KEY, and PW_REAL_MODEL_KEY.')
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
  await expect(page.getByTestId('message-assistant').filter({ hasText: /Playwright real chat ok/i }).last()).toBeVisible({ timeout: 90000 })
})

test('mock provider chat can require command approval and resolve it from the approval card', async ({ page, request }) => {
  test.setTimeout(90_000)

  const approvalAgentName = `PW Approval Agent ${Date.now()}`
  const approvalProviderName = `PW Approval Provider ${Date.now()}`
  const approvalModelName = `PW Approval Model ${Date.now()}`
  const providerCreateResponse = await request.post('http://127.0.0.1:5117/api/provider-connections', {
    data: {
      name: approvalProviderName,
      providerType: 1,
      apiKey: 'demo-local',
      baseUrl: 'mock://studio/v1/',
      endpoint: null,
      providerName: null,
      relativePath: null,
      apiKeyHeaderName: null,
      authMode: null,
      apiVersion: null,
      isEnabled: true,
    },
  })
  expect(providerCreateResponse.ok()).toBeTruthy()
  const createdProvider = await providerCreateResponse.json()

  const modelCreateResponse = await request.post('http://127.0.0.1:5117/api/models', {
    data: {
      providerConnectionId: createdProvider.id,
      displayName: approvalModelName,
      modelKey: 'gpt-4o-mini',
      supportsStreaming: true,
      supportsTools: true,
      supportsVision: false,
      isEnabled: true,
    },
  })
  expect(modelCreateResponse.ok()).toBeTruthy()
  const createdModel = await modelCreateResponse.json()

  await page.goto('/agents')
  await page.getByTestId('create-agent').click()
  await expect(page.locator('.agent-modal-shell .n-base-selection')).toBeVisible()
  await expect(page.locator('.agent-modal-shell .n-base-selection .n-base-selection-label')).not.toContainText('loading', { timeout: 30_000 })
  await page.getByTestId('agent-name-input').locator('input').fill(approvalAgentName)
  await page.getByTestId('agent-description-input').locator('textarea').fill('Playwright approval flow agent.')
  await page.locator('.agent-modal-shell .n-base-selection').click()
  await page.locator('.n-base-select-menu .n-base-select-option', { hasText: approvalModelName }).click()
  await page.getByTestId('agent-prompt-input').locator('textarea').fill('Use tools when necessary.')
  await page.getByTestId('save-agent').click()
  await expect(page.locator('.agent-card', { hasText: approvalAgentName }).first()).toBeVisible()

  await page.locator('.agent-card', { hasText: approvalAgentName }).click()
  await page.waitForURL(/\/chat\?agentId=/)
  await expect(page.getByTestId('chat-input')).toBeVisible()

  await page.getByTestId('chat-input').locator('textarea').fill('Please run local command approval test')
  await page.getByTestId('send-message').click()

  const approvalModal = page.getByTestId('approval-modal')
  await expect(approvalModal).toBeVisible({ timeout: 30_000 })
  await expect(approvalModal).toContainText('run_local_command')
  await expect(approvalModal).toContainText(/shell:\s+(auto|bash|sh|pwsh|cmd)/i)
  await page.getByTestId('approval-auto-approve').click()

  await approvalModal.locator('[data-testid^="approval-approve-"]').click()

  const assistantBubble = page.locator('[data-testid="message-assistant"]').last()
  await expect(assistantBubble).toContainText(/workspace tool completed successfully/i, { timeout: 30_000 })
  await expect(assistantBubble).toContainText(/Playwright command ok/i, { timeout: 30_000 })
  await expect(assistantBubble.locator('[data-testid^="tool-history-"]').last()).toContainText('run_local_command')

  await page.getByTestId('chat-input').locator('textarea').fill('Please run local command approval test again')
  await page.getByTestId('send-message').click()

  await expect(page.getByTestId('approval-modal')).toBeHidden({ timeout: 10_000 })

  const latestAssistantBubble = page.locator('[data-testid="message-assistant"]').last()
  await expect(latestAssistantBubble).toContainText(/workspace tool completed successfully/i, { timeout: 30_000 })
  await expect(latestAssistantBubble).toContainText(/Playwright command ok/i, { timeout: 30_000 })
  await expect(latestAssistantBubble.locator('[data-testid^="tool-history-"]').last()).toContainText('run_local_command')
})

test('agent allowed skills restrict which skill can become active', async ({ page, request }) => {
  test.setTimeout(90_000)

  const agentName = `PW Skill Allowlist Agent ${Date.now()}`
  const providerName = `PW Skill Provider ${Date.now()}`
  const modelName = `PW Skill Model ${Date.now()}`
  let modelId = ''
  let agentId = ''
  const providerCreateResponse = await request.post('http://127.0.0.1:5117/api/provider-connections', {
    data: {
      name: providerName,
      providerType: 1,
      apiKey: 'demo-local',
      baseUrl: 'mock://studio/v1/',
      endpoint: null,
      providerName: null,
      relativePath: null,
      apiKeyHeaderName: null,
      authMode: null,
      apiVersion: null,
      isEnabled: true,
    },
  })
  expect(providerCreateResponse.ok()).toBeTruthy()
  const createdProvider = await providerCreateResponse.json()

  const modelCreateResponse = await request.post('http://127.0.0.1:5117/api/models', {
    data: {
      providerConnectionId: createdProvider.id,
      displayName: modelName,
      modelKey: 'gpt-4o-mini',
      supportsStreaming: true,
      supportsTools: true,
      supportsVision: false,
      isEnabled: true,
    },
  })
  expect(modelCreateResponse.ok()).toBeTruthy()
  const createdModel = await modelCreateResponse.json()
  modelId = createdModel.id
  expect(modelId).toBeTruthy()

  const agentCreateResponse = await request.post('http://127.0.0.1:5117/api/agents', {
    data: {
      studioModelId: modelId,
      name: agentName,
      description: 'Playwright skill allowlist agent.',
      systemPrompt: 'Use skills when they match.',
      temperature: 0.6,
      maxTokens: 2048,
      enableSkills: true,
      isPinned: false,
      selectedToolNames: [],
      allowedSkillNames: ['repo-guide'],
    },
  })
  expect(agentCreateResponse.ok()).toBeTruthy()
  const createdAgent = await agentCreateResponse.json()
  agentId = createdAgent.id

  const conversationCreateResponse = await request.post('http://127.0.0.1:5117/api/conversations', {
    data: {
      agentId,
      title: `PW Skill Conversation ${Date.now()}`,
    },
  })
  expect(conversationCreateResponse.ok()).toBeTruthy()
  await conversationCreateResponse.json()

  await page.goto(`/chat?agentId=${agentId}`)
  await page.waitForURL(/\/chat\?agentId=/)
  await expect(page.getByTestId('chat-input')).toBeVisible({ timeout: 30_000 })

  await page.getByTestId('chat-input').locator('textarea').fill('Please analyze the repository architecture and find implementation entry points')
  await page.getByTestId('send-message').click()
  await expect(page.getByTestId('active-skill-tag')).toContainText('repo-guide', { timeout: 30_000 })

  await page.getByTestId('chat-input').locator('textarea').fill('Write release notes and a changelog summary for today\'s work')
  await page.getByTestId('send-message').click()
  await expect(page.getByTestId('active-skill-tag')).toContainText('repo-guide', { timeout: 30_000 })
  await expect(page.getByTestId('active-skill-tag')).not.toContainText('release-note')
})
