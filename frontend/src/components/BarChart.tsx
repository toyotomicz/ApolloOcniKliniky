import { useState } from 'react'
import { formatShortDay, pluralize } from '../lib/format'

interface BarChartProps {
  data: { date: string; count: number }[]
  /** Accessible title, e.g. "Registrace za posledních 7 dní". */
  title: string
}

/** Column with a 4px rounded data-end and a square base on the baseline. */
function columnPath(x: number, y: number, width: number, height: number): string {
  const r = Math.min(4, height, width / 2)
  const base = y + height
  return `M${x},${base} V${y + r} Q${x},${y} ${x + r},${y} H${x + width - r} Q${x + width},${y} ${x + width},${y + r} V${base} Z`
}

/**
 * Dependency-free SVG column chart (one series, today emphasised). Every column is a hover/focus target with a
 * tooltip; a visually hidden table carries all values for screen readers and keyboard users.
 */
export function BarChart({ data, title }: BarChartProps) {
  const [active, setActive] = useState<number | null>(null)
  const width = 560
  const height = 220
  const padding = { top: 24, right: 8, bottom: 44, left: 32 }
  const innerWidth = width - padding.left - padding.right
  const innerHeight = height - padding.top - padding.bottom

  const max = Math.max(...data.map((d) => d.count), 0)
  // Round the scale up to a "nice" value so grid lines are whole numbers.
  const niceMax = max <= 4 ? 4 : Math.ceil(max / 4) * 4
  const ticks = [0, niceMax / 4, niceMax / 2, (niceMax * 3) / 4, niceMax]
  const slot = innerWidth / Math.max(data.length, 1)
  const barWidth = Math.min(24, slot * 0.62)
  const dense = data.length > 10
  // Selective direct labels: today's value and the highest day; the tooltip and table carry the rest.
  const maxIndex = data.reduce((best, d, i) => (d.count > (data[best]?.count ?? -1) ? i : best), 0)

  const describe = (d: { date: string; count: number }, isToday: boolean) =>
    `${isToday ? 'Dnes' : formatShortDay(d.date).day}: ${d.count} ${pluralize(d.count, 'registrace', 'registrace', 'registrací')}`

  return (
    <figure className="chart-figure" style={{ margin: 0 }}>
      <svg className="bar-chart" viewBox={`0 0 ${width} ${height}`} role="img" aria-label={title}>
        {ticks.map((tick) => {
          const y = padding.top + innerHeight - (tick / niceMax) * innerHeight
          return (
            <g key={tick}>
              <line className="grid-line" x1={padding.left} x2={width - padding.right} y1={y} y2={y} />
              <text x={padding.left - 8} y={y + 4} textAnchor="end">
                {tick}
              </text>
            </g>
          )
        })}
        {data.map((d, i) => {
          const barHeight = (d.count / niceMax) * innerHeight
          const x = padding.left + slot * i + (slot - barWidth) / 2
          const y = padding.top + innerHeight - barHeight
          const isToday = i === data.length - 1
          const label = formatShortDay(d.date)
          const showAxisLabel = !dense || isToday || i % 5 === 0
          const showValue = d.count > 0 && (isToday || i === maxIndex || !dense)
          return (
            <g key={d.date} data-testid="chart-bar">
              {d.count > 0 && (
                <path
                  className={`bar ${isToday ? 'bar-today' : ''} ${active === i ? 'bar-active' : ''}`}
                  d={columnPath(x, y, barWidth, Math.max(barHeight, 2))}
                />
              )}
              {showValue && (
                <text className="bar-value" x={x + barWidth / 2} y={y - 6} textAnchor="middle">
                  {d.count}
                </text>
              )}
              {showAxisLabel && (
                <>
                  <text x={x + barWidth / 2} y={height - padding.bottom + 18} textAnchor="middle">
                    {isToday ? 'dnes' : dense ? label.day : label.weekday}
                  </text>
                  {!dense && (
                    <text x={x + barWidth / 2} y={height - padding.bottom + 34} textAnchor="middle">
                      {label.day}
                    </text>
                  )}
                </>
              )}
              {/* Hit target: the whole column slot, larger than the mark itself. */}
              <rect
                className="bar-hit"
                x={padding.left + slot * i}
                y={padding.top}
                width={slot}
                height={innerHeight}
                tabIndex={0}
                aria-label={describe(d, isToday)}
                onPointerEnter={() => setActive(i)}
                onPointerLeave={() => setActive(null)}
                onFocus={() => setActive(i)}
                onBlur={() => setActive(null)}
              />
            </g>
          )
        })}
      </svg>
      {active !== null && data[active] && (
        <div
          className="chart-tooltip"
          role="presentation"
          style={{ left: `${((padding.left + slot * (active + 0.5)) / width) * 100}%` }}
        >
          <strong>{data[active].count}</strong> {pluralize(data[active].count, 'registrace', 'registrace', 'registrací')}
          <span>{active === data.length - 1 ? 'dnes' : `${formatShortDay(data[active].date).weekday} ${formatShortDay(data[active].date).day}`}</span>
        </div>
      )}
      <table className="visually-hidden">
        <caption>{title}</caption>
        <thead>
          <tr>
            <th scope="col">Den</th>
            <th scope="col">Počet registrací</th>
          </tr>
        </thead>
        <tbody>
          {data.map((d) => (
            <tr key={d.date}>
              <td>{d.date}</td>
              <td>{d.count}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  )
}

export interface HorizontalBarRow {
  key: string
  label: string
  value: number
  /** Secondary text after the value, e.g. a percentage. */
  note?: string
}

/** Single-hue horizontal bars with the value at the tip (used for the funnel and the plan breakdown). */
export function HorizontalBars({ rows, label }: { rows: HorizontalBarRow[]; label: string }) {
  const max = Math.max(...rows.map((r) => r.value), 1)
  return (
    <ul className="hbar-list" aria-label={label}>
      {rows.map((row) => (
        <li key={row.key}>
          <span className="hbar-label">{row.label}</span>
          <span className="hbar-track" aria-hidden="true">
            <span className="hbar-fill" style={{ width: `${Math.max((row.value / max) * 100, row.value > 0 ? 2 : 0)}%` }} />
          </span>
          <span className="hbar-value">
            <strong>{row.value}</strong>
            {row.note && <span className="muted"> {row.note}</span>}
          </span>
        </li>
      ))}
    </ul>
  )
}
