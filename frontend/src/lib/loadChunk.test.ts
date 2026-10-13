import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { loadChunk } from './loadChunk'

describe('loadChunk', () => {
  const reload = vi.fn()
  const originalLocation = window.location

  beforeEach(() => {
    sessionStorage.clear()
    reload.mockReset()
    Object.defineProperty(window, 'location', { configurable: true, value: { ...originalLocation, reload } })
  })

  afterEach(() => {
    Object.defineProperty(window, 'location', { configurable: true, value: originalLocation })
  })

  it('returns the module when the import succeeds', async () => {
    await expect(loadChunk(() => Promise.resolve({ Page: 1 }))).resolves.toEqual({ Page: 1 })
    expect(reload).not.toHaveBeenCalled()
  })

  it('reloads once when a chunk from an older build is missing', async () => {
    const failing = () => Promise.reject(new TypeError('Failed to fetch dynamically imported module'))

    void loadChunk(failing)
    await vi.waitFor(() => expect(reload).toHaveBeenCalledTimes(1))

    // Right after that reload the error goes to the error boundary instead of reloading again.
    await expect(loadChunk(failing)).rejects.toThrow('Failed to fetch dynamically imported module')
    expect(reload).toHaveBeenCalledTimes(1)
  })
})
