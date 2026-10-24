import styles from '../../styles/site.module.css'
import type { CSSProperties } from 'react'
import { routePath } from '../../app/navigation'

const logoStyle = (fileName: string) =>
    ({
        backgroundImage: `url(${import.meta.env.BASE_URL}${fileName})`,
    }) satisfies CSSProperties

export function Brand() {
    return (
        <a
            className={styles['brand']}
            href={routePath('home')}
            aria-label="Wyrd home"
        >
            <span
                className={
                    styles['brand-image'] + ' ' + styles['brand-image-dark']
                }
                style={logoStyle('wyrd-logo-dark.png')}
                aria-hidden="true"
            />
            <span
                className={
                    styles['brand-image'] + ' ' + styles['brand-image-light']
                }
                style={logoStyle('wyrd-logo-light.png')}
                aria-hidden="true"
            />
        </a>
    )
}
