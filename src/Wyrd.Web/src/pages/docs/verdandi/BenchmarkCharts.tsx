import { useState } from 'react'
import {
    COMMANDS,
    HOT_FAILED,
    MACHINE,
    WORKERS,
    type Point,
    type Series,
} from './benchmarks'
import css from './verdandi.module.css'

const W = 640
const H = 300
const PAD = { top: 20, right: 150, bottom: 44, left: 64 }
const number = new Intl.NumberFormat('en-US')

interface Hover {
    series: Series
    point: Point
    when: 'before' | 'after'
    cx: number
    cy: number
}

function niceMax(value: number) {
    const step = 10 ** Math.floor(Math.log10(value))
    return Math.ceil(value / step) * step
}

interface ChartProps {
    title: string
    unit: string
    xLabel: string
    data: Series[]
}

function LineChart({ title, unit, xLabel, data }: ChartProps) {
    const [hover, setHover] = useState<Hover | null>(null)
    const [asTable, setAsTable] = useState(false)
    const xs = [...new Set(data.flatMap((s) => s.before.map((p) => p.x)))].sort(
        (a, b) => a - b,
    )
    const top = niceMax(
        Math.max(
            ...data.flatMap((s) => [...s.before, ...s.after].map((p) => p.y)),
        ),
    )
    const ticks = [0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(top * f))
    const plotW = W - PAD.left - PAD.right
    const plotH = H - PAD.top - PAD.bottom
    // Callers and workers are counts we chose, not a continuous scale: evenly spaced.
    const x = (value: number) =>
        PAD.left + (xs.indexOf(value) / (xs.length - 1)) * plotW
    const y = (value: number) => PAD.top + plotH - (value / top) * plotH
    const line = (points: Point[]) =>
        points.map((p, i) => `${i ? 'L' : 'M'}${x(p.x)},${y(p.y)}`).join(' ')

    return (
        <figure className={css.chart}>
            <div className={css.chartHead}>
                <h4>{title}</h4>
                <ul className={css.legend}>
                    {data.map((s) => (
                        <li key={s.id}>
                            <span
                                className={`${css.swatch} ${css[`series-${s.id}`]}`}
                            />{' '}
                            {s.label}
                        </li>
                    ))}
                    <li>
                        <svg
                            width="14"
                            height="14"
                            aria-hidden
                            className={css.legendMark}
                        >
                            <circle cx="7" cy="7" r="4" />
                        </svg>
                        before
                    </li>
                    <li>
                        <svg
                            width="14"
                            height="14"
                            aria-hidden
                            className={css.legendMark}
                        >
                            <path d="M7 1.5 L12.5 7 L7 12.5 L1.5 7 Z" />
                        </svg>
                        after the cheaper messages of 0.5
                    </li>
                </ul>
                <button
                    type="button"
                    className={css.linkButton}
                    onClick={() => setAsTable((t) => !t)}
                >
                    {asTable ? 'Show the chart' : 'Show as a table'}
                </button>
            </div>

            {asTable ? (
                <div className={css.dataTable}>
                    <table>
                        <thead>
                            <tr>
                                <th>Keys</th>
                                <th>{xLabel}</th>
                                <th>Before ({unit})</th>
                                <th>After ({unit})</th>
                            </tr>
                        </thead>
                        <tbody>
                            {data.flatMap((s) =>
                                s.before.map((p) => {
                                    const after = s.after.filter(
                                        (a) => a.x === p.x,
                                    )
                                    return (
                                        <tr key={`${s.id}-${p.x}`}>
                                            <td>{s.label}</td>
                                            <td>{p.x}</td>
                                            <td>{number.format(p.y)}</td>
                                            <td>
                                                {after.length === 0
                                                    ? 'not measured'
                                                    : after
                                                          .map((a) =>
                                                              number.format(
                                                                  a.y,
                                                              ),
                                                          )
                                                          .join(' and ')}
                                            </td>
                                        </tr>
                                    )
                                }),
                            )}
                        </tbody>
                    </table>
                </div>
            ) : (
                <div className={css.plot}>
                    <svg
                        viewBox={`0 0 ${W} ${H}`}
                        role="img"
                        aria-label={`${title}: ${unit} by ${xLabel.toLowerCase()}`}
                    >
                        {ticks.map((t) => (
                            <g key={t}>
                                <line
                                    x1={PAD.left}
                                    x2={W - PAD.right}
                                    y1={y(t)}
                                    y2={y(t)}
                                    className={css.grid}
                                />
                                <text
                                    x={PAD.left - 10}
                                    y={y(t)}
                                    className={css.tickY}
                                >
                                    {number.format(t)}
                                </text>
                            </g>
                        ))}
                        {xs.map((v) => (
                            <text
                                key={v}
                                x={x(v)}
                                y={H - PAD.bottom + 20}
                                className={css.tickX}
                            >
                                {v}
                            </text>
                        ))}
                        <text
                            x={PAD.left + plotW / 2}
                            y={H - 6}
                            className={css.axisLabel}
                        >
                            {xLabel}
                        </text>
                        <text
                            x={14}
                            y={PAD.top + plotH / 2}
                            className={css.axisLabel}
                            transform={`rotate(-90 14 ${PAD.top + plotH / 2})`}
                        >
                            {unit}
                        </text>

                        {data.map((s) => {
                            const end = s.before[s.before.length - 1]
                            return (
                                <g key={s.id} className={css[`series-${s.id}`]}>
                                    <path
                                        d={line(s.before)}
                                        className={css.seriesLine}
                                    />
                                    {s.before.map((p) => (
                                        <circle
                                            key={p.x}
                                            cx={x(p.x)}
                                            cy={y(p.y)}
                                            r={4.5}
                                            className={css.marker}
                                        />
                                    ))}
                                    {s.after.map((p, i) => {
                                        const cx = x(p.x)
                                        const cy = y(p.y)
                                        return (
                                            <path
                                                key={`${p.x}-${i}`}
                                                d={`M${cx} ${cy - 7} L${cx + 7} ${cy} L${cx} ${cy + 7} L${cx - 7} ${cy} Z`}
                                                className={css.markerAfter}
                                            />
                                        )
                                    })}
                                    <text
                                        x={x(end.x) + 12}
                                        y={y(end.y)}
                                        className={css.directLabel}
                                    >
                                        {s.label}
                                    </text>
                                </g>
                            )
                        })}

                        {data.flatMap((s) =>
                            (['before', 'after'] as const).flatMap((when) =>
                                s[when].map((p, i) => {
                                    const cx = x(p.x)
                                    const cy = y(p.y)
                                    return (
                                        <circle
                                            key={`${s.id}-${when}-${p.x}-${i}`}
                                            cx={cx}
                                            cy={cy}
                                            r={13}
                                            className={css.hit}
                                            tabIndex={0}
                                            aria-label={`${s.label}, ${xLabel.toLowerCase()} ${p.x}, ${when}: ${number.format(p.y)} ${unit}${p.note ? `, ${p.note}` : ''}`}
                                            onMouseEnter={() =>
                                                setHover({
                                                    series: s,
                                                    point: p,
                                                    when,
                                                    cx,
                                                    cy,
                                                })
                                            }
                                            onFocus={() =>
                                                setHover({
                                                    series: s,
                                                    point: p,
                                                    when,
                                                    cx,
                                                    cy,
                                                })
                                            }
                                            onMouseLeave={() => setHover(null)}
                                            onBlur={() => setHover(null)}
                                        />
                                    )
                                }),
                            ),
                        )}
                    </svg>
                    {hover && (
                        <div
                            className={css.tooltip}
                            style={{
                                left: `${(hover.cx / W) * 100}%`,
                                top: `${(hover.cy / H) * 100}%`,
                            }}
                            role="status"
                        >
                            <strong>{number.format(hover.point.y)}</strong>{' '}
                            {unit}
                            <span>
                                {hover.series.label} · {hover.point.x}{' '}
                                {xLabel.toLowerCase()} · {hover.when}
                                {hover.point.note
                                    ? ` (${hover.point.note})`
                                    : ''}
                            </span>
                        </div>
                    )}
                </div>
            )}
        </figure>
    )
}

export function BenchmarkCharts() {
    return (
        <div className={css.charts}>
            <LineChart
                title="Commands from an API: 2,000 SendAsync calls per run"
                unit="commands/s"
                xLabel="Callers"
                data={COMMANDS}
            />
            <p className={css.chartNote}>
                On the same SKU, calls still conflicting after 5 attempts, of
                2,000:{' '}
                {HOT_FAILED.before
                    .map(
                        (f) =>
                            `${number.format(f.failed)} with ${f.callers} callers`,
                    )
                    .join(', ')}{' '}
                before;{' '}
                {HOT_FAILED.after
                    .map(
                        (f) =>
                            `${number.format(f.failed)} with ${f.callers} callers`,
                    )
                    .join(', ')}{' '}
                after (a conflict is still retried at once).
            </p>
            <LineChart
                title="Workers draining 1,000 placed and paid orders"
                unit="messages/s"
                xLabel="Workers"
                data={WORKERS}
            />
            <p className={css.chartNote}>
                {MACHINE}. Only the rows measured after are drawn after;
                everything comes from <code>benchmarks/README.md</code>, and a
                container on a laptop is not a production database: compare the
                runs, don’t read them as capacity.
            </p>
        </div>
    )
}
