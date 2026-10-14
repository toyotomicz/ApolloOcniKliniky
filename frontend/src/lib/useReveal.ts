import { useLayoutEffect, type RefObject } from 'react'

const STAGGER_MS = 90

/**
 * Reveals the elements matching `selector` inside the container as they scroll into view, one after another.
 * Elements are only hidden once the observer is in place, and nothing is hidden without IntersectionObserver
 * or with prefers-reduced-motion, so the content never depends on the animation.
 */
export function useReveal(containerRef: RefObject<HTMLElement | null>, selector: string, ready: boolean) {
  useLayoutEffect(() => {
    const container = containerRef.current
    if (!ready || !container || typeof IntersectionObserver === 'undefined') return
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return

    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) continue
          entry.target.setAttribute('data-reveal', 'shown')
          observer.unobserve(entry.target)
        }
      },
      { threshold: 0.15 },
    )
    container.querySelectorAll<HTMLElement>(selector).forEach((element, index) => {
      element.style.setProperty('--reveal-delay', `${index * STAGGER_MS}ms`)
      element.setAttribute('data-reveal', 'hidden')
      observer.observe(element)
    })
    return () => observer.disconnect()
  }, [containerRef, selector, ready])
}
