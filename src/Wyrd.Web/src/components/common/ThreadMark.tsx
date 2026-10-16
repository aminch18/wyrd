import styles from '../../styles/site.module.css'
import { useId } from 'react'

export function ThreadMark({ compact = false }: { compact?: boolean }) {
    const gradientId = useId()
    const glowId = useId()

    return (
        <div
            className={[
                styles['thread-mark'],
                compact ? styles.compact : '',
            ].join(' ')}
            aria-hidden="true"
        >
            <svg viewBox="0 0 680 420">
                <defs>
                    <linearGradient
                        id={gradientId}
                        x1="90"
                        y1="60"
                        x2="590"
                        y2="360"
                        gradientUnits="userSpaceOnUse"
                    >
                        <stop stopColor="#4ed8b1" />
                        <stop offset="0.48" stopColor="#6ca89d" />
                        <stop offset="1" stopColor="#9ce5d0" />
                    </linearGradient>
                    <filter
                        id={glowId}
                        x="-30%"
                        y="-30%"
                        width="160%"
                        height="160%"
                    >
                        <feGaussianBlur stdDeviation="12" result="blur" />
                        <feMerge>
                            <feMergeNode in="blur" />
                            <feMergeNode in="SourceGraphic" />
                        </feMerge>
                    </filter>
                </defs>
                <path
                    className={styles['thread-faint']}
                    d="M58 335C154 137 222 116 300 246C354 336 437 350 618 82"
                />
                <path
                    className={styles['thread-faint']}
                    d="M66 90C175 305 252 310 337 188C398 102 478 96 615 325"
                />
                <path
                    className={styles['thread-main']}
                    style={{
                        stroke: `url(#${gradientId})`,
                        filter: `url(#${glowId})`,
                    }}
                    d="M66 90C175 305 252 310 337 188C398 102 478 96 615 325"
                />
                <path
                    className={styles['thread-main'] + ' ' + styles['delayed']}
                    style={{
                        stroke: `url(#${gradientId})`,
                        filter: `url(#${glowId})`,
                    }}
                    d="M58 335C154 137 222 116 300 246C354 336 437 350 618 82"
                />
                <circle cx="66" cy="90" r="7" />
                <circle cx="337" cy="188" r="7" />
                <circle cx="615" cy="325" r="7" />
            </svg>
        </div>
    )
}
