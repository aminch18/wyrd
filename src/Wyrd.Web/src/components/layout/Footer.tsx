import styles from '../../styles/site.module.css'
import { GITHUB_URL, NUGET_URL } from '../../app/links'
import { routePath } from '../../app/navigation'
import { Brand } from './Brand'

export function Footer() {
    return (
        <footer>
            <div
                className={
                    styles['section-shell'] + ' ' + styles['footer-inner']
                }
            >
                <div>
                    <Brand />
                    <p>Deterministic event-driven processes for .NET.</p>
                </div>
                <div className={styles['footer-links']}>
                    <a href={routePath('docs')}>Documentation</a>
                    <a href={GITHUB_URL} target="_blank" rel="noreferrer">
                        GitHub
                    </a>
                    <a href={NUGET_URL} target="_blank" rel="noreferrer">
                        NuGet
                    </a>
                </div>
                <p className={styles['footer-note']}>Pre-1.0 · MIT licensed</p>
            </div>
        </footer>
    )
}
