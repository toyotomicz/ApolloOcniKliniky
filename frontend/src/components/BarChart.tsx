import { formatShortDay } from '../lib/format'

interface BarChartProps {
  data: { date: string; count: number }[]
  /** Accessible title, e.g. "Registrace za posledních 7 dní". */
  title: string
}

/** Dependency-free SVG bar chart with an equivalent visually hidden table for screen readers. */
export function BarChart({ data, title }: BarChartProps) {
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
  const barWidth = Math.min(44, slot * 0.6)

  return (
    <figure style={{ margin: 0 }}>
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
          return (
            <g key={d.date} data-testid="chart-bar">
              <rect className={isToday ? 'bar bar-today' : 'bar'} x={x} y={y} width={barWidth} height={Math.max(barHeight, d.count > 0 ? 2 : 0)} rx={3} />
              {d.count > 0 && (
                <text className="bar-value" x={x + barWidth / 2} y={y - 6} textAnchor="middle">
                  {d.count}
                </text>
              )}
              <text x={x + barWidth / 2} y={height - padding.bottom + 18} textAnchor="middle">
                {isToday ? 'dnes' : label.weekday}
              </text>
              <text x={x + barWidth / 2} y={height - padding.bottom + 34} textAnchor="middle">
                {label.day}
              </text>
            </g>
          )
        })}
      </svg>
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
