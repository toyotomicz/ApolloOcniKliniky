import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { callsTo, mockApi, pendingProfile, renderRoute, testPlans } from '../test/utils'

describe('Clinic portal messages', () => {
  it('shows the conversation, marks new replies and sends a message', async () => {
    const user = userEvent.setup()
    const fetchMock = mockApi({
      'GET /api/plans': () => ({ body: testPlans }),
      'GET /api/clinic/me': () => ({ body: pendingProfile }),
      'GET /api/clinic/messages': () => ({
        body: [
          { id: 'm1', sender: 'clinic', authorName: null, body: 'Kdy bude registrace posouzena?', createdAt: '2026-10-13T08:00:00Z', readAt: '2026-10-13T09:00:00Z' },
          { id: 'm2', sender: 'administrator', authorName: 'Administrátor', body: 'Do zítřka.', createdAt: '2026-10-13T10:00:00Z', readAt: null },
        ],
      }),
      'POST /api/clinic/messages': (body) => ({
        status: 201,
        body: { id: 'm3', sender: 'clinic', authorName: null, body: (body as { body: string }).body, createdAt: '2026-10-13T11:00:00Z', readAt: null },
      }),
    })

    renderRoute('/portal')

    const thread = await screen.findByRole('list', { name: 'Zprávy' })
    expect(within(thread).getByText('Do zítřka.')).toBeInTheDocument()
    expect(within(thread).getByText('Nová')).toBeInTheDocument()
    expect(screen.getByText('1 nová zpráva')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Potvrzení \(PDF\)/ })).toHaveAttribute('href', '/api/clinic/me/confirmation.pdf')

    await user.click(screen.getByRole('button', { name: /Odeslat/ }))
    expect(await screen.findByText('Napište text zprávy.')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Nová zpráva'), 'Děkujeme!')
    await user.click(screen.getByRole('button', { name: /Odeslat/ }))
    expect(await within(thread).findByText('Děkujeme!')).toBeInTheDocument()
    const [call] = callsTo(fetchMock, 'POST', '/api/clinic/messages')
    expect(JSON.parse(String(call![1]!.body))).toEqual({ body: 'Děkujeme!' })
  })
})
