import { render, screen } from '@testing-library/react'
import { act, useRef } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useReveal } from './useReveal'

function Cards() {
  const ref = useRef<HTMLDivElement>(null)
  useReveal(ref, '.card', true)
  return (
    <div ref={ref}>
      <article className="card">A</article>
      <article className="card">B</article>
    </div>
  )
}

describe('useReveal', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('leaves content visible when IntersectionObserver is unavailable', () => {
    vi.stubGlobal('IntersectionObserver', undefined)
    render(<Cards />)
    expect(screen.getByText('A')).not.toHaveAttribute('data-reveal')
  })

  it('hides cards until they scroll into view, with a staggered delay', () => {
    let callback: IntersectionObserverCallback = () => {}
    const unobserve = vi.fn()
    vi.stubGlobal(
      'IntersectionObserver',
      class {
        constructor(cb: IntersectionObserverCallback) {
          callback = cb
        }
        observe() {}
        unobserve = unobserve
        disconnect() {}
      },
    )
    render(<Cards />)
    const [a, b] = [screen.getByText('A'), screen.getByText('B')]
    expect(a).toHaveAttribute('data-reveal', 'hidden')
    expect(b.style.getPropertyValue('--reveal-delay')).toBe('90ms')

    act(() => callback([{ target: a, isIntersecting: true } as unknown as IntersectionObserverEntry], {} as IntersectionObserver))
    expect(a).toHaveAttribute('data-reveal', 'shown')
    expect(b).toHaveAttribute('data-reveal', 'hidden')
    expect(unobserve).toHaveBeenCalledWith(a)
  })
})
