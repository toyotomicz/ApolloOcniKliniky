const RING_RADII = [150, 205, 270, 345, 420, 490]

/**
 * Decorative iris-like rings behind the landing hero (the portal is for eye clinics).
 * Purely visual: hidden from assistive technology, motion disabled for prefers-reduced-motion.
 */
export function HeroIris() {
  return (
    <div className="hero-iris" aria-hidden="true">
      <svg viewBox="0 0 1000 1000" focusable="false">
        {RING_RADII.map((r) => (
          <circle key={r} className="hero-iris-ring" cx="500" cy="500" r={r} />
        ))}
        <circle className="hero-iris-fibres" cx="500" cy="500" r="238" />
        <circle className="hero-iris-accent" cx="500" cy="500" r="308" pathLength={100} />
      </svg>
    </div>
  )
}
